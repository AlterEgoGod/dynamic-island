using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

using System.Windows.Controls.Primitives;
namespace DynamicIsland
{
    internal sealed partial class Island
    {
        private void UpdateStopwatch()
        {
            TimeSpan elapsed = counter.Elapsed(DateTime.UtcNow);
            Find<TextBlock>("StopwatchLabel").Text = CounterStopwatch.Format(elapsed);
            Find<TextBlock>("StopwatchStatus").Text = counter.Running ? "Идёт отсчёт" : elapsed > TimeSpan.Zero ? "На паузе" : "Готов к старту";
            if (counterShowingPause != counter.Running)
            {
                counterShowingPause = counter.Running;
                Find<ContentControl>("StopwatchIcon").Content = Icons.Load(counter.Running ? "pause" : "play");
            }
            if (counter.Running) counterTicks.Start(); else counterTicks.Stop();
        }

    }
}
