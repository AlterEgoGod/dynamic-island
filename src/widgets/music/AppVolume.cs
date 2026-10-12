using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DynamicIsland
{
    // Session volume belongs to an application, so browser tabs share this control.
    // It never changes the endpoint/master volume or falls back to another process.
    internal static class AppVolume
    {
        public static string ProcessName(string appId)
        {
            string id = (appId ?? "").ToLowerInvariant();
            foreach (string name in new[] { "chrome", "msedge", "firefox", "brave", "opera", "spotify", "dynamicisland" })
                if (id == name || id == name + ".exe" || (name == "spotify" && id.StartsWith("spotifyab.spotify"))) return name;
            if (id == "browser.exe") return "browser";
            if (id.EndsWith(".exe", StringComparison.Ordinal))
                try { return Path.GetFileNameWithoutExtension(id); } catch (ArgumentException) { }
            return null;
        }
        private static bool Matches(string appId, Process process)
        {
            if ((appId ?? "").StartsWith("pid:", StringComparison.Ordinal)) return appId == ProcessId(process);
            if ((appId ?? "").StartsWith("exe:", StringComparison.Ordinal)) return string.Equals(appId.Substring(4), process.MainModule.FileName, StringComparison.OrdinalIgnoreCase);
            string name = ProcessName(appId);
            if (name != null) return string.Equals(name, process.ProcessName, StringComparison.OrdinalIgnoreCase);
            uint length = 512; StringBuilder identity = new StringBuilder((int)length);
            return GetApplicationUserModelId(process.Handle, ref length, identity) == 0 && string.Equals(appId, identity.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        private static string ProcessId(Process process) { return "pid:" + process.Id + ":" + process.StartTime.ToUniversalTime().Ticks; }
        public static List<MediaTrack> Discover(List<MediaTrack> represented)
        {
            List<MediaTrack> result = new List<MediaTrack>();
            Visit(delegate(Process process, object control)
            {
                using (Process self = Process.GetCurrentProcess()) if (process.Id == self.Id) return;
                int state; ((IAudioSessionControl2)control).GetState(out state); if (state == 2) return;
                foreach (MediaTrack existing in represented)
                    try { if (Matches(existing.AppId, process)) return; } catch (Exception) { }
                string identity = ProcessId(process);
                string appId = identity;
                string name = MediaSourceNames.Fallback(process.ProcessName + ".exe");
                try { appId = "exe:" + process.MainModule.FileName.ToLowerInvariant(); string description = process.MainModule.FileVersionInfo.FileDescription; if (!string.IsNullOrWhiteSpace(description)) name = description; } catch (Exception) { }
                if (result.Exists(delegate(MediaTrack item) { return item.AppId == appId; })) return;
                result.Add(new MediaTrack { Id = process.ProcessName.Equals("wmplayer", StringComparison.OrdinalIgnoreCase) ? "wmp:" + identity.Substring(4) : "audio:" + appId, AppId = appId, Source = name, PlaybackKnown = false,
                    CompatibilityNote = "Доступна громкость приложения. Этот источник не передаёт управление треками; используйте его собственный плеер." });
            });
            return result;
        }
        public static double? Read(string appId) { bool changed; return Access(appId, null, out changed); }
        public static bool Set(string appId, double volume)
        {
            if (double.IsNaN(volume) || double.IsInfinity(volume)) return false;
            bool changed; Access(appId, Math.Max(0, Math.Min(1, volume)), out changed); return changed;
        }
        private static void Release(object value) { try { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); } catch (Exception) { } }
        private static double? Access(string appId, double? write, out bool changed)
        {
            bool written = false; double? result = null;
            Visit(delegate(Process process, object control)
            {
                if (!Matches(appId, process)) return;
                int state; ((IAudioSessionControl2)control).GetState(out state); if (state == 2) return;
                ISimpleAudioVolume volume = (ISimpleAudioVolume)control;
                float current; bool muted; volume.GetMasterVolume(out current); volume.GetMute(out muted);
                if (!result.HasValue) result = muted ? 0 : current;
                if (write.HasValue)
                {
                    Guid context = Guid.Empty;
                    volume.SetMasterVolume((float)write.Value, ref context); volume.SetMute(false, ref context); written = true;
                }
            });
            changed = written; return result;
        }
        private static void Visit(Action<Process, object> visit)
        {
            object enumeratorObject = null; IMMDeviceCollection devices = null;
            try
            {
                enumeratorObject = new DeviceEnumerator();
                IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)enumeratorObject;
                enumerator.EnumAudioEndpoints(0, 1, out devices);
                uint count; devices.GetCount(out count);
                for (uint d = 0; d < count; d++)
                {
                    IMMDevice device = null; object managerObject = null; IAudioSessionEnumerator sessions = null;
                    try
                    {
                        devices.Item(d, out device); Guid iid = typeof(IAudioSessionManager2).GUID;
                        device.Activate(ref iid, 23, IntPtr.Zero, out managerObject);
                        ((IAudioSessionManager2)managerObject).GetSessionEnumerator(out sessions);
                        int total; sessions.GetCount(out total);
                        for (int index = 0; index < total; index++)
                        {
                            object control = null;
                            try
                            {
                                sessions.GetSession(index, out control);
                                uint pid; ((IAudioSessionControl2)control).GetProcessId(out pid);
                                if (pid == 0) continue;
                                using (Process process = Process.GetProcessById((int)pid)) visit(process, control);
                            }
                            catch (Exception) { }
                            finally { Release(control); }
                        }
                    }
                    catch (Exception) { }
                    finally { Release(sessions); Release(managerObject); Release(device); }
                }
            }
            catch (Exception) { }
            finally { Release(devices); Release(enumeratorObject); }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder identity);
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class DeviceEnumerator { }
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator { void EnumAudioEndpoints(int flow, uint state, out IMMDeviceCollection devices); }
        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection { void GetCount(out uint count); void Item(uint index, out IMMDevice device); }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice { void Activate(ref Guid iid, uint context, IntPtr activation, [MarshalAs(UnmanagedType.IUnknown)] out object result); }
        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2 { void Unused0(); void Unused1(); void GetSessionEnumerator(out IAudioSessionEnumerator sessions); }
        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator { void GetCount(out int count); void GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object control); }
        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            void GetState(out int state); void Unused1(); void Unused2(); void Unused3(); void Unused4(); void Unused5(); void Unused6(); void Unused7(); void Unused8(); void Unused9(); void Unused10(); void GetProcessId(out uint pid);
        }
        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISimpleAudioVolume
        {
            void SetMasterVolume(float level, ref Guid context); void GetMasterVolume(out float level);
            void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context); void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }
    }
}
