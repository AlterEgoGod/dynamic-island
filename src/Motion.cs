using System;
using System.Windows;
using System.Windows.Media.Animation;
namespace DynamicIsland
{
    internal static class Motion
    {
        public static double Ease(double value)
        {
            double t = Math.Max(0, Math.Min(1, value));
            return t * t * t * (t * (t * 6 - 15) + 10);
        }
    }
    internal sealed class SmoothEase : EasingFunctionBase
    {
        protected override double EaseInCore(double value) { return Motion.Ease(value); }
        protected override Freezable CreateInstanceCore() { return new SmoothEase(); }
    }
}
