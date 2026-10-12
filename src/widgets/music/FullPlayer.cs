using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Windows.Media;

namespace DynamicIsland
{
    internal sealed partial class Island
    {
        private string selectedMusicSource;
        private readonly Dictionary<string, double> audibleVolumes = new Dictionary<string, double>();
        private bool mediaAdjusting, volumeReading, volumeWriting;
        private string adjustingId, adjustingTitle, volumeAppId;
        private Slider adjustingSlider;
        private double? appVolume;
        private double audibleVolume = .5;
        private int volumeRevision;

        private void WireFullPlayer()
        {
            Find<Button>("MusicShuffle").Click += delegate { MediaCommand("shuffle"); };
            Find<Button>("MusicRepeat").Click += delegate { MediaCommand("repeat"); };
            ToolTipService.SetShowOnDisabled(Find<Button>("MusicShuffle"), true);
            ToolTipService.SetShowOnDisabled(Find<Button>("MusicRepeat"), true);
            Find<Button>("MusicMuteApp").Click += delegate { if (appVolume.HasValue) ChangeAppVolume(appVolume.Value > 0 ? 0 : audibleVolume); };
            foreach (string name in new[] { "MusicSeek", "MusicVolume" })
            {
                Slider slider = Find<Slider>(name);
                // Slider handles point-clicks in its class handler before our instance handler.
                // Listen to handled events too, so releasing a simple click commits the new value.
                slider.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(delegate { BeginMediaAdjustment(slider); }), true);
                slider.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(delegate { CommitMediaAdjustment(); }), true);
                slider.LostMouseCapture += delegate { if (mediaAdjusting && !slider.IsMouseCaptureWithin) CommitMediaAdjustment(); };
                slider.PreviewKeyDown += delegate(object sender, KeyEventArgs args)
                {
                    if (args.Key == Key.Left || args.Key == Key.Right || args.Key == Key.Home || args.Key == Key.End || args.Key == Key.Up || args.Key == Key.Down || args.Key == Key.PageUp || args.Key == Key.PageDown) BeginMediaAdjustment(slider);
                };
                slider.KeyUp += delegate { CommitMediaAdjustment(); };
                slider.LostKeyboardFocus += delegate { if (!slider.IsMouseCaptureWithin) CommitMediaAdjustment(); };
                slider.ValueChanged += delegate
                {
                    if (!mediaAdjusting || adjustingSlider != slider) return;
                    if (slider.Name == "MusicSeek" && mediaSnapshot.Current != null) Find<TextBlock>("MusicTrackTime").Text = MediaTrack.Time(slider.Value) + " / " + MediaTrack.Time(mediaSnapshot.Current.Duration);
                    else Find<TextBlock>("MusicVolumeValue").Text = Math.Round(slider.Value) + "%";
                };
            }
        }
        private void BeginMediaAdjustment(Slider slider)
        {
            if (mediaAdjusting || !slider.IsEnabled || mediaSnapshot.Current == null) return;
            mediaAdjusting = true; adjustingSlider = slider; adjustingId = mediaSnapshot.Current.Id; adjustingTitle = mediaSnapshot.Current.Title;
        }
        private bool AdjustmentStillMatches()
        {
            return mediaSnapshot.Current != null && mediaSnapshot.Current.Id == adjustingId && mediaSnapshot.Current.Title == adjustingTitle;
        }
        private void CommitMediaAdjustment()
        {
            if (!mediaAdjusting) return;
            Slider slider = adjustingSlider; bool matches = AdjustmentStillMatches(); mediaAdjusting = false; adjustingSlider = null;
            if (!matches) { RenderMedia(mediaSnapshot); return; }
            if (slider.Name == "MusicSeek") MediaCommand("seek", slider.Value); else ChangeAppVolume(slider.Value / 100);
        }
        private void CancelMediaAdjustment() { mediaAdjusting = false; adjustingSlider = null; RenderMedia(mediaSnapshot); }
        private void RenderFullPlayer(MediaTrack track)
        {
            Find<Image>("MusicCover").Source = track == null ? null : track.Artwork;
            Find<ContentControl>("MusicCoverPlaceholder").Visibility = track == null || track.Artwork == null ? Visibility.Visible : Visibility.Collapsed;
            Find<TextBlock>("MusicAlbum").Text = track == null ? "" : track.Album;
            bool timed = track != null && track.Duration > 0;
            Slider seek = Find<Slider>("MusicSeek");
            seek.Visibility = timed && track.CanSeek ? Visibility.Visible : Visibility.Collapsed;
            seek.IsEnabled = timed && track.CanSeek && !mediaCommandBusy;
            Find<ProgressBar>("MusicTrackProgress").Visibility = timed && !track.CanSeek ? Visibility.Visible : Visibility.Collapsed;
            if (!mediaAdjusting)
            {
                seek.Maximum = timed ? track.Duration : 1;
                seek.Value = timed ? Math.Min(track.Duration, Math.Max(0, track.Position)) : 0;
            }
            else if (adjustingSlider == seek && AdjustmentStillMatches())
                Find<TextBlock>("MusicTrackTime").Text = MediaTrack.Time(seek.Value) + " / " + MediaTrack.Time(track.Duration);
            bool shuffle = track != null && track.CanShuffle && track.Shuffle.HasValue;
            bool repeat = track != null && track.CanRepeat && track.Repeat.HasValue;
            Find<Button>("MusicShuffle").IsEnabled = shuffle && !mediaCommandBusy;
            Find<Button>("MusicShuffle").SetResourceReference(Control.BackgroundProperty, track != null && track.Shuffle == true ? "AccentSoft" : "Card");
            Find<Button>("MusicShuffle").ToolTip = shuffle ? track.Shuffle.Value ? "Выключить случайный порядок" : "Включить случайный порядок" : "Этот источник не предоставляет управление случайным порядком";
            Find<Button>("MusicRepeat").IsEnabled = repeat && !mediaCommandBusy;
            Find<Button>("MusicRepeat").Content = track != null && track.Repeat == MediaPlaybackAutoRepeatMode.Track ? "Повтор 1" : "Повтор";
            Find<Button>("MusicRepeat").SetResourceReference(Control.BackgroundProperty, repeat && track.Repeat != MediaPlaybackAutoRepeatMode.None ? "AccentSoft" : "Card");
            Find<Button>("MusicRepeat").ToolTip = !repeat ? "Этот источник не предоставляет управление повтором" : track.Repeat == MediaPlaybackAutoRepeatMode.None ? "Включить повтор списка" : track.Repeat == MediaPlaybackAutoRepeatMode.List ? "Повторять один трек" : "Выключить повтор";
            if (track == null || volumeAppId != track.AppId)
            {
                appVolume = null; volumeAppId = track == null ? null : track.AppId;
                if (volumeAppId == null || !audibleVolumes.TryGetValue(volumeAppId, out audibleVolume)) audibleVolume = .5;
                RenderAppVolume();
            }
        }
        private async void RefreshAppVolume()
        {
            if (smoke || disposed || !WidgetEnabled("music") || volumeReading || volumeWriting || mediaAdjusting || mediaSnapshot.Current == null) return;
            string appId = mediaSnapshot.Current.AppId; volumeReading = true;
            int revision = volumeRevision;
            double? level = null;
            try { level = await Task.Run(delegate { return AppVolume.Read(appId); }); }
            finally { volumeReading = false; }
            if (disposed || revision != volumeRevision || volumeWriting || mediaAdjusting || mediaSnapshot.Current == null || mediaSnapshot.Current.AppId != appId) return;
            appVolume = level; volumeAppId = appId; if (level.HasValue && level.Value > 0) audibleVolumes[appId] = audibleVolume = level.Value; RenderAppVolume();
        }
        private void RenderAppVolume()
        {
            Find<Slider>("MusicVolume").IsEnabled = appVolume.HasValue && !volumeWriting;
            Find<Button>("MusicMuteApp").IsEnabled = appVolume.HasValue && !volumeWriting;
            if (!mediaAdjusting) Find<Slider>("MusicVolume").Value = appVolume.HasValue ? appVolume.Value * 100 : 0;
            Find<TextBlock>("MusicVolumeValue").Text = appVolume.HasValue ? Math.Round(appVolume.Value * 100) + "%" : "—";
            Find<Button>("MusicMuteApp").Content = appVolume == 0 ? "Тихо" : "Звук";
            Find<Button>("MusicMuteApp").ToolTip = appVolume == 0 ? "Включить звук приложения" : "Отключить звук приложения";
            Find<TextBlock>("MusicVolumeHint").Text = appVolume.HasValue ? "Громкость приложения · в браузере — всех вкладок" : "Громкость этого приложения пока недоступна";
        }
        private async void ChangeAppVolume(double level)
        {
            if (smoke) { if (mediaTestCommand != null) mediaTestCommand(volumeAppId, "volume", level); return; }
            if (disposed || volumeWriting || !appVolume.HasValue || mediaSnapshot.Current == null || volumeAppId != mediaSnapshot.Current.AppId) return;
            string appId = volumeAppId;
            volumeWriting = true; volumeRevision++;
            Find<Slider>("MusicVolume").IsEnabled = false; Find<Button>("MusicMuteApp").IsEnabled = false;
            bool success;
            try { success = await Task.Run(delegate { return AppVolume.Set(appId, level); }); }
            finally { volumeWriting = false; }
            if (disposed || mediaSnapshot.Current == null || mediaSnapshot.Current.AppId != appId) return;
            if (success) { appVolume = level; if (level > 0) audibleVolumes[appId] = audibleVolume = level; RenderAppVolume(); }
            else { RenderAppVolume(); mediaCommandError = "Не удалось изменить громкость приложения."; mediaCommandErrorUntil = DateTime.UtcNow.AddSeconds(5); RenderMedia(mediaSnapshot); }
        }
        private void ApplySourceSelection(MediaSnapshot snapshot)
        {
            if (snapshot.Error == "Выбранный источник закрыт. Выберите другой.") snapshot.Error = "";
            snapshot.Choose(snapshot.AutomaticId, selectedMusicSource, snapshot.Current == null ? null : snapshot.Current.Id);
        }
        private void SelectMediaSource(string id)
        {
            if (mediaAdjusting) CancelMediaAdjustment();
            selectedMusicSource = id;
            mediaCommandErrorUntil = DateTime.MinValue;
            RenderMedia(mediaSnapshot); RefreshAppVolume();
        }
    }
}
