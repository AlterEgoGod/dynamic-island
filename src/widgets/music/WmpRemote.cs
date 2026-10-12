using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Media;

namespace DynamicIsland
{
    // Classic WMP does not publish GSMTC sessions. Use its documented remote control site.
    // Never sets URL, opens media, docks the player, or starts a standalone player.
    public sealed class WmpRemote : IDisposable
    {
        private readonly TaskCompletionSource<Dispatcher> ready = new TaskCompletionSource<Dispatcher>();
        private object player;
        private RemoteSite site;
        private string identity;
        private volatile bool disposed;
        public WmpRemote()
        {
            Thread thread = new Thread(delegate()
            {
                Dispatcher dispatcher = Dispatcher.CurrentDispatcher; ready.SetResult(dispatcher);
                Dispatcher.Run();
            });
            thread.IsBackground = true; thread.Name = "Classic WMP bridge"; thread.SetApartmentState(ApartmentState.STA); thread.Start();
        }
        private async Task<T> Run<T>(Func<T> action)
        {
            Dispatcher dispatcher = await ready.Task.ConfigureAwait(false);
            return await dispatcher.InvokeAsync(action).Task.ConfigureAwait(false);
        }
        internal static object Get(object target, string name) { return target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null); }
        internal static object Call(object target, string name, params object[] args) { return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args); }
        private static void Put(object target, string name, object value) { target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, new[] { value }); }
        private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        private bool Connect()
        {
            if (disposed) return false;
            Process[] processes = Process.GetProcessesByName("wmplayer");
            string found = null;
            foreach (Process process in processes)
                using (process) { try { if (process.MainWindowHandle != IntPtr.Zero) found = "wmp:" + process.Id + ":" + process.StartTime.ToUniversalTime().Ticks; } catch (Exception) { } }
            if (found == null) { Disconnect(); return false; }
            if (player != null && identity == found) return true;
            Disconnect();
            try
            {
                site = new RemoteSite();
                player = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("6BF52A52-394A-11D3-B153-00C04F79FAA6")));
                ((IOlePlayer)player).SetClientSite(site);
                ((IPersistInit)player).InitNew();
                if (!Convert.ToBoolean(Get(player, "isRemote"))) throw new InvalidOperationException("WMP remote connection unavailable");
                identity = found; return true;
            }
            catch { Disconnect(); throw; }
        }
        internal Task<MediaTrack> ReadAsync() { return Run(delegate { try { return Read(); } catch { Disconnect(); throw; } }); }
        private MediaTrack Read()
        {
            if (!Connect()) return null;
            object media = null, controls = null, settings = null;
            try
            {
                MediaTrack track = new MediaTrack { Id = identity, AppId = "wmplayer.exe", Source = "Проигрыватель Windows Media" };
                controls = Get(player, "controls"); settings = Get(player, "settings"); media = Get(player, "currentMedia");
                track.Playing = Convert.ToInt32(Get(player, "playState")) == 3;
                track.CanToggle = Available(controls, track.Playing ? "pause" : "play");
                track.CanNext = Available(controls, "next"); track.CanPrevious = Available(controls, "previous");
                if (media != null)
                {
                    track.Title = Convert.ToString(Get(media, "name"));
                    track.Artist = Convert.ToString(Call(media, "getItemInfo", "Author"));
                    track.Album = Convert.ToString(Call(media, "getItemInfo", "WM/AlbumTitle"));
                    track.Duration = Convert.ToDouble(Get(media, "duration")); track.Position = Convert.ToDouble(Get(controls, "currentPosition"));
                    track.CanSeek = track.Duration > 0 && Available(controls, "currentPosition");
                }
                track.Shuffle = Convert.ToBoolean(Call(settings, "getMode", "shuffle"));
                track.Repeat = Convert.ToBoolean(Call(settings, "getMode", "loop")) ? MediaPlaybackAutoRepeatMode.List : MediaPlaybackAutoRepeatMode.None;
                track.CanShuffle = track.CanRepeat = true;
                return track;
            }
            finally { Release(media); Release(controls); Release(settings); }
        }
        private static bool Available(object controls, string name)
        { return Convert.ToBoolean(controls.GetType().InvokeMember("isAvailable", BindingFlags.GetProperty, null, controls, new object[] { name })); }
        public Task<bool> CommandAsync(string id, string command, double value)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            return Run(delegate
            {
                if (disposed || DateTime.UtcNow >= deadline) return false;
                MediaTrack track = Read(); if (track == null || track.Id != id) return false;
                if (disposed || DateTime.UtcNow >= deadline) return false;
                object controls = null, settings = null;
                try
                {
                    controls = Get(player, "controls"); settings = Get(player, "settings");
                    return ExecuteCommand(track, controls, settings, command, value);
                }
                finally { Release(controls); Release(settings); }
            });
        }
        internal static bool ExecuteCommand(MediaTrack track, object controls, object settings, string command, double value)
        {
            if (command == "toggle" && track.CanToggle) Call(controls, track.Playing ? "pause" : "play");
            else if (command == "next" && track.CanNext) Call(controls, "next");
            else if (command == "previous" && track.CanPrevious) Call(controls, "previous");
            else if (command == "seek" && track.CanSeek && !double.IsNaN(value) && !double.IsInfinity(value)) Put(controls, "currentPosition", Math.Max(0, Math.Min(track.Duration, value)));
            else if (command == "shuffle" && track.CanShuffle) Call(settings, "setMode", "shuffle", track.Shuffle != true);
            else if (command == "repeat" && track.CanRepeat) Call(settings, "setMode", "loop", track.Repeat == MediaPlaybackAutoRepeatMode.None);
            else return false;
            return true;
        }
        private void Disconnect()
        {
            if (player != null) { try { ((IOlePlayer)player).SetClientSite(null); } catch (Exception) { } Release(player); }
            player = null; site = null; identity = null;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            ready.Task.ContinueWith(delegate(Task<Dispatcher> task) { task.Result.BeginInvoke(new Action(delegate { Disconnect(); Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); })); });
        }
        [ComImport, Guid("00000112-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IOlePlayer { void SetClientSite([MarshalAs(UnmanagedType.Interface)] IPlayerSite site); }
        [ComImport, Guid("7FD52380-4E07-101B-AE2D-08002B2EC713"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistInit { void GetClassID(out Guid id); [PreserveSig] int IsDirty(); void Load(IntPtr stream); void Save(IntPtr stream, bool clear); void GetSizeMax(out long size); void InitNew(); }
        [ComVisible(true), Guid("00000118-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IPlayerSite
        {
            [PreserveSig] int SaveObject(); [PreserveSig] int GetMoniker(uint assign, uint which, out IntPtr moniker);
            [PreserveSig] int GetContainer(out IntPtr container); [PreserveSig] int ShowObject();
            [PreserveSig] int OnShowWindow([MarshalAs(UnmanagedType.Bool)] bool show); [PreserveSig] int RequestNewObjectLayout();
        }
        [ComVisible(true), Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IRemoteProvider { [PreserveSig] int QueryService(ref Guid service, ref Guid iid, out IntPtr result); }
        [ComVisible(true), Guid("CBB92747-741F-44FE-AB5B-F1A48F3B2A59"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IRemoteServices
        {
            [PreserveSig] int GetServiceType([MarshalAs(UnmanagedType.BStr)] out string type);
            [PreserveSig] int GetApplicationName([MarshalAs(UnmanagedType.BStr)] out string name);
            [PreserveSig] int GetScriptableObject([MarshalAs(UnmanagedType.BStr)] out string name, out IntPtr value);
            [PreserveSig] int GetCustomUIMode([MarshalAs(UnmanagedType.BStr)] out string mode);
        }
        [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
        public sealed class RemoteSite : IPlayerSite, IRemoteProvider, IRemoteServices
        {
            public int QueryService(ref Guid service, ref Guid iid, out IntPtr result)
            { result = IntPtr.Zero; if (iid != typeof(IRemoteServices).GUID) return unchecked((int)0x80004002); result = Marshal.GetComInterfaceForObject(this, typeof(IRemoteServices)); return 0; }
            public int GetServiceType(out string type) { type = "Remote"; return 0; }
            public int GetApplicationName(out string name) { name = "Dynamic Island"; return 0; }
            public int GetScriptableObject(out string name, out IntPtr value) { name = null; value = IntPtr.Zero; return unchecked((int)0x80004001); }
            public int GetCustomUIMode(out string mode) { mode = null; return unchecked((int)0x80004001); }
            public int SaveObject() { return unchecked((int)0x80004001); }
            public int GetMoniker(uint a, uint b, out IntPtr value) { value = IntPtr.Zero; return unchecked((int)0x80004001); }
            public int GetContainer(out IntPtr value) { value = IntPtr.Zero; return unchecked((int)0x80004002); }
            public int ShowObject() { return 0; } public int OnShowWindow(bool show) { return 0; }
            public int RequestNewObjectLayout() { return unchecked((int)0x80004001); }
        }
    }
}
