using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Xml.Linq;

namespace DynamicIsland
{
    internal sealed partial class Island
    {
        private bool featureDialogOpen, musicOpen, musicWide;
        private MusicPlayer music;

        private void WireFeatures()
        {
            WireMiniPlayer();
            Find<Button>("TimerRestart").Click += delegate { CancelFocusEdit(); timerCue.Stop(); focus.Reset(); UpdateClock(); };
            Find<Button>("TimerAddMinute").Click += delegate
            {
                if (editingFocus && !CommitFocusEdit()) return;
                if (focus.AddMinute(DateTime.UtcNow)) { timerCue.Stop(); preferences.FocusSeconds = (long)focus.Duration.TotalSeconds; Save(); UpdateClock(); }
            };
            Find<TextBox>("TimerLabel").ToolTip = "Минуты, мин:сек или ч:мин:сек · Enter — применить · Esc — отменить";
            Find<Button>("AddQuickApp").Click += delegate { ChooseQuickApps(); };
            Find<Button>("AddQuickLink").Click += delegate { AddQuickLink(); };
            Find<TextBox>("QuickLinkAddress").KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs args)
            {
                if (args.Key == System.Windows.Input.Key.Enter) { AddQuickLink(); args.Handled = true; }
            };
            Find<Button>("MusicBack").Click += delegate { CloseMusic(); };
            Find<Button>("MusicWide").Click += delegate { musicWide = !musicWide; Find<Button>("MusicWide").Content = musicWide ? "Компактно" : "Шире"; grace = DateTime.UtcNow.AddSeconds(2); Snap(); };
            Find<Button>("MusicYandex").Click += delegate { SelectMusic("yandex"); };
            Find<Button>("MusicSpotify").Click += delegate { SelectMusic("spotify"); };
            Find<Button>("MusicYoutube").Click += delegate { SelectMusic("youtube"); };
            Find<Button>("MusicHome").Click += delegate { if (music != null) music.Home(); };
            Find<Button>("MusicReload").Click += delegate { if (music != null) music.Reload(); };
            Find<Button>("MusicMute").Click += delegate { if (music != null) music.ToggleMute(); };
            Find<Button>("MusicExternal").Click += delegate { BrowserMusicLogin(); };
            Find<Button>("InstallWebView").Click += delegate { if (!smoke) Launch("https://developer.microsoft.com/microsoft-edge/webview2/#download-section"); };
        }
        private void OpenMusic()
        {
            ShowSettings(false); musicOpen = true;
            Find<ScrollViewer>("OverviewScroll").Visibility = Visibility.Collapsed;
            Find<Grid>("MusicPage").Visibility = Visibility.Visible;
            Snap();
            SelectMusic(preferences.MusicService);
        }
        private void CloseMusic()
        {
            musicOpen = false; Find<Grid>("MusicPage").Visibility = Visibility.Collapsed;
            Find<ScrollViewer>("OverviewScroll").Visibility = Visibility.Visible;
            Snap();
            // Hiding the view preserves the web player's session and audio.
            Find<Button>("ChooseMusicSource").Focus();
        }
        private void SelectMusic(string service)
        {
            preferences.MusicService = MusicServices.Normalize(service); Save();
            foreach (string key in new[] { "yandex", "spotify", "youtube" })
                Find<Button>(key == "yandex" ? "MusicYandex" : key == "spotify" ? "MusicSpotify" : "MusicYoutube").SetResourceReference(Control.BackgroundProperty, preferences.MusicService == key ? "AccentSoft" : "Card");
            Find<TextBlock>("MusicNotice").Text = MusicServices.Notice(preferences.MusicService);
            if (musicBrowserMode) { BrowserMusicPage(); return; }
            Find<UniformGrid>("MusicWebToolbar").Visibility = Visibility.Visible;
            Find<Button>("MusicWide").Visibility = Visibility.Visible;
            Find<Grid>("MusicHost").Visibility = Visibility.Visible;
            Find<Button>("MusicHome").IsEnabled = true; Find<Button>("MusicReload").IsEnabled = true;
            if (music == null)
            {
                music = new MusicPlayer(Window, Find<Grid>("MusicHost"), smoke);
                music.StatusChanged += delegate(string status, bool placeholder, bool runtimeMissing)
                {
                    if (disposed || musicBrowserMode || music == null) return;
                    Find<TextBlock>("MusicStatus").Text = status;
                    Find<TextBlock>("MusicMessage").Text = status;
                    Find<StackPanel>("MusicPlaceholder").Visibility = placeholder ? Visibility.Visible : Visibility.Collapsed;
                    Find<Button>("InstallWebView").Visibility = runtimeMissing ? Visibility.Visible : Visibility.Collapsed;
                    Find<Button>("MusicMute").Content = music.IsMuted ? "Включить звук" : "Без звука";
                    Find<Button>("MusicMute").IsEnabled = music.Ready;
                };
            }
            music.SetTheme(preferences.Dark); music.Select(preferences.MusicService);
        }
        private void ResetMusic()
        {
            if (music != null) { music.Dispose(); music = null; }
            musicBrowserMode = true; desktopMedia.Select(null);
            selectedMusicSource = null; mediaAdjusting = false;
            mediaCommandErrorUntil = DateTime.MinValue;
            musicWide = false; Find<Button>("MusicWide").Content = "Шире";
            if (musicOpen) CloseMusic();
            RenderMedia(new MediaSnapshot());
        }
        private void CheckFeatures(string directory, List<string> results)
        {
            ShowSettings(false); Find<ScrollViewer>("OverviewScroll").ScrollToTop();
            SetFocusSeconds(180); focus.Toggle(DateTime.UtcNow.AddSeconds(-60)); UpdateClock();
            Assert(focus.Running && Find<ProgressBar>("TimerProgress").Value >= 1.0 / 3 && Find<TextBlock>("TimerHint").Text.StartsWith("До "), "Focus shows countdown progress and completion time", results);
            Find<Button>("TimerAddMinute").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(focus.Running && preferences.FocusSeconds == 240 && focus.Remaining(DateTime.UtcNow).TotalSeconds > 178, "Add minute extends the current countdown without stopping", results);
            Find<Button>("TimerButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(!focus.Running && ((string)Find<Button>("TimerButton").ToolTip).StartsWith("Продолжить"), "Paused timer exposes resume action", results);
            Find<Button>("TimerRestart").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(!focus.Running && focus.Remaining(DateTime.UtcNow).TotalSeconds == 240 && Find<ProgressBar>("TimerProgress").Value == 0, "Restart button restores full interval and progress", results);
            SetFocusSeconds(1); focus.Toggle(DateTime.UtcNow.AddSeconds(-2)); UpdateClock();
            Assert(!focus.Running && Find<ProgressBar>("TimerProgress").Value == 1 && ((string)Find<Button>("TimerButton").ToolTip).StartsWith("Повторить"), "Completed timer offers repeat and full progress", results);
            Find<Button>("TimerButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(focus.Running, "Completed timer can repeat", results);
            SetFocusSeconds(300); focus.Toggle(DateTime.UtcNow.AddSeconds(-70)); UpdateClock();
            Snapshot(Path.Combine(directory, "island-timer.png"));
            int count = preferences.QuickApps.Count;
            Assert(AddQuickApp(@"C:\Apps\Example Player.exe") && !AddQuickApp(@"c:\apps\EXAMPLE PLAYER.exe"), "Quick access rejects duplicate program paths ignoring case", results);
            BuildQuickApps(); ShowSettings(true); Window.UpdateLayout();
            Assert(Find<UniformGrid>("QuickAppsGrid").Children.Count == count + 1 && Find<StackPanel>("QuickAppEditor").Children.Count == count + 1, "Adding a program updates main access and editor", results);
            Grid row = (Grid)Find<StackPanel>("QuickAppEditor").Children[count];
            TextBox name = (TextBox)row.Children[0]; name.Text = "Мой плеер";
            name.RaiseEvent(new System.Windows.Input.KeyboardFocusChangedEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, Environment.TickCount, name, null) { RoutedEvent = System.Windows.Input.Keyboard.LostKeyboardFocusEvent });
            Assert(preferences.QuickApps[count].Name == "Мой плеер", "Renaming an app updates its preference", results);
            name.BringIntoView(); Window.UpdateLayout(); Snapshot(Path.Combine(directory, "island-quick-apps.png"));
            ((Button)row.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(preferences.QuickApps.Count == count && Find<UniformGrid>("QuickAppsGrid").Children.Count == count, "Removing an app updates both lists", results);
            CheckQuickLinks(directory, results);
            preferences.QuickApps.Clear(); BuildQuickApps();
            Assert(Find<TextBlock>("QuickAppsEmpty").Visibility == Visibility.Visible, "Empty quick access explains how to add programs", results);
            musicBrowserMode = false; OpenMusic(); Window.UpdateLayout();
            Assert(musicOpen && Find<Grid>("MusicPage").IsVisible && !Find<ScrollViewer>("OverviewScroll").IsVisible, "Music opens inside the island", results);
            SelectMusic("spotify"); Assert(preferences.MusicService == "spotify", "Spotify service can be selected", results);
            SelectMusic("youtube"); Assert(Find<TextBlock>("MusicNotice").Text.Contains("Google"), "Google embedded sign-in limitation is explained", results);
            SelectMusic("yandex"); Snapshot(Path.Combine(directory, "island-music.png"));
            Find<Button>("MusicWide").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(CurrentBounds() == LayoutBounds(true) && content.Width > 430, "Music can expand within screen bounds for desktop service pages", results);
            CheckWideMusic(results);
            CloseMusic(); Assert(!musicOpen && Find<ScrollViewer>("OverviewScroll").IsVisible && music != null, "Returning to widget keeps player instance alive", results);
            ResetSettings(); Assert(preferences.QuickApps.Count == 4 && preferences.MusicService == "yandex" && music == null, "Reset restores default programs and disposes music playback", results);
            CheckMiniPlayer(directory, results);
        }
        private void CheckWideMusic(List<string> results)
        {
            System.Drawing.Rectangle wide = CurrentBounds();
            SetExpanded(false, true); Assert(CurrentBounds() == wide && VisibleBounds().Width <= 132 * Scale(), "Wide music collapses to the same narrow strip without moving its canvas", results);
            SetExpanded(true, true); Assert(CurrentBounds() == wide, "Wide music reopens without native window movement", results);
        }
    }
}
