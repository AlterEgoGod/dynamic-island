using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland
{
    internal sealed partial class Island
    {
        private void CheckMediaPointClicks(MediaTrack track, MediaSnapshot snapshot, List<string> results)
        {
            List<string> commands = new List<string>(); double value = -1; string target = null;
            mediaTestCommand = delegate(string id, string command, double amount) { commands.Add(command); value = amount; target = id; };
            try
            {
                foreach (string name in new[] { "MusicSeek", "MusicVolume" })
                {
                    Slider slider = Find<Slider>(name);
                    // Move-to-point marks mouse-down handled in WPF before instance handlers run.
                    slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Handled = true });
                    Assert(mediaAdjusting && adjustingSlider == slider, "A handled point click starts adjustment for " + name, results);
                    slider.Value = name == "MusicSeek" ? 123 : 37;
                    RenderMedia(snapshot);
                    Assert(slider.Value == (name == "MusicSeek" ? 123 : 37), "Polling preserves clicked value for " + name, results);
                    int before = commands.Count;
                    slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent, Handled = true });
                    Assert(!mediaAdjusting && commands.Count == before + 1 && Math.Abs(value - (name == "MusicSeek" ? 123 : .37)) < .0001 && target == (name == "MusicSeek" ? track.Id : track.AppId), "A click without dragging commits once to the selected source for " + name, results);
                }
                BeginMediaAdjustment(Find<Slider>("MusicSeek"));
                int count = commands.Count; string oldTitle = track.Title; track.Title = "Changed during adjustment";
                CommitMediaAdjustment(); track.Title = oldTitle;
                Assert(commands.Count == count && !mediaAdjusting, "A click is discarded when its track changes before release", results);
                Assert(AppVolume.ProcessName("wmplayer.exe") == "wmplayer" && AppVolume.ProcessName("AIMP.exe") == "aimp" && AppVolume.ProcessName("vlc.exe") == "vlc", "Application volume supports classic WMP and other desktop players", results);
                CheckClassicCommands(results);
                MediaTrack audioOnly = new MediaTrack { Id = "audio-fixture", AppId = "fixture.exe", Source = "Тестовый аудиоплеер", PlaybackKnown = false, CompatibilityNote = "Доступна громкость приложения." };
                MediaSnapshot audioSnapshot = new MediaSnapshot { Current = audioOnly, Tracks = new List<MediaTrack> { audioOnly } };
                RenderMedia(audioSnapshot); appVolume = .5; volumeAppId = audioOnly.AppId; RenderAppVolume();
                Assert(Find<Slider>("MusicVolume").IsEnabled && !Find<Button>("MiniToggle").IsEnabled && !Find<Button>("MiniNext").IsEnabled && Find<Slider>("MusicSeek").Visibility == Visibility.Collapsed && Find<TextBlock>("MiniPlayerMessage").Text == audioOnly.CompatibilityNote, "Audio-only applications expose volume without invented transport capabilities", results);
            }
            finally { mediaTestCommand = null; RenderMedia(snapshot); appVolume = .65; volumeAppId = track.AppId; RenderAppVolume(); }
        }
        private void CheckClassicCommands(List<string> results)
        {
            WmpCommandFixture endpoint = new WmpCommandFixture();
            MediaTrack source = new MediaTrack { CanToggle = true, CanNext = true, CanPrevious = true, CanSeek = true, CanShuffle = true, CanRepeat = true, Duration = 100 };
            WmpRemote.ExecuteCommand(source, endpoint, endpoint, "next", 0);
            WmpRemote.ExecuteCommand(source, endpoint, endpoint, "previous", 0);
            WmpRemote.ExecuteCommand(source, endpoint, endpoint, "toggle", 0);
            source.Playing = true; WmpRemote.ExecuteCommand(source, endpoint, endpoint, "toggle", 0);
            Assert(string.Join(",", endpoint.Calls) == "next,previous,play,pause", "Classic WMP transport targets the supplied endpoint once per command", results);
            WmpRemote.ExecuteCommand(source, endpoint, endpoint, "seek", 500);
            Assert(endpoint.currentPosition == 100 && !WmpRemote.ExecuteCommand(source, endpoint, endpoint, "seek", double.NaN), "Classic WMP seek is bounded and rejects invalid positions", results);
            source.CanNext = false; int count = endpoint.Calls.Count;
            Assert(!WmpRemote.ExecuteCommand(source, endpoint, endpoint, "next", 0) && endpoint.Calls.Count == count, "Classic WMP respects unsupported commands without global media keys", results);
            WmpRemote.ExecuteCommand(source, endpoint, endpoint, "shuffle", 0);
            source.Repeat = Windows.Media.MediaPlaybackAutoRepeatMode.None; WmpRemote.ExecuteCommand(source, endpoint, endpoint, "repeat", 0);
            Assert(endpoint.Shuffle && endpoint.Loop, "Classic WMP shuffle and repeat map to its own settings", results);
        }
    }
    public sealed class WmpCommandFixture
    {
        public readonly List<string> Calls = new List<string>();
        public double currentPosition { get; set; }
        public bool Shuffle, Loop;
        public void next() { Calls.Add("next"); } public void previous() { Calls.Add("previous"); }
        public void play() { Calls.Add("play"); } public void pause() { Calls.Add("pause"); }
        public void setMode(string mode, bool value) { if (mode == "shuffle") Shuffle = value; if (mode == "loop") Loop = value; }
    }
}
