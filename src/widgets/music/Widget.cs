using System.Xml.Linq;
namespace DynamicIsland
{
    internal sealed class MusicWidget : IslandWidget
    {
        public override string Id { get { return "music"; } }
        public override string Title { get { return "Музыка"; } }
        public override void ReadSettings(XElement settings, Preferences preferences) { preferences.MusicService = MusicServices.Normalize((string)settings.Element("LegacyService")); }
        public override XElement WriteSettings(Preferences preferences) { return new XElement("Settings", new XElement("Source", "automatic"), new XElement("LegacyService", preferences.MusicService)); }
    }
}
