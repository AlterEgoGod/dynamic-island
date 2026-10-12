using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland
{
    internal static class MusicCheck
    {
        public static int Run(bool online = false)
        {
            string directory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"));
            Directory.CreateDirectory(directory);
            int exit = 1;
            Application app = new Application();
            Grid host = new Grid();
            Window window = new Window { Title = "Dynamic Island · offline player test", Width = 480, Height = 680, AllowsTransparency = true, WindowStyle = WindowStyle.None, Background = Brushes.Transparent, Content = host, ShowInTaskbar = false, ShowActivated = false };
            MusicPlayer player = new MusicPlayer(window, host, true);
            window.Loaded += async delegate
            {
                try
                {
                    Task check = online ? player.ProbeServicesAsync(directory) : player.VerifyOfflineAsync();
                    if (await Task.WhenAny(check, Task.Delay(online ? 110000 : 30000)) != check) throw new TimeoutException("WebView2 check timed out.");
                    await check;
                    await Task.Delay(500);
                    window.UpdateLayout();
                    RenderTargetBitmap bitmap = new RenderTargetBitmap(480, 680, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                    PngBitmapEncoder png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                    using (Stream file = File.Create(Path.Combine(directory, online ? "music-probe-window.png" : "music-webview.png"))) png.Save(file);
                    if (!online) File.WriteAllText(Path.Combine(directory, "music-test.txt"), "PASS: WebView2 composition initializes in a transparent WPF window; offline page, interaction, mute, theme and retained view while hidden. No service login or protected-stream test performed.");
                    exit = 0;
                }
                catch (Exception error) { File.WriteAllText(Path.Combine(directory, online ? "music-probe.txt" : "music-test.txt"), "FAIL: " + error); }
                finally { player.Dispose(); window.Close(); }
            };
            app.Run(window); return exit;
        }
    }
}
