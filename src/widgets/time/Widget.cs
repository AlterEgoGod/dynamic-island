using System.Collections.Generic;
using System.Xml.Linq;
namespace DynamicIsland
{
    internal sealed class TimeWidget : IslandWidget
    {
        public override string Id { get { return "time"; } }
        public override string Title { get { return "Время"; } }

        public override void ReadSettings(XElement settings, Preferences preferences)
        {
            long seconds;
            if (long.TryParse((string)settings.Element("DurationSeconds"), out seconds) && seconds >= 0 && seconds <= FocusTimeInput.MaxSeconds) preferences.FocusSeconds = seconds;
            XElement presets = settings.Element("Presets");
            if (presets == null) return;
            preferences.Presets = new List<int>();
            foreach (XElement item in presets.Elements("Minutes")) { int minutes; if (int.TryParse(item.Value, out minutes) && minutes > 0 && !preferences.Presets.Contains(minutes)) preferences.Presets.Add(minutes); }
            preferences.Presets.Sort();
        }
        public override XElement WriteSettings(Preferences preferences)
        {
            XElement presets = new XElement("Presets"); foreach (int minutes in preferences.Presets) presets.Add(new XElement("Minutes", minutes));
            return new XElement("Settings", new XElement("DurationSeconds", preferences.FocusSeconds), presets);
        }
    }
}
