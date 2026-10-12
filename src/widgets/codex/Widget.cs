using System.Xml.Linq;
namespace DynamicIsland
{
    internal sealed class CodexWidget : IslandWidget
    {
        public override string Id { get { return "codex"; } }
        public override string Title { get { return "Лимиты Codex"; } }
        public override void ReadSettings(XElement settings, Preferences preferences) { preferences.CodexPath = (string)settings.Element("ExecutablePath") ?? ""; }
        public override XElement WriteSettings(Preferences preferences) { return new XElement("Settings", new XElement("ExecutablePath", preferences.CodexPath)); }
    }
}
