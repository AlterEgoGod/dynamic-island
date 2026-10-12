using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DynamicIsland
{
    internal sealed partial class Island
    {
        private readonly DesktopMedia desktopMedia = new DesktopMedia();
        private readonly DispatcherTimer mediaPolling = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private MediaSnapshot mediaSnapshot = new MediaSnapshot();
        private bool mediaReading, mediaCommandBusy, miniPausedIcon, musicBrowserMode = true;
        private ContextMenu musicSources;
        private DateTime mediaCommandErrorUntil;
        private string mediaCommandError = "Источник не выполнил команду. Проверьте открытую вкладку.";
        private int mediaRevision;
        private Action<string, string, double> mediaTestCommand;

        private void WireMiniPlayer()
        {
            WireFullPlayer();
            Find<Button>("MiniPrevious").Click += delegate { MediaCommand("previous"); };
            Find<Button>("MiniToggle").Click += delegate { MediaCommand("toggle"); };
            Find<Button>("MiniNext").Click += delegate { MediaCommand("next"); };
            ToolTipService.SetShowOnDisabled(Find<Button>("MiniNext"), true);
            ToolTipService.SetShowOnDisabled(Find<Button>("MiniPrevious"), true);
            Find<Button>("ChooseMusicSource").Click += delegate { ShowMediaSources(); };
            Find<Button>("MusicBrowserLogin").Click += delegate { BrowserMusicLogin(); };
            Find<Button>("MusicEmbedded").Click += delegate { musicBrowserMode = false; SelectMusic(preferences.MusicService); };
            mediaPolling.Tick += delegate { RefreshMedia(); };
            Window.Loaded += delegate { if (!smoke && WidgetEnabled("music")) { mediaPolling.Start(); RefreshMedia(); } };
        }
        private async void RefreshMedia()
        {
            if (smoke || disposed || !WidgetEnabled("music") || mediaReading || mediaCommandBusy) return;
            mediaReading = true;
            int revision = mediaRevision;
            try { MediaSnapshot snapshot = await desktopMedia.ReadAsync(); if (!disposed && WidgetEnabled("music") && revision == mediaRevision && !mediaCommandBusy) { RenderMedia(snapshot); RefreshAppVolume(); } }
            catch (Exception) { if (!disposed && WidgetEnabled("music") && revision == mediaRevision) RenderMedia(new MediaSnapshot { Error = "Не удалось получить трек из Windows. Повторяем подключение…" }); }
            finally { mediaReading = false; if (!disposed && revision != mediaRevision && !mediaCommandBusy) RefreshMedia(); }
        }
        private void RenderMedia(MediaSnapshot snapshot)
        {
            ApplySourceSelection(snapshot);
            mediaSnapshot = snapshot;
            MediaTrack track = snapshot.Current;
            Find<TextBlock>("MusicTrackTitle").Text = track == null ? "Музыка" : track.DisplayTitle;
            Find<TextBlock>("MusicSummary").Text = track == null ? "Включите музыку в браузере или приложении" : string.IsNullOrWhiteSpace(track.Artist) ? track.Source : track.Artist;
            Find<StackPanel>("MiniPlayerControls").Visibility = track == null ? Visibility.Collapsed : Visibility.Visible;
            Find<Grid>("MusicSourcesRow").Visibility = Visibility.Visible;
            Find<TextBlock>("MusicSourceLabel").Text = (selectedMusicSource == null ? "Авто" : "Источник") + " · " + (track == null ? "нет трека" : track.Source + (!track.PlaybackKnown ? "" : track.Playing ? " · играет" : " · пауза"));
            string message = DateTime.UtcNow < mediaCommandErrorUntil ? mediaCommandError : snapshot.Error;
            if (string.IsNullOrEmpty(message) && track != null && track.CompatibilityNote.Length > 0) message = track.CompatibilityNote;
            if (string.IsNullOrEmpty(message) && track != null && !track.CanNext && !track.CanPrevious) message = "Источник не предоставляет переключение треков. Выберите другой источник или откройте очередь в проигрывателе.";
            Find<TextBlock>("MiniPlayerMessage").Text = message;
            Find<TextBlock>("MiniPlayerMessage").Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
            Find<Button>("MiniPrevious").IsEnabled = track != null && track.CanPrevious && !mediaCommandBusy;
            Find<Button>("MiniNext").IsEnabled = track != null && track.CanNext && !mediaCommandBusy;
            Find<Button>("MiniPrevious").ToolTip = track != null && !track.CanPrevious ? "Источник не предоставляет переход к предыдущему треку" : "Предыдущий трек";
            Find<Button>("MiniNext").ToolTip = track != null && !track.CanNext ? "Источник не предоставляет переход к следующему треку" : "Следующий трек";
            Find<Button>("MiniToggle").IsEnabled = track != null && track.CanToggle && !mediaCommandBusy;
            Find<Button>("MiniToggle").ToolTip = track != null && track.Playing ? "Пауза" : "Продолжить воспроизведение";
            System.Windows.Automation.AutomationProperties.SetName(Find<Button>("MiniToggle"), (string)Find<Button>("MiniToggle").ToolTip);
            bool pausedIcon = track != null && track.Playing;
            if (miniPausedIcon != pausedIcon) { miniPausedIcon = pausedIcon; Find<ContentControl>("MiniPlayIcon").Content = Icons.Load(pausedIcon ? "pause" : "play"); }
            bool timed = track != null && track.Duration > 0;
            Find<ProgressBar>("MusicTrackProgress").Visibility = timed ? Visibility.Visible : Visibility.Collapsed;
            Find<ProgressBar>("MusicTrackProgress").Value = timed ? Math.Max(0, Math.Min(1, track.Position / track.Duration)) : 0;
            Find<TextBlock>("MusicTrackTime").Text = timed ? MediaTrack.Time(track.Position) + " / " + MediaTrack.Time(track.Duration) : "";
            RenderFullPlayer(track);
            UpdateSourceMenu();
        }
        private async void MediaCommand(string command, double value = 0)
        {
            MediaTrack track = mediaSnapshot.Current;
            if (smoke) { if (track != null && mediaTestCommand != null) mediaTestCommand(track.Id, command, value); return; }
            if (disposed || mediaCommandBusy || track == null) return;
            string id = track.Id; mediaCommandBusy = true; mediaRevision++; mediaCommandErrorUntil = DateTime.MinValue; RenderMedia(mediaSnapshot);
            bool success = false;
            try { success = await desktopMedia.CommandAsync(id, command, value); }
            finally { mediaCommandBusy = false; }
            if (disposed) return;
            RenderMedia(mediaSnapshot);
            if (!success)
            {
                mediaCommandError = desktopMedia.LastCommandError;
                mediaCommandErrorUntil = DateTime.UtcNow.AddSeconds(5); RenderMedia(mediaSnapshot);
            }
            else RefreshMedia();
        }
        private void ShowMediaSources()
        {
            ShowSourceMenu();
        }
        private void BrowserMusicLogin()
        {
            if (smoke) return;
            // Stop only our embedded view; never stop, log out or replace an external browser session.
            if (music != null) { music.Dispose(); music = null; }
            musicBrowserMode = true; desktopMedia.Select(null);
            selectedMusicSource = null;
            mediaCommandErrorUntil = DateTime.MinValue;
            musicWide = false; Find<Button>("MusicWide").Content = "Шире"; Snap();
            SelectMusic(preferences.MusicService);
            Launch(MusicServices.Url(preferences.MusicService));
            Find<TextBlock>("MusicStatus").Text = "Войдите в браузере и включите трек. Плеер появится в карточке «Музыка».";
            grace = DateTime.UtcNow.AddSeconds(3); RefreshMedia();
        }
        private void BrowserMusicPage()
        {
            Find<System.Windows.Controls.Primitives.UniformGrid>("MusicWebToolbar").Visibility = Visibility.Collapsed;
            Find<Button>("MusicWide").Visibility = Visibility.Collapsed;
            Find<Grid>("MusicHost").Visibility = Visibility.Collapsed;
            Find<StackPanel>("MusicPlaceholder").Visibility = Visibility.Visible;
            Find<Button>("InstallWebView").Visibility = Visibility.Collapsed;
            Find<TextBlock>("MusicMessage").Text = MusicServices.Name(preferences.MusicService) + "\n\n1. Войдите через браузер.\n2. Включите трек и оставьте вкладку открытой.\n3. Управляйте музыкой в карточке острова.";
            Find<TextBlock>("MusicNotice").Text = "Вход остаётся в браузере. Мини-плеер появится после запуска трека, если браузер передаёт его в Windows.";
            Find<TextBlock>("MusicStatus").Text = "Оставьте вкладку открытой. Поддержка кнопок зависит от браузера.";
            Find<Button>("MusicHome").IsEnabled = false; Find<Button>("MusicReload").IsEnabled = false; Find<Button>("MusicMute").IsEnabled = false;
        }
        private void CheckMiniPlayer(string directory, List<string> results)
        {
            MediaTrack track = new MediaTrack { Id = "test", AppId = "chrome.exe", Album = "Тестовый альбом", Title = "Тестовый трек · длинное название для проверки выравнивания", Artist = "Тестовый исполнитель", Source = "Браузер · тестовые данные", Playing = true, CanToggle = true, CanNext = true, CanPrevious = false, CanSeek = true, CanShuffle = true, CanRepeat = true, Shuffle = true, Repeat = Windows.Media.MediaPlaybackAutoRepeatMode.Track, Position = 67, Duration = 214 };
            track.Artwork = System.Windows.Media.Imaging.BitmapSource.Create(2, 2, 96, 96, System.Windows.Media.PixelFormats.Rgb24, null, new byte[] { 112,65,217, 190,160,255, 68,49,106, 238,231,255 }, 6); track.Artwork.Freeze();
            MediaSnapshot snapshot = new MediaSnapshot { Current = track, Tracks = new List<MediaTrack> { track } };
            RenderMedia(snapshot); ShowSettings(false); Window.UpdateLayout(); Find<Border>("MusicCard").BringIntoView(); Window.UpdateLayout();
            Assert(Find<StackPanel>("MiniPlayerControls").IsVisible && Find<TextBlock>("MusicTrackTime").Text == "1:07 / 3:34" && miniPausedIcon, "Mini-player shows track, timing and pause state inside the music card", results);
            Assert(!Find<Button>("MiniPrevious").IsEnabled && Find<Button>("MiniNext").IsEnabled, "Mini-player respects per-source supported commands", results);
            appVolume = .65; volumeAppId = track.AppId; RenderAppVolume();
            CheckMediaPointClicks(track, snapshot, results);
            Assert(Find<Slider>("MusicSeek").IsVisible && Find<Slider>("MusicSeek").Maximum == 214 && Find<Image>("MusicCover").Source == track.Artwork && (string)Find<Button>("MusicRepeat").Content == "Повтор 1", "Player renders artwork, seek range and actual repeat state", results);
            BeginMediaAdjustment(Find<Slider>("MusicSeek")); Find<Slider>("MusicSeek").Value = 100; track.Position = 70; RenderMedia(snapshot);
            Assert(Find<Slider>("MusicSeek").Value == 100 && Find<TextBlock>("MusicTrackTime").Text.StartsWith("1:40"), "Polling does not overwrite a seek in progress", results);
            track.Title = "Следующий тестовый трек"; Assert(!AdjustmentStillMatches(), "A changed track invalidates an in-progress seek", results); CancelMediaAdjustment();
            Snapshot(System.IO.Path.Combine(directory, "island-mini-player-light.png")); ChangeTheme(true); Snapshot(System.IO.Path.Combine(directory, "island-mini-player-dark.png"));
            double musicWidth = Find<Border>("MusicCard").Width;
            Find<Border>("MusicCard").Width = 270; Window.UpdateLayout();
            Point shuffleRight = Find<Button>("MusicShuffle").TranslatePoint(new Point(Find<Button>("MusicShuffle").ActualWidth, 0), Find<Border>("MusicCard"));
            Point previousLeft = Find<Button>("MiniPrevious").TranslatePoint(new Point(), Find<Border>("MusicCard"));
            Point nextRight = Find<Button>("MiniNext").TranslatePoint(new Point(Find<Button>("MiniNext").ActualWidth, 0), Find<Border>("MusicCard"));
            Point repeatLeft = Find<Button>("MusicRepeat").TranslatePoint(new Point(), Find<Border>("MusicCard"));
            Assert(shuffleRight.X <= previousLeft.X + .1 && nextRight.X <= repeatLeft.X + .1, "Player commands do not overlap in a narrow card", results);
            Find<Border>("MusicCard").Width = musicWidth; Window.UpdateLayout();
            ShowMediaSources(); Window.UpdateLayout();
            grace = DateTime.MinValue; preferences.Delay = 0; HandleHover(false);
            Assert(musicSources.IsOpen && musicSources.Items.Count == 3 && expanded, "Source chooser opens in the current theme and prevents auto-collapse", results);
            Assert((string)((MenuItem)musicSources.Items[0]).Header == "Автоматический выбор" && ((MenuItem)musicSources.Items[0]).IsChecked, "Automatic source is the default menu selection", results);
            Assert(((string)((MenuItem)musicSources.Items[2]).Header).Contains(track.Source), "Source menu lists actual playback sessions", results);
            MediaTrack untitled = new MediaTrack { Id = "untitled", AppId = "wmplayer.exe", Source = "Windows Media Player", Title = "" };
            snapshot.Tracks.Add(untitled); RenderMedia(snapshot); musicSources.UpdateLayout();
            Assert(musicSources.Items.Count == 4 && ((MenuItem)musicSources.Items[3]).Uid == untitled.Id && ((string)((MenuItem)musicSources.Items[3]).Tag).Contains(untitled.DisplayTitle), "Open source menu updates live and keeps a Windows player without track metadata", results);
            Assert(MediaSourceNames.Fallback(@"C:\Program Files\Windows Media Player\wmplayer.exe") == "Windows Media Player" && MediaSourceNames.Fallback("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic") == "Медиаплеер Windows" && MediaSourceNames.Fallback(@"C:\Players\LocalPlayer.exe") == "LocalPlayer", "Windows player and unknown player names are distinguishable without exposing paths", results);
            SnapshotSourceMenu(System.IO.Path.Combine(directory, "island-sources-dark.png")); ChangeTheme(false); SnapshotSourceMenu(System.IO.Path.Combine(directory, "island-sources-light.png"));
            snapshot.Tracks.Remove(untitled); RenderMedia(snapshot);
            Assert(Window.FindName("OpenMusic") == null, "Service and library button is removed", results);
            ((MenuItem)musicSources.Items[2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert(selectedMusicSource == track.Id && mediaSnapshot.Current == track, "Source menu selects a concrete session", results);
            MediaTrack other = new MediaTrack { Id = "other", Title = "Другой тестовый трек", Source = "Другой проигрыватель", Playing = true };
            snapshot.Tracks.Add(other); snapshot.AutomaticId = other.Id; RenderMedia(snapshot);
            Assert(snapshot.Current == track, "Manual source remains selected when Windows changes its active source", results);
            snapshot.Tracks.Remove(track); RenderMedia(snapshot);
            Assert(snapshot.Current == null, "Closed manual source does not redirect commands", results);
            ((MenuItem)musicSources.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert(selectedMusicSource == null && snapshot.Current == other && snapshot.Error == "", "Returning to automatic immediately follows the active source and clears the closed-source message", results);
            musicSources.IsOpen = false; snapshot.Tracks.Clear(); RenderMedia(snapshot); ShowMediaSources();
            Assert(((MenuItem)musicSources.Items[0]).IsChecked && !((MenuItem)musicSources.Items[2]).IsEnabled, "Empty source menu retains automatic selection with an unavailable placeholder", results);
            musicSources.IsOpen = false; snapshot.Tracks.Add(track); snapshot.AutomaticId = track.Id; snapshot.Current = track;
            track.Playing = false; track.Duration = 0; track.CanShuffle = false; track.CanRepeat = false; RenderMedia(snapshot);
            Assert(!Find<Button>("MusicShuffle").IsEnabled && !Find<Button>("MusicRepeat").IsEnabled && Find<Slider>("MusicSeek").Visibility == Visibility.Collapsed, "Unsupported shuffle, repeat and live seek are disabled rather than simulated", results);
            Assert(!miniPausedIcon && Find<ProgressBar>("MusicTrackProgress").Visibility == Visibility.Collapsed, "Paused and live sources do not show invented progress", results);
            RenderMedia(new MediaSnapshot());
            Assert(Find<StackPanel>("MiniPlayerControls").Visibility == Visibility.Collapsed && Find<TextBlock>("MusicTrackTitle").Text == "Музыка", "Closing the source clears stale track and controls", results);
            MediaTrack second = new MediaTrack { Id = "another", Title = "Другая вкладка", Playing = true };
            snapshot.Tracks.Add(second); snapshot.Choose(second.Id, track.Id, null);
            Assert(snapshot.Current == track, "Explicit source selection stays on its source when another tab plays", results);
            snapshot.Tracks.Remove(track); snapshot.Choose(second.Id, track.Id, null);
            Assert(snapshot.Current == null && snapshot.Error.Length > 0, "A closed selected source never silently redirects commands to another player", results);
            snapshot.Choose(second.Id, null, track.Id); Assert(snapshot.Current == second, "Automatic source follows the active Windows player", results);
            OpenMusic(); Window.UpdateLayout(); Assert(music == null && Find<Button>("MusicBrowserLogin").IsVisible && Find<TextBlock>("MusicMessage").Text.Contains("вкладку"), "Browser login is the primary flow without starting WebView2", results);
            Snapshot(System.IO.Path.Combine(directory, "island-browser-login.png"));
            CloseMusic(); ResetSettings();
        }
        private void SnapshotSourceMenu(string path)
        {
            musicSources.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(musicSources.ActualWidth), (int)Math.Ceiling(musicSources.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(musicSources);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var file = System.IO.File.Create(path)) encoder.Save(file);
        }
    }
}
