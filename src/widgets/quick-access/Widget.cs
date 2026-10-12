using System.Collections.Generic;
using System.Xml.Linq;
namespace DynamicIsland
{
    internal sealed class QuickAccessWidget : IslandWidget
    {
        public override string Id { get { return "quick-access"; } }
        public override string Title { get { return "Быстрый доступ"; } }
        public override void ReadSettings(XElement settings, Preferences preferences)
        {
            XElement apps = settings.Element("Apps"); if (apps == null) return;
            preferences.QuickApps = new List<QuickApp>();
            foreach (XElement element in apps.Elements("App")) { QuickApp app = QuickApp.FromXml(element); if (app != null && !preferences.QuickApps.Exists(delegate(QuickApp other) { return other.SameTarget(app.Target); })) preferences.QuickApps.Add(app); }
        }
        public override XElement WriteSettings(Preferences preferences)
        {
            XElement apps = new XElement("Apps"); foreach (QuickApp app in preferences.QuickApps) apps.Add(app.ToXml()); return new XElement("Settings", apps);
        }
    }
}
