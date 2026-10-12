using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Foundation.Metadata;

namespace DynamicIsland
{
    internal static class MediaSourceNames
    {
        private static readonly Dictionary<string, Task<string>> cache = new Dictionary<string, Task<string>>(StringComparer.OrdinalIgnoreCase);
        public static Task<string> Resolve(string appId)
        {
            string id = appId ?? "";
            lock (cache)
            {
                Task<string> name;
                if (cache.TryGetValue(id, out name)) return name;
                if (cache.Count >= 128) cache.Clear();
                name = Task.Run(delegate
                {
                    try
                    {
                        if (ApiInformation.IsMethodPresent("Windows.ApplicationModel.AppInfo", "GetFromAppUserModelId"))
                        {
                            AppInfo app = AppInfo.GetFromAppUserModelId(id);
                            if (app != null && !string.IsNullOrWhiteSpace(app.DisplayInfo.DisplayName)) return app.DisplayInfo.DisplayName;
                        }
                    }
                    catch (Exception) { }
                    return Fallback(id);
                });
                cache[id] = name; return name;
            }
        }
        public static string Fallback(string id)
        {
            string lower = (id ?? "").ToLowerInvariant();
            if (lower.Contains("yandexmusic") || lower.Contains("yandex.music") || lower.Contains("yandex-music")) return "Яндекс Музыка";
            if (lower.Contains("spotify")) return "Spotify";
            if (lower.Contains("msedge")) return "Microsoft Edge";
            if (lower.Contains("chrome")) return "Google Chrome";
            if (lower.Contains("firefox")) return "Firefox";
            if (lower.Contains("brave")) return "Brave";
            if (lower.Contains("opera")) return "Opera";
            if (lower.Contains("aimp")) return "AIMP";
            if (lower.Contains("foobar2000")) return "foobar2000";
            if (lower.Contains("vlc")) return "VLC media player";
            if (lower.Contains("wmplayer")) return "Windows Media Player";
            if (lower.Contains("zunemusic")) return "Медиаплеер Windows";
            if (lower.Contains("zunevideo")) return "Кино и ТВ";
            if (lower.Contains("music.ui")) return "Музыка";
            if (lower.Contains("yandex") || lower == "browser.exe") return "Яндекс Браузер";
            if (lower.Contains("dynamicisland")) return "Плеер острова";
            if (string.IsNullOrWhiteSpace(id)) return "Неизвестный проигрыватель";
            // Keep unknown app names distinguishable. Do not expose full filesystem paths.
            try
            {
                string name = Path.GetFileName(id);
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(name);
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            catch (ArgumentException) { }
            return "Неизвестный проигрыватель";
        }
    }
}
