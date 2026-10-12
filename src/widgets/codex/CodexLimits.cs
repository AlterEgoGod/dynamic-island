using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DynamicIsland
{
    internal sealed class QuotaWindow
    {
        public double RemainingPercent;
        public int Minutes;
        public DateTime? ResetsAt;
    }

    internal sealed class LimitsSnapshot
    {
        public QuotaWindow FiveHour, Weekly;
        public string Plan = "", BucketName = "";
        public DateTime UpdatedAt;
        public string PlanLabel { get { return WeeklyPrimary ? "Business Premium" : Plan == "plus" ? "Plus" : Plan == "pro" ? "Pro" : Plan == "business" ? "Business" : Plan == "enterprise" ? "Enterprise" : "ChatGPT"; } }
        public bool WeeklyPrimary { get { return string.Equals(Plan, "self_serve_business_prolite", StringComparison.OrdinalIgnoreCase); } }
        public QuotaWindow MainQuota { get { return WeeklyPrimary ? Weekly : FiveHour; } }
        public bool HasData { get { return FiveHour != null || Weekly != null; } }

        public static LimitsSnapshot Parse(IDictionary<string, object> result, string accountPlan)
        {
            LimitsSnapshot value = new LimitsSnapshot { Plan = accountPlan ?? "", UpdatedAt = DateTime.UtcNow };
            IDictionary<string, object> bucket = Json.Map(Json.Get(result, "rateLimits"));
            IDictionary<string, object> buckets = Json.Map(Json.Get(result, "rateLimitsByLimitId"));
            // Prefer the Codex bucket. Never substitute another model's quota for a missing Codex bucket.
            if (buckets != null && Json.Map(Json.Get(buckets, "codex")) != null) bucket = Json.Map(Json.Get(buckets, "codex"));
            if (bucket == null) return value;
            string id = Json.Text(bucket, "limitId");
            if (!string.IsNullOrEmpty(id) && id != "codex") return value;
            string plan = Json.Text(bucket, "planType"); if (!string.IsNullOrEmpty(plan)) value.Plan = plan;
            value.BucketName = Json.Text(bucket, "limitName");
            foreach (string key in new[] { "primary", "secondary" })
            {
                IDictionary<string, object> window = Json.Map(Json.Get(bucket, key));
                if (window == null) continue;
                double used, minutes;
                if (!Json.Number(window, "usedPercent", out used) || !Json.Number(window, "windowDurationMins", out minutes) || minutes <= 0) continue;
                QuotaWindow quota = new QuotaWindow { RemainingPercent = Math.Max(0, Math.Min(100, 100 - used)), Minutes = (int)minutes };
                double epoch;
                if (Json.Number(window, "resetsAt", out epoch))
                {
                    try { quota.ResetsAt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(epoch); } catch (ArgumentOutOfRangeException) { }
                }
                // Match by duration, not by position: Business plans can return the weekly window as primary.
                if (quota.Minutes == 300) value.FiveHour = quota;
                else if (quota.Minutes == 10080) value.Weekly = quota;
            }
            return value;
        }
    }

    internal static class Json
    {
        public static IDictionary<string, object> Map(object value) { return value as IDictionary<string, object>; }
        public static object Get(IDictionary<string, object> map, string key) { object value; return map != null && map.TryGetValue(key, out value) ? value : null; }
        public static string Text(IDictionary<string, object> map, string key) { return Get(map, key) as string ?? ""; }
        public static bool Number(IDictionary<string, object> map, string key, out double value)
        {
            object raw = Get(map, key); value = 0;
            return raw != null && !(raw is bool) && double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);
        }
        public static IDictionary<string, object> Parse(string json) { return Map(new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.DeserializeObject(json)); }
    }

    internal static class CodexPath
    {
        public static string Find(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (IsExecutable(configured)) return Path.GetFullPath(configured);
            }
            string beside = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "codex-path.txt");
            if (File.Exists(beside))
            {
                string explicitPath = File.ReadAllText(beside).Trim().Trim('"');
                if (IsExecutable(explicitPath)) return Path.GetFullPath(explicitPath);
            }
            List<string> paths = new List<string> { Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "codex.exe") };
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
                if (!string.IsNullOrWhiteSpace(directory)) paths.Add(Path.Combine(directory.Trim().Trim('"'), "codex.exe"));
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (string editor in new[] { ".vscode", ".vscode-insiders", ".cursor" })
            {
                string extensions = Path.Combine(profile, editor, "extensions");
                if (!Directory.Exists(extensions)) continue;
                try
                {
                    foreach (string directory in Directory.GetDirectories(extensions, "openai.chatgpt-*").OrderByDescending(Directory.GetLastWriteTimeUtc))
                        paths.Add(Path.Combine(directory, "bin", "windows-x86_64", "codex.exe"));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            string npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai");
            paths.Add(Path.Combine(npm, "codex", "vendor", "x86_64-pc-windows-msvc", "codex", "codex.exe"));
            paths.Add(Path.Combine(npm, "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "codex", "codex.exe"));
            foreach (string path in paths) if (IsExecutable(path)) return Path.GetFullPath(path);
            throw new InvalidOperationException("Codex не найден. Установите Codex и повторите вход через браузер.");
        }
        private static bool IsExecutable(string path)
        {
            try { return string.Equals(Path.GetFileName(path), "codex.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path); }
            catch (ArgumentException) { return false; }
        }
    }

    // Only account/read, account/rateLimits/read and user-initiated login are used; no inference is started.
    internal sealed class LimitsClient : IDisposable
    {
        private readonly SemaphoreSlim operation = new SemaphoreSlim(1, 1);
        private readonly object sync = new object();
        private readonly Dictionary<int, TaskCompletionSource<IDictionary<string, object>>> pending = new Dictionary<int, TaskCompletionSource<IDictionary<string, object>>>();
        private Process server;
        private int nextId;
        private bool ready, disposed;
        private long loginExpires;
        public bool LoggingIn { get { return Interlocked.Read(ref loginExpires) > DateTime.UtcNow.Ticks; } }
        public string ConfiguredPath = "";
        public event Action LoginCompleted;

        public async Task<LimitsSnapshot> Read()
        {
            await operation.WaitAsync().ConfigureAwait(false);
            try
            {
                await Connect().ConfigureAwait(false);
                IDictionary<string, object> accountReply = await Request("account/read", new { refreshToken = false }).ConfigureAwait(false);
                IDictionary<string, object> account = Json.Map(Json.Get(accountReply, "account"));
                if (account == null || Json.Text(account, "type") != "chatgpt") throw new InvalidOperationException("Для лимитов нужен вход в Codex через ChatGPT.");
                IDictionary<string, object> reply = await Request("account/rateLimits/read", null).ConfigureAwait(false);
                return LimitsSnapshot.Parse(reply, Json.Text(account, "planType"));
            }
            catch { Disconnect(); throw; }
            finally { operation.Release(); }
        }

        public async Task<string> BeginLogin()
        {
            await operation.WaitAsync().ConfigureAwait(false);
            try
            {
                await Connect().ConfigureAwait(false);
                Interlocked.Exchange(ref loginExpires, DateTime.UtcNow.AddMinutes(5).Ticks);
                IDictionary<string, object> reply = await Request("account/login/start", new { type = "chatgpt" }).ConfigureAwait(false);
                string url = Json.Text(reply, "authUrl");
                Uri uri;
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != "https" || !(uri.Host == "auth.openai.com" || uri.Host == "chatgpt.com")) throw new InvalidOperationException("Codex не вернул адрес для входа через ChatGPT.");
                return url;
            }
            catch { Interlocked.Exchange(ref loginExpires, 0); Disconnect(); throw; }
            finally { operation.Release(); }
        }

        private async Task Connect()
        {
            if (disposed) throw new ObjectDisposedException("LimitsClient");
            if (ready && server != null && !server.HasExited) return;
            Disconnect();
            string executable = CodexPath.Find(ConfiguredPath);
            Process process = new Process { StartInfo = new ProcessStartInfo(executable, "app-server --listen stdio://") {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            }};
            process.ErrorDataReceived += delegate { }; // Drain stderr; never write auth or protocol payloads to a log.
            if (!process.Start()) throw new InvalidOperationException("Не удалось запустить Codex.");
            lock (sync) server = process;
            process.BeginErrorReadLine();
            Task reader = Pump(process);
            await Request("initialize", new { clientInfo = new { name = "dynamic_island", title = "Dynamic Island", version = "1.1.0" } }).ConfigureAwait(false);
            Send(new { method = "initialized", @params = new { } }); ready = true;
        }

        private async Task Pump(Process process)
        {
            try
            {
                while (!disposed)
                {
                    string line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                    if (line == null) break;
                    IDictionary<string, object> message;
                    try { message = Json.Parse(line); } catch (ArgumentException) { continue; }
                    double idValue;
                    if (Json.Number(message, "id", out idValue) && (Json.Get(message, "result") != null || Json.Get(message, "error") != null))
                    {
                        TaskCompletionSource<IDictionary<string, object>> completion = null;
                        lock (sync) { if (pending.TryGetValue((int)idValue, out completion)) pending.Remove((int)idValue); }
                        if (completion == null) continue;
                        if (Json.Get(message, "error") != null)
                            completion.TrySetException(new InvalidOperationException("Codex не смог получить лимиты. Проверьте вход и подключение."));
                        else completion.TrySetResult(Json.Map(Json.Get(message, "result")));
                    }
                    else if (Json.Text(message, "method") == "account/login/completed")
                    {
                        Interlocked.Exchange(ref loginExpires, 0);
                        IDictionary<string, object> parameters = Json.Map(Json.Get(message, "params"));
                        if (object.Equals(Json.Get(parameters, "success"), true)) { Action handler = LoginCompleted; if (handler != null) handler(); }
                    }
                    else if (Json.Get(message, "id") != null && Json.Get(message, "method") != null)
                    {
                        Send(new { id = Json.Get(message, "id"), error = new { code = -32601, message = "This client only reads account limits." } });
                    }
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            finally
            {
                lock (sync)
                {
                    if (server == process)
                    {
                        ready = false;
                        foreach (var request in pending.Values) request.TrySetException(new IOException("Соединение с Codex закрыто."));
                        pending.Clear();
                    }
                }
            }
        }

        private async Task<IDictionary<string, object>> Request(string method, object parameters)
        {
            int id = Interlocked.Increment(ref nextId);
            TaskCompletionSource<IDictionary<string, object>> completion = new TaskCompletionSource<IDictionary<string, object>>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (sync) pending.Add(id, completion);
            try
            {
                Send(new { id = id, method = method, @params = parameters });
                Task finished = await Task.WhenAny(completion.Task, Task.Delay(20000)).ConfigureAwait(false);
                if (finished != completion.Task) throw new TimeoutException("Codex не ответил за 20 секунд. Проверьте подключение.");
                return await completion.Task.ConfigureAwait(false);
            }
            finally { lock (sync) pending.Remove(id); }
        }
        private void Send(object message)
        {
            string line = new JavaScriptSerializer().Serialize(message);
            lock (sync)
            {
                if (disposed || server == null || server.HasExited) throw new IOException("Соединение с Codex закрыто.");
                server.StandardInput.WriteLine(line); server.StandardInput.Flush();
            }
        }
        private void Disconnect()
        {
            Process process;
            lock (sync)
            {
                process = server; server = null; ready = false;
                foreach (var completion in pending.Values) completion.TrySetCanceled();
                pending.Clear();
            }
            if (process != null)
            {
                try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                process.Dispose();
            }
        }
        public void Dispose() { disposed = true; Disconnect(); }
    }
}
