using System;
using System.IO;
using System.Media;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace DynamicIsland
{
    // Keep these tokens and docs/DESIGN.md in sync.
    internal static class ThemePalette
    {
        public static readonly string[] Keys = { "Surface", "Card", "Ink", "Muted", "Line", "Accent", "AccentSoft" };
        public static readonly string[] Light = { "#FCFBFF", "#F3F0FA", "#242133", "#665F77", "#EAE5F4", "#7041D9", "#EEE7FF" };
        public static readonly string[] Dark = { "#1C1B24", "#292735", "#F5F2FF", "#C3BBD3", "#51495F", "#BEA0FF", "#3A2D53" };
        public static void Apply(ResourceDictionary resources, bool dark)
        {
            for (int i = 0; i < Keys.Length; i++) resources[Keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString((dark ? Dark : Light)[i]));
        }
        public static double Contrast(string foreground, string background)
        {
            double a = Luminance((Color)ColorConverter.ConvertFromString(foreground));
            double b = Luminance((Color)ColorConverter.ConvertFromString(background));
            return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        }
        private static double Luminance(Color color) { return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B); }
        private static double Linear(byte channel) { double value = channel / 255.0; return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4); }
    }

    internal sealed class TimerCue : IDisposable
    {
        private MemoryStream stream;
        private SoundPlayer player;
        public const int SampleRate = 22050;
        public const double Seconds = 3.3;
        public static byte[] CreateWave()
        {
            int samples = (int)(SampleRate * Seconds);
            using (MemoryStream output = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(output, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(SampleRate); writer.Write(SampleRate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
                double[] notes = { 659.25, 830.61, 987.77 };
                for (int i = 0; i < samples; i++)
                {
                    double value = 0, now = i / (double)SampleRate;
                    for (int note = 0; note < notes.Length; note++)
                    {
                        double t = now - note * 0.85;
                        if (t < 0 || t > 1.6) continue;
                        double envelope = Math.Min(1, t / 0.025) * Math.Exp(-3.2 * t) * Math.Min(1, (1.6 - t) / 0.12);
                        value += 0.18 * envelope * (Math.Sin(2 * Math.PI * notes[note] * t) + 0.25 * Math.Sin(4 * Math.PI * notes[note] * t));
                    }
                    writer.Write((short)(Math.Max(-1, Math.Min(1, value)) * short.MaxValue));
                }
                return output.ToArray();
            }
        }
        public void Play()
        {
            Stop();
            if (player == null) { stream = new MemoryStream(CreateWave(), false); player = new SoundPlayer(stream); }
            try { player.Play(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        }
        public void Stop() { if (player != null) player.Stop(); }
        public void Dispose() { if (player != null) { player.Stop(); player.Dispose(); } if (stream != null) stream.Dispose(); }
    }
}
