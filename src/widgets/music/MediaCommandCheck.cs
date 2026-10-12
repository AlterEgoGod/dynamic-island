using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Playback;

namespace DynamicIsland
{
    // Own silent metadata-only source. Never sends commands to any user session.
    internal static class MediaCommandCheck
    {
        public static int Run()
        {
            string report = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts", "media-command-test.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(report));
            try { Task.Run((Func<Task>)Check).GetAwaiter().GetResult(); File.WriteAllText(report, "PASS: next and previous reached only the isolated test media session, once each. Session without a track title remains selectable. No user playback commands or audio."); return 0; }
            catch (Exception error) { File.WriteAllText(report, "FAIL: " + error); return 1; }
        }
        private static async Task Check()
        {
            string marker = "Dynamic Island test " + Guid.NewGuid().ToString("N");
            using (MediaPlayer player = new MediaPlayer())
            using (DesktopMedia desktop = new DesktopMedia())
            {
                player.CommandManager.IsEnabled = false; player.Volume = 0;
                var controls = player.SystemMediaTransportControls;
                controls.IsEnabled = true; controls.IsNextEnabled = true; controls.IsPreviousEnabled = true;
                controls.PlaybackStatus = MediaPlaybackStatus.Paused;
                controls.DisplayUpdater.Type = MediaPlaybackType.Music; controls.DisplayUpdater.MusicProperties.Title = marker; controls.DisplayUpdater.Update();
                int next = 0, previous = 0;
                controls.ButtonPressed += delegate(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
                {
                    if (args.Button == SystemMediaTransportControlsButton.Next) Interlocked.Increment(ref next);
                    if (args.Button == SystemMediaTransportControlsButton.Previous) Interlocked.Increment(ref previous);
                };
                try
                {
                    MediaTrack own = null;
                    for (int attempt = 0; attempt < 20 && own == null; attempt++)
                    {
                        MediaSnapshot snapshot = await desktop.ReadAsync(); own = snapshot.Tracks.Find(delegate(MediaTrack track) { return track.Title == marker; });
                        if (own == null) await Task.Delay(200);
                    }
                    if (own == null) throw new InvalidOperationException("Windows did not publish the isolated test session");
                    if (!own.CanNext || !own.CanPrevious) throw new InvalidOperationException("Test capabilities missing");
                    if (!await desktop.CommandAsync(own.Id, "next")) throw new InvalidOperationException("Next failed: " + desktop.LastCommandError);
                    if (!await desktop.CommandAsync(own.Id, "previous")) throw new InvalidOperationException("Previous failed: " + desktop.LastCommandError);
                    for (int attempt = 0; attempt < 20 && (next < 1 || previous < 1); attempt++) await Task.Delay(100);
                    if (next != 1 || previous != 1) throw new InvalidOperationException("Commands were lost or duplicated: " + next + "/" + previous);
                    controls.IsNextEnabled = false;
                    await Task.Delay(200);
                    if (await desktop.CommandAsync(own.Id, "next") || string.IsNullOrEmpty(desktop.LastCommandError)) throw new InvalidOperationException("Disabled source capability was ignored");
                    if (next != 1) throw new InvalidOperationException("An unsupported command was sent");
                    controls.DisplayUpdater.MusicProperties.Title = ""; controls.DisplayUpdater.Update();
                    bool foundUntitled = false;
                    for (int attempt = 0; attempt < 20 && !foundUntitled; attempt++)
                    {
                        MediaSnapshot snapshot = await desktop.ReadAsync();
                        MediaTrack untitled = snapshot.Tracks.Find(delegate(MediaTrack track) { return track.Id == own.Id; });
                        foundUntitled = untitled != null && string.IsNullOrWhiteSpace(untitled.Title);
                        if (!foundUntitled) await Task.Delay(100);
                    }
                    if (!foundUntitled) throw new InvalidOperationException("Session disappeared when its title became empty");
                }
                finally { controls.IsEnabled = false; controls.DisplayUpdater.ClearAll(); }
            }
        }
    }
}
