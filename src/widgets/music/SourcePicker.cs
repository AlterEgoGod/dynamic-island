using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace DynamicIsland
{
    internal sealed partial class Island
    {
        private string sourceMenuSignature;
        private void ShowSourceMenu()
        {
            if (musicSources != null && musicSources.IsOpen) { musicSources.IsOpen = false; return; }
            musicSources = new ContextMenu { Resources = Window.Resources, PlacementTarget = Find<Button>("ChooseMusicSource"), Placement = PlacementMode.Bottom, VerticalOffset = 7,
                Width = Math.Min(360, Math.Max(220, Window.ActualWidth - 48)), Style = (Style)Window.Resources["SourceMenu"] };
            musicSources.Closed += delegate { grace = DateTime.UtcNow.AddMilliseconds(500); };
            sourceMenuSignature = null; UpdateSourceMenu(true); musicSources.IsOpen = true;
            RefreshMedia();
        }
        private MenuItem SourceItem(string id, string name, string description, bool selected, string icon)
        {
            ContentControl face = new ContentControl { Content = Icons.Load(icon), Width = 18, Height = 18 };
            face.SetResourceReference(Control.ForegroundProperty, "Accent");
            MenuItem item = new MenuItem { Uid = id, Header = name, Tag = description, ToolTip = name + "\n" + description, Icon = face, IsCheckable = true, IsChecked = selected, Style = (Style)Window.Resources["SourceMenuItem"] };
            System.Windows.Automation.AutomationProperties.SetName(item, name + ", " + description);
            return item;
        }
        private void UpdateSourceMenu(bool force = false)
        {
            if (musicSources == null || (!force && !musicSources.IsOpen)) return;
            List<string> parts = new List<string> { selectedMusicSource ?? "automatic" };
            foreach (MediaTrack track in mediaSnapshot.Tracks) parts.Add(track.Id + "\n" + track.Source + "\n" + track.Title + "\n" + track.Playing + "\n" + track.PlaybackKnown);
            string signature = string.Join("\r", parts);
            if (!force && signature == sourceMenuSignature) return;
            sourceMenuSignature = signature;
            string focused = null;
            foreach (object value in musicSources.Items) { MenuItem item = value as MenuItem; if (item != null && item.IsKeyboardFocusWithin) focused = item.Uid; }
            musicSources.Items.Clear();
            MenuItem automatic = SourceItem("automatic", "Автоматический выбор", "Следовать активному проигрывателю", selectedMusicSource == null, "brand");
            automatic.Click += delegate { SelectMediaSource(null); }; musicSources.Items.Add(automatic);
            musicSources.Items.Add(new Separator { Style = (Style)Window.Resources["SourceMenuSeparator"] });
            foreach (MediaTrack track in mediaSnapshot.Tracks)
            {
                string id = track.Id;
                MenuItem item = SourceItem(id, track.Source, track.PlaybackKnown ? (track.Playing ? "Играет" : "На паузе") + " · " + track.DisplayTitle : "Аудиоприложение · доступна громкость", selectedMusicSource == id, "play");
                item.Click += delegate { SelectMediaSource(id); }; musicSources.Items.Add(item);
            }
            if (mediaSnapshot.Tracks.Count == 0)
            {
                MenuItem empty = SourceItem("empty", "Нет активных источников", "Откройте музыку в приложении", false, "play"); empty.IsEnabled = false; musicSources.Items.Add(empty);
            }
            if (focused != null)
                foreach (object value in musicSources.Items) { MenuItem item = value as MenuItem; if (item != null && item.Uid == focused) { item.Focus(); break; } }
        }
    }
}
