using System;
using System.Globalization;
namespace DynamicIsland
{
    internal sealed class FocusTimer
    {
        public TimeSpan Duration { get; private set; }
        private TimeSpan remaining;
        private DateTime deadline;
        public bool Running { get; private set; }
        public FocusTimer() { Reset(); }
        public void SetMinutes(int minutes)
        {
            if (minutes < 1) throw new ArgumentOutOfRangeException("minutes");
            SetSeconds((long)minutes * 60);
        }
        public void SetSeconds(long seconds)
        {
            if (seconds < 0 || seconds > FocusTimeInput.MaxSeconds) throw new ArgumentOutOfRangeException("seconds");
            Duration = TimeSpan.FromSeconds(seconds); Reset();
        }
        public TimeSpan Remaining(DateTime now) { return Running ? (deadline > now ? deadline - now : TimeSpan.Zero) : remaining; }
        public void Toggle(DateTime now)
        {
            if (Duration == TimeSpan.Zero) return;
            if (Running) { remaining = Remaining(now); Running = false; }
            else { if (remaining <= TimeSpan.Zero) remaining = Duration; deadline = now + remaining; Running = true; }
        }
        public bool Tick(DateTime now)
        {
            if (!Running || Remaining(now) > TimeSpan.Zero) return false;
            remaining = TimeSpan.Zero; Running = false; return true;
        }
        public void Reset() { Running = false; remaining = Duration; }
        public bool AddMinute(DateTime now)
        {
            if (Duration == TimeSpan.Zero || Duration.TotalSeconds > FocusTimeInput.MaxSeconds - 60) return false;
            if (Running && Remaining(now) == TimeSpan.Zero) { Running = false; remaining = TimeSpan.Zero; }
            Duration += TimeSpan.FromMinutes(1);
            if (Running) deadline += TimeSpan.FromMinutes(1); else remaining += TimeSpan.FromMinutes(1);
            return true;
        }
        public double Progress(DateTime now) { return Duration == TimeSpan.Zero ? 0 : Math.Max(0, Math.Min(1, 1 - Remaining(now).TotalSeconds / Duration.TotalSeconds)); }
    }

    internal static class FocusTimeInput
    {
        // Storage/DateTime safety bound, not a product limit on timer length.
        public const long MaxSeconds = (long)int.MaxValue * 60 + 59;
        public static bool TryParse(string text, out long totalSeconds)
        {
            totalSeconds = 0;
            string[] parts = (text ?? "").Trim().Split(':');
            if (parts.Length == 3)
            {
                long hours; int minutePart, secondPart;
                if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hours) || hours > MaxSeconds / 3600 ||
                    !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minutePart) || minutePart > 59 ||
                    !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out secondPart) || secondPart > 59) return false;
                totalSeconds = hours * 3600 + minutePart * 60 + secondPart;
                return totalSeconds > 0 && totalSeconds <= MaxSeconds;
            }
            int minutes, seconds = 0;
            if (parts.Length < 1 || parts.Length > 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out minutes)) return false;
            if (parts.Length == 2 && (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out seconds) || seconds > 59)) return false;
            totalSeconds = (long)minutes * 60 + seconds;
            return totalSeconds >= 1 && totalSeconds <= MaxSeconds;
        }
        public static string Format(long totalSeconds) { return (totalSeconds / 60).ToString("00") + ":" + (totalSeconds % 60).ToString("00"); }
    }

}
