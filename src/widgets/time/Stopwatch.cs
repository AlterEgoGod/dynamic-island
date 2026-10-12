using System;
namespace DynamicIsland
{
    internal sealed class CounterStopwatch
    {
        private TimeSpan accumulated;
        private DateTime started;
        public bool Running { get; private set; }
        public TimeSpan Elapsed(DateTime now) { return accumulated + (Running && now > started ? now - started : TimeSpan.Zero); }
        public void Toggle(DateTime now)
        {
            if (Running) { accumulated = Elapsed(now); Running = false; }
            else { started = now; Running = true; }
        }
        public void Reset() { accumulated = TimeSpan.Zero; Running = false; }
        public static string Format(TimeSpan elapsed)
        {
            return elapsed.TotalHours >= 1 ? ((int)elapsed.TotalHours).ToString("00") + elapsed.ToString(@"\:mm\:ss") : ((int)elapsed.TotalMinutes).ToString("00") + elapsed.ToString(@"\:ss\.f");
        }
    }

}
