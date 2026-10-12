using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.IO;
using System.Windows.Media.Imaging;
using Windows.Media;
using Windows.Storage.Streams;
using Windows.Media.Control;

namespace DynamicIsland
{
    internal sealed class MediaTrack
    {
        public string Id = "", Title = "", Artist = "", Source = "";
        public bool Playing, CanToggle, CanPrevious, CanNext;
        public double Position, Duration;
        public string AppId = "", Album = "";
        public string CompatibilityNote = "";
        public bool PlaybackKnown = true;
        public bool CanSeek, CanShuffle, CanRepeat;
        public bool? Shuffle;
        public MediaPlaybackAutoRepeatMode? Repeat;
        public BitmapSource Artwork;
        public override string ToString() { return Source + " · " + Title; }
        public static string Time(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;
            return ((long)seconds / 60).ToString("0") + ":" + ((long)seconds % 60).ToString("00");
        }
        public string DisplayTitle { get { return string.IsNullOrWhiteSpace(Title) ? "Название трека недоступно" : Title; } }
    }
    internal sealed class MediaSnapshot
    {
        public List<MediaTrack> Tracks = new List<MediaTrack>();
        public MediaTrack Current;
        public string AutomaticId;
        public string Error = "";
        public void Choose(string currentId, string selectedId, string lastId)
        {
            if (selectedId != null) Current = Tracks.Find(delegate(MediaTrack track) { return track.Id == selectedId; });
            else
            {
                Current = Tracks.Find(delegate(MediaTrack track) { return track.Id == currentId; });
                if (Current == null) Current = Tracks.Find(delegate(MediaTrack track) { return track.Id == lastId; });
                if (Current == null) Current = Tracks.Find(delegate(MediaTrack track) { return track.Playing; });
                if (Current == null && Tracks.Count > 0) Current = Tracks[0];
            }
            if (Current == null && selectedId != null) Error = "Выбранный источник закрыт. Выберите другой.";
        }
    }

    // Windows exposes playback metadata and commands, not login state or browser cookies.
    internal sealed class DesktopMedia : IDisposable
    {
        private GlobalSystemMediaTransportControlsSessionManager manager;
        private readonly Dictionary<string, GlobalSystemMediaTransportControlsSession> sessions = new Dictionary<string, GlobalSystemMediaTransportControlsSession>();
        private bool disposed;
        private readonly WmpRemote classic = new WmpRemote();
        private Task<MediaTrack> classicRead;
        private MediaTrack classicTrack;
        private DateTime classicRetry;
        public string LastCommandError { get; private set; }
        private string selectedId, lastId;
        private DateTime retryAfter;
        private readonly Dictionary<string, Task<BitmapSource>> covers = new Dictionary<string, Task<BitmapSource>>();
        private readonly Dictionary<string, DateTime> coverTimes = new Dictionary<string, DateTime>();
        public void Select(string id) { selectedId = string.IsNullOrEmpty(id) ? null : id; }
        private static string Id(GlobalSystemMediaTransportControlsSession session)
        {
            if (session == null) return null;
            IntPtr identity = Marshal.GetIUnknownForObject(session);
            try { return identity.ToInt64().ToString("x"); }
            finally { Marshal.Release(identity); }
        }
        private static async Task<T> Timed<T>(Task<T> task)
        {
            if (await Task.WhenAny(task, Task.Delay(4000)) != task)
            {
                ObserveFailure(task);
                throw new TimeoutException();
            }
            return await task;
        }
        private static void ObserveFailure<T>(Task<T> task) { task.ContinueWith(delegate(Task<T> failed) { var observed = failed.Exception; }, TaskContinuationOptions.OnlyOnFaulted); }
        public async Task<MediaSnapshot> ReadAsync()
        {
            MediaSnapshot result = new MediaSnapshot();
            if (disposed) return result;
            if (DateTime.UtcNow < retryAfter)
            {
                result.Error = "Управление музыкой временно недоступно";
                await AddCompatibilitySources(result); result.Choose(result.AutomaticId, selectedId, lastId); return result;
            }
            try
            {
                if (manager == null) manager = await Timed(System.WindowsRuntimeSystemExtensions.AsTask<GlobalSystemMediaTransportControlsSessionManager>(GlobalSystemMediaTransportControlsSessionManager.RequestAsync()));
                if (disposed) return result;
                var active = manager.GetSessions();
                string currentId = Id(manager.GetCurrentSession());
                result.AutomaticId = currentId;
                Dictionary<string, GlobalSystemMediaTransportControlsSession> fresh = new Dictionary<string, GlobalSystemMediaTransportControlsSession>();
                // Read metadata concurrently so one unresponsive tab does not delay every other source.
                List<Task<MediaTrack>> reads = new List<Task<MediaTrack>>();
                foreach (var session in active)
                {
                    string id = Id(session); fresh[id] = session; reads.Add(ReadTrack(session, id));
                }
                MediaTrack[] tracks = await Task.WhenAll(reads);
                if (disposed) return result;
                sessions.Clear(); foreach (var pair in fresh) sessions[pair.Key] = pair.Value;
                foreach (MediaTrack track in tracks) if (track != null) result.Tracks.Add(track);
            }
            catch (Exception)
            {
                manager = null; sessions.Clear(); retryAfter = DateTime.UtcNow.AddSeconds(15);
                result.Error = "Windows не предоставила доступ к проигрывателям. Повторяем подключение…";
            }
            // Discovery is independent: an unavailable GSMTC API must not hide legacy/audio sources.
            await AddCompatibilitySources(result);
            result.Choose(result.AutomaticId, selectedId, lastId);
            if (result.Current != null) lastId = result.Current.Id;
            return result;
        }
        private async Task AddCompatibilitySources(MediaSnapshot snapshot)
        {
            if (disposed) return;
            if (classicRead == null && DateTime.UtcNow >= classicRetry) classicRead = classic.ReadAsync();
            if (classicRead != null)
            {
                // The first remote COM attachment can take seconds. Never block the UI or queue more reads.
                await Task.WhenAny(classicRead, Task.Delay(100));
                if (classicRead.IsCompleted)
                {
                    try { classicTrack = await classicRead; }
                    catch (Exception) { classicTrack = null; classicRetry = DateTime.UtcNow.AddSeconds(15); }
                    classicRead = null;
                }
            }
            if (classicTrack != null && !snapshot.Tracks.Exists(delegate(MediaTrack item) { return AppVolume.ProcessName(item.AppId) == "wmplayer"; })) snapshot.Tracks.Add(classicTrack);
            var audio = await Task.Run(delegate { return AppVolume.Discover(snapshot.Tracks); });
            snapshot.Tracks.AddRange(audio);
            if (snapshot.Tracks.Count > 0) snapshot.Error = "";
            MediaTrack active = snapshot.Tracks.Find(delegate(MediaTrack track) { return track.Id == snapshot.AutomaticId && track.Playing; });
            if (active == null)
            {
                active = snapshot.Tracks.Find(delegate(MediaTrack track) { return track.PlaybackKnown && track.Playing; });
                if (active != null) snapshot.AutomaticId = active.Id;
            }
        }
        private async Task<MediaTrack> ReadTrack(GlobalSystemMediaTransportControlsSession session, string id)
        {
            MediaTrack track;
            try
            {
                string appId = session.SourceAppUserModelId;
                var info = session.GetPlaybackInfo(); var controls = info.Controls;
                track = new MediaTrack { Id = id, AppId = appId, Source = MediaSourceNames.Fallback(appId),
                    CanSeek = controls.IsPlaybackPositionEnabled, CanShuffle = controls.IsShuffleEnabled, CanRepeat = controls.IsRepeatEnabled, Shuffle = info.IsShuffleActive, Repeat = info.AutoRepeatMode,
                    Playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    CanToggle = controls.IsPlayPauseToggleEnabled || (info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ? controls.IsPauseEnabled : controls.IsPlayEnabled), CanPrevious = controls.IsPreviousEnabled, CanNext = controls.IsNextEnabled };
                try
                {
                    var timeline = session.GetTimelineProperties();
                    track.Duration = Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds);
                    double position = Math.Max(0, (timeline.Position - timeline.StartTime).TotalSeconds);
                    if (track.Playing && info.PlaybackRate.HasValue) position += Math.Max(0, (DateTimeOffset.UtcNow - timeline.LastUpdatedTime).TotalSeconds) * info.PlaybackRate.Value;
                    track.Position = track.Duration > 0 ? Math.Min(track.Duration, position) : 0;
                }
                catch (Exception) { }
                Task<string> name = MediaSourceNames.Resolve(appId);
                if (name.Status == TaskStatus.RanToCompletion) track.Source = name.Result;
            }
            catch (Exception) { return null; } // Closed session, not merely missing metadata.
            try
            {
                var properties = await Timed(System.WindowsRuntimeSystemExtensions.AsTask<GlobalSystemMediaTransportControlsSessionMediaProperties>(session.TryGetMediaPropertiesAsync()));
                track.Title = properties.Title ?? ""; track.Artist = properties.Artist ?? ""; track.Album = properties.AlbumTitle ?? "";
                string coverKey = id + "\n" + track.Title + "\n" + track.Artist + "\n" + track.Album;
                Task<BitmapSource> cover;
                lock (covers)
                {
                    if (properties.Thumbnail == null) cover = Task.FromResult<BitmapSource>(null);
                    else if (!covers.TryGetValue(coverKey, out cover) || (cover.Status == TaskStatus.RanToCompletion && cover.Result == null && DateTime.UtcNow - coverTimes[coverKey] > TimeSpan.FromSeconds(15)))
                    {
                        if (covers.Count >= 24) { covers.Clear(); coverTimes.Clear(); }
                        cover = ReadCover(properties.Thumbnail); covers[coverKey] = cover; coverTimes[coverKey] = DateTime.UtcNow;
                    }
                }
                track.Artwork = cover.Status == TaskStatus.RanToCompletion ? cover.Result : null;
            }
            catch (Exception) { } // A player without a title must still remain selectable.
            Task<string> resolvedName = MediaSourceNames.Resolve(track.AppId);
            if (resolvedName.Status == TaskStatus.RanToCompletion) track.Source = resolvedName.Result;
            return track;
        }
        private static async Task<BitmapSource> ReadCover(IRandomAccessStreamReference reference)
        {
            if (reference == null) return null;
            try
            {
                using (var cancellation = new System.Threading.CancellationTokenSource(4000))
                using (IRandomAccessStreamWithContentType random = await System.WindowsRuntimeSystemExtensions.AsTask<IRandomAccessStreamWithContentType>(reference.OpenReadAsync(), cancellation.Token))
                {
                    if (random.Size == 0 || random.Size > 8 * 1024 * 1024) return null;
                    using (Stream input = random.AsStreamForRead())
                    using (MemoryStream data = new MemoryStream())
                    {
                        await input.CopyToAsync(data, 81920, cancellation.Token); data.Position = 0;
                        BitmapImage image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 192; image.StreamSource = data; image.EndInit(); image.Freeze(); return image;
                    }
                }
            }
            catch (Exception) { return null; }
        }
        public async Task<bool> CommandAsync(string id, string command, double value = 0)
        {
            LastCommandError = "";
            if (id != null && id.StartsWith("wmp:", StringComparison.Ordinal))
            {
                try { if (!disposed && await Timed(classic.CommandAsync(id, command, value))) return true; }
                catch (Exception) { }
                LastCommandError = "Windows Media Player не выполнил команду или источник уже закрыт."; return false;
            }
            GlobalSystemMediaTransportControlsSession session;
            if (disposed || id == null || !sessions.TryGetValue(id, out session)) { LastCommandError = "Выбранный источник закрыт. Выберите другой."; return false; }
            try
            {
                // Refresh the exact session before using its capabilities; never redirect to a different source.
                GlobalSystemMediaTransportControlsSession fresh = null;
                foreach (var candidate in manager.GetSessions()) if (Id(candidate) == id) { fresh = candidate; break; }
                if (fresh == null) { LastCommandError = "Выбранный источник закрыт. Выберите другой."; return false; }
                session = fresh;
                var controls = session.GetPlaybackInfo().Controls;
                Windows.Foundation.IAsyncOperation<bool> operation;
                if (command == "previous" && controls.IsPreviousEnabled) operation = session.TrySkipPreviousAsync();
                else if (command == "next" && controls.IsNextEnabled) operation = session.TrySkipNextAsync();
                else if (command == "toggle" && controls.IsPlayPauseToggleEnabled) operation = session.TryTogglePlayPauseAsync();
                else if (command == "toggle" && session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && controls.IsPauseEnabled) operation = session.TryPauseAsync();
                else if (command == "toggle" && controls.IsPlayEnabled) operation = session.TryPlayAsync();
                else if (command == "seek" && controls.IsPlaybackPositionEnabled)
                {
                    var timeline = session.GetTimelineProperties();
                    if (double.IsNaN(value) || double.IsInfinity(value) || timeline.EndTime <= timeline.StartTime) return false;
                    double seconds = Math.Max(0, Math.Min((timeline.EndTime - timeline.StartTime).TotalSeconds, value));
                    operation = session.TryChangePlaybackPositionAsync(timeline.StartTime.Ticks + TimeSpan.FromSeconds(seconds).Ticks);
                }
                else if (command == "shuffle" && controls.IsShuffleEnabled && session.GetPlaybackInfo().IsShuffleActive.HasValue)
                    operation = session.TryChangeShuffleActiveAsync(!session.GetPlaybackInfo().IsShuffleActive.Value);
                else if (command == "repeat" && controls.IsRepeatEnabled && session.GetPlaybackInfo().AutoRepeatMode.HasValue)
                {
                    var mode = session.GetPlaybackInfo().AutoRepeatMode.Value;
                    operation = session.TryChangeAutoRepeatModeAsync(mode == MediaPlaybackAutoRepeatMode.None ? MediaPlaybackAutoRepeatMode.List : mode == MediaPlaybackAutoRepeatMode.List ? MediaPlaybackAutoRepeatMode.Track : MediaPlaybackAutoRepeatMode.None);
                }
                else { LastCommandError = "Проигрыватель не предоставляет эту команду для текущего трека."; return false; }
                bool accepted = await Timed(System.WindowsRuntimeSystemExtensions.AsTask<bool>(operation));
                if (!accepted) LastCommandError = "Проигрыватель отклонил команду. Проверьте очередь в приложении или вкладке.";
                return accepted;
            }
            catch (TimeoutException) { LastCommandError = "Проигрыватель не ответил вовремя. Команда не повторяется автоматически."; return false; }
            catch (Exception) { LastCommandError = "Не удалось связаться с выбранным проигрывателем."; return false; }
        }
        public void Dispose() { disposed = true; classic.Dispose(); sessions.Clear(); manager = null; lock (covers) { covers.Clear(); coverTimes.Clear(); } }
    }
    internal static class DesktopMediaCheck
    {
        public static int Run()
        {
            string directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"));
            System.IO.Directory.CreateDirectory(directory);
            try
            {
                MediaSnapshot snapshot = Task.Run(async delegate
                {
                    using (DesktopMedia media = new DesktopMedia())
                    {
                        MediaSnapshot read = await media.ReadAsync();
                        for (int attempt = 0; attempt < 40 && read.Tracks.Exists(delegate(MediaTrack track) { return track.Id.StartsWith("wmp:") && !track.PlaybackKnown; }); attempt++)
                        { await Task.Delay(250); read = await media.ReadAsync(); }
                        return read;
                    }
                }).GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(snapshot.Error)) throw new InvalidOperationException(snapshot.Error);
                int volumes = Task.Run(delegate { int available = 0; foreach (MediaTrack track in snapshot.Tracks) if (AppVolume.Read(track.AppId).HasValue) available++; return available; }).GetAwaiter().GetResult();
                var capabilities = new List<string>();
                foreach (MediaTrack track in snapshot.Tracks) capabilities.Add(track.Source + ": playbackKnown=" + track.PlaybackKnown + ", playing=" + track.Playing + ", next=" + track.CanNext + ", previous=" + track.CanPrevious + ", toggle=" + track.CanToggle + ", seek=" + track.CanSeek);
                File.WriteAllLines(Path.Combine(directory, "media-capabilities.txt"), capabilities);
                System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "media-test.txt"), "PASS: Windows media sessions API available. Sources: " + snapshot.Tracks.Count + "; readable application volumes: " + volumes + ". Read-only check; no playback/volume changes or account data recorded.");
                return 0;
            }
            catch (Exception error) { System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "media-test.txt"), "FAIL: " + error); return 1; }
        }
    }
}
