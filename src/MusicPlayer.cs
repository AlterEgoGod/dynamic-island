using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DynamicIsland
{
    internal static class MusicServices
    {
        public static string Normalize(string service) { return service == "spotify" || service == "youtube" ? service : "yandex"; }
        public static string Name(string service) { return service == "spotify" ? "Spotify" : service == "youtube" ? "YouTube Music" : "Яндекс Музыка"; }
        public static string Url(string service) { return service == "spotify" ? "https://open.spotify.com/" : service == "youtube" ? "https://music.youtube.com/" : "https://music.yandex.ru/"; }
        public static string Notice(string service)
        {
            if (service == "youtube") return "Google может запретить вход во встроенном плеере. В этом случае используйте «В браузере»; вход из браузера сюда не переносится.";
            if (service == "spotify") return "Войдите на странице Spotify. Если сервис не поддерживает защищённое воспроизведение здесь, используйте «В браузере».";
            return "Войдите через Яндекс ID на странице сервиса. Для прослушивания может потребоваться Яндекс Плюс.";
        }
        public static bool AllowedNavigation(string value)
        {
            Uri uri;
            return value == "about:blank" || (Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo));
        }
    }

    // The service's own web player owns login, library and playback. No credentials,
    // cookies, tokens or page content are copied into widget settings or native code.
    internal sealed class MusicPlayer : IDisposable
    {
        private readonly Window owner;
        private readonly Grid host;
        private readonly bool testing;
        private WebView2CompositionControl view;
        private CoreWebView2Environment environment;
        private Task initializing;
        private readonly List<Window> popups = new List<Window>();
        private string service, requestedService, status = "";
        private bool disposed, dark, failed, placeholder = true, missing;
        private int navigationVersion;
        public event Action<string, bool, bool> StatusChanged;
        public bool Ready { get { return !disposed && view != null && view.CoreWebView2 != null && !failed; } }
        public bool IsMuted { get { return Ready && view.CoreWebView2.IsMuted; } }
        public bool HoldsOpen { get { return popups.Count > 0 || (host.IsVisible && host.IsKeyboardFocusWithin); } }

        public MusicPlayer(Window owner, Grid host, bool testing) { this.owner = owner; this.host = host; this.testing = testing; }

        public async void Select(string selected)
        {
            if (disposed) return;
            requestedService = MusicServices.Normalize(selected);
            if (testing) { service = requestedService; Report("Тестовый режим · " + MusicServices.Name(service), true, false); return; }
            if (service == requestedService && Ready) { Report(status, placeholder, missing); return; }
            int version = ++navigationVersion;
            Report("Загружаем " + MusicServices.Name(requestedService) + "…", true, false);
            try
            {
                if (failed) ReleaseView();
                await EnsureAsync();
                if (disposed || version != navigationVersion) return;
                ClosePopups(); service = requestedService;
                view.CoreWebView2.Navigate(MusicServices.Url(service));
            }
            catch (Exception error) { if (!disposed && version == navigationVersion) ReportError(error); }
        }
        private Task EnsureAsync()
        {
            if (initializing == null) initializing = InitializeAsync();
            return initializing;
        }
        private async Task InitializeAsync()
        {
            string folder = testing ? Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts", "webview-test-profile")) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "MusicProfile");
            environment = await CoreWebView2Environment.CreateAsync(null, folder);
            if (disposed) return;
            view = new WebView2CompositionControl();
            host.Children.Add(view);
            await view.EnsureCoreWebView2Async(environment);
            if (disposed) return;
            Configure(view.CoreWebView2);
            view.CoreWebView2.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs args)
            {
                if (disposed) return;
                if (!MusicServices.AllowedNavigation(args.Uri) && !(testing && args.Uri.StartsWith("data:text/html;charset=utf-8;base64,", StringComparison.Ordinal))) { args.Cancel = true; Report("Эта ссылка открывает внешнее приложение. Используйте «В браузере».", false, false); return; }
                Report("Загружаем страницу…", false, false);
            };
            view.CoreWebView2.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs args)
            {
                if (disposed) return;
                if (!args.IsSuccess)
                {
                    if (args.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return;
                    Report("Страница недоступна. Проверьте соединение и нажмите «Обновить».", true, false); return;
                }
                Report(PageStatus(), false, false);
            };
            view.CoreWebView2.DocumentTitleChanged += delegate { if (!disposed && !placeholder) Report(PageStatus(), false, false); };
            view.CoreWebView2.IsMutedChanged += delegate { if (!disposed) Report(status, placeholder, missing); };
            view.CoreWebView2.NewWindowRequested += PopupRequested;
            view.CoreWebView2.ProcessFailed += delegate(object sender, CoreWebView2ProcessFailedEventArgs args)
            {
                if (disposed || args.ProcessFailedKind == CoreWebView2ProcessFailedKind.FrameRenderProcessExited) return;
                failed = true; Report("Плеер остановился. Нажмите «Обновить», чтобы восстановить его.", true, false);
            };
            SetTheme(dark);
        }
        private string PageStatus()
        {
            if (!Ready) return "";
            Uri uri;
            string domain = Uri.TryCreate(view.CoreWebView2.Source, UriKind.Absolute, out uri) ? uri.Host : "";
            // Show only the origin and title, never OAuth query strings.
            return domain + (string.IsNullOrWhiteSpace(view.CoreWebView2.DocumentTitle) ? "" : " · " + view.CoreWebView2.DocumentTitle);
        }
        private void Configure(CoreWebView2 core)
        {
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.DownloadStarting += delegate(object sender, CoreWebView2DownloadStartingEventArgs args) { args.Cancel = true; Report("Для загрузки файлов откройте сервис в браузере.", false, false); };
            core.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs args)
            {
                if (args.PermissionKind == CoreWebView2PermissionKind.Camera || args.PermissionKind == CoreWebView2PermissionKind.Microphone || args.PermissionKind == CoreWebView2PermissionKind.Geolocation || args.PermissionKind == CoreWebView2PermissionKind.Notifications) args.State = CoreWebView2PermissionState.Deny;
            };
        }
        private async void PopupRequested(object sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;
            if (disposed || !MusicServices.AllowedNavigation(args.Uri)) return;
            CoreWebView2Deferral deferral = args.GetDeferral();
            Window popup = null;
            try
            {
                WebView2CompositionControl child = new WebView2CompositionControl();
                popup = new Window { Owner = owner, Title = "Вход · " + MusicServices.Name(service), Width = 500, Height = 660, MinWidth = 320, MinHeight = 300, Content = child, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
                popup.Width = Math.Min(popup.Width, SystemParameters.WorkArea.Width); popup.Height = Math.Min(popup.Height, SystemParameters.WorkArea.Height);
                Window current = popup;
                bool closed = false;
                popup.Closed += delegate { closed = true; popups.Remove(current); child.Dispose(); };
                popups.Add(popup); popup.Show();
                await child.EnsureCoreWebView2Async(environment);
                if (disposed || closed) return;
                Configure(child.CoreWebView2);
                child.CoreWebView2.NavigationStarting += delegate(object s, CoreWebView2NavigationStartingEventArgs e) { if (!MusicServices.AllowedNavigation(e.Uri)) e.Cancel = true; };
                child.CoreWebView2.SourceChanged += delegate
                {
                    Uri uri;
                    if (!closed && Uri.TryCreate(child.CoreWebView2.Source, UriKind.Absolute, out uri)) current.Title = uri.Host + " · вход в " + MusicServices.Name(service);
                };
                child.CoreWebView2.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
                child.CoreWebView2.WindowCloseRequested += delegate { current.Close(); };
                args.NewWindow = child.CoreWebView2;
            }
            catch (Exception) { if (popup != null && popups.Contains(popup)) popup.Close(); if (!disposed) Report("Окно входа недоступно. Попробуйте вход на самой странице или откройте сервис в браузере.", false, false); }
            finally { deferral.Complete(); }
        }
        public void SetTheme(bool value)
        {
            dark = value;
            if (Ready) view.CoreWebView2.Profile.PreferredColorScheme = dark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
        }
        public void Home() { if (Ready && service != null) view.CoreWebView2.Navigate(MusicServices.Url(service)); else if (requestedService != null) Select(requestedService); }
        public void Reload() { if (Ready) view.CoreWebView2.Reload(); else if (requestedService != null) Select(requestedService); }
        public void ToggleMute() { if (Ready) view.CoreWebView2.IsMuted = !view.CoreWebView2.IsMuted; }
        private void Report(string value, bool showPlaceholder, bool runtimeMissing)
        {
            status = value; placeholder = showPlaceholder; missing = runtimeMissing;
            if (view != null) view.Visibility = showPlaceholder ? Visibility.Hidden : Visibility.Visible;
            if (StatusChanged != null) StatusChanged(value, showPlaceholder, runtimeMissing);
        }
        private void ReportError(Exception error)
        {
            bool runtime = error is WebView2RuntimeNotFoundException;
            ReleaseView();
            Report(runtime ? "Для встроенного плеера нужен Microsoft Edge WebView2 Runtime." : "Не удалось запустить плеер. Нажмите «Обновить» или откройте сервис в браузере.", true, runtime);
        }
        private void ClosePopups() { foreach (Window popup in popups.ToArray()) popup.Close(); }
        private void ReleaseView()
        {
            ClosePopups();
            if (view != null) { host.Children.Remove(view); view.Dispose(); view = null; }
            initializing = null; service = null; failed = false;
        }
        public void Dispose() { if (disposed) return; disposed = true; navigationVersion++; ReleaseView(); }

        // Exercises the same composition control in a transparent WPF window without
        // contacting services, logging in, playing sound or touching the user's profile.
        public async Task VerifyOfflineAsync()
        {
            await EnsureAsync();
            TaskCompletionSource<bool> loaded = new TaskCompletionSource<bool>();
            string details = "";
            view.CoreWebView2.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs args) { details += "Start: " + args.Uri + ", cancelled=" + args.Cancel + "; "; };
            view.CoreWebView2.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs args) { details += args.WebErrorStatus; loaded.TrySetResult(args.IsSuccess); };
            view.CoreWebView2.NavigateToString("<!doctype html><html><head><title>Music test</title></head><body style='background:#292735;color:#F5F2FF;font:18px Segoe UI'><h2>Встроенный плеер</h2><p>Локальная проверка WebView2</p><audio id='audio' controls></audio><button id='button' onclick=\"document.title='Clicked'\">Проверить управление</button></body></html>");
            if (await Task.WhenAny(loaded.Task, Task.Delay(15000)) != loaded.Task || !await loaded.Task) throw new InvalidOperationException("Offline WebView2 navigation failed: " + details);
            string result = await view.CoreWebView2.ExecuteScriptAsync("document.getElementById('button').click(); document.title");
            if (result != "\"Clicked\"") throw new InvalidOperationException("Embedded page interaction failed.");
            ToggleMute(); if (!IsMuted) throw new InvalidOperationException("Mute failed."); ToggleMute();
            host.Visibility = Visibility.Collapsed;
            if (!Ready) throw new InvalidOperationException("Hiding the view disposed its player.");
            host.Visibility = Visibility.Visible;
            SetTheme(true);
            if (view.CoreWebView2.Profile.PreferredColorScheme != CoreWebView2PreferredColorScheme.Dark) throw new InvalidOperationException("Music theme failed.");
        }
        public async Task ProbeServicesAsync(string directory)
        {
            if (!testing) throw new InvalidOperationException("Probe requires an isolated test profile.");
            await EnsureAsync();
            List<string> report = new List<string>();
            foreach (string key in new[] { "yandex", "spotify", "youtube" })
            {
                service = key;
                TaskCompletionSource<string> loaded = new TaskCompletionSource<string>();
                EventHandler<CoreWebView2NavigationCompletedEventArgs> handler = delegate(object sender, CoreWebView2NavigationCompletedEventArgs args) { loaded.TrySetResult(args.IsSuccess ? "loaded" : args.WebErrorStatus.ToString()); };
                view.CoreWebView2.NavigationCompleted += handler;
                view.CoreWebView2.Navigate(MusicServices.Url(key));
                await Task.WhenAny(loaded.Task, Task.Delay(25000));
                view.CoreWebView2.NavigationCompleted -= handler;
                await Task.Delay(3000);
                report.Add(key + ": " + (loaded.Task.IsCompleted ? loaded.Task.Result : "timeout") + " · " + view.CoreWebView2.DocumentTitle);
                using (Stream file = File.Create(Path.Combine(directory, "music-" + key + ".png"))) await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
            }
            report.Add("Anonymous service pages only. Account login and protected playback were not tested.");
            File.WriteAllLines(Path.Combine(directory, "music-probe.txt"), report);
        }
    }
}
