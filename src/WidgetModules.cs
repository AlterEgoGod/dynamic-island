using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Windows;

namespace DynamicIsland
{
    // Built-in plugin contract. Views, persistence and lifecycle are owned by each module.
    // Shell services are injected; widgets do not create another Window or depend on each other.
    internal interface IIslandWidget
    {
        string Id { get; }
        string Title { get; }
        bool Compact { get; }
        FrameworkElement View { get; set; }
        FrameworkElement SettingsView { get; set; }
        void ReadSettings(XElement settings, Preferences preferences);
        XElement WriteSettings(Preferences preferences);
        void SetEnabled(bool enabled, Action<string, bool> lifecycle);
    }
    internal abstract class IslandWidget : IIslandWidget
    {
        public abstract string Id { get; }
        public abstract string Title { get; }
        public virtual bool Compact { get { return false; } }
        public FrameworkElement View { get; set; }
        public FrameworkElement SettingsView { get; set; }
        private bool? active;
        public virtual void ReadSettings(XElement settings, Preferences preferences) { }
        public virtual XElement WriteSettings(Preferences preferences) { return new XElement("Settings"); }
        public void SetEnabled(bool enabled, Action<string, bool> lifecycle)
        {
            if (active == enabled) return;
            active = enabled; lifecycle(Id, enabled);
        }
    }
    internal sealed class WidgetState
    {
        public string Id;
        public bool Enabled = true;
        public WidgetState(string id) { Id = id; }
    }
    internal static class WidgetCatalog
    {
        public static List<IIslandWidget> Create()
        {
            return new List<IIslandWidget> { new ClockWidget(), new QuickAccessWidget(), new TimeWidget(), new MusicWidget(), new CodexWidget() };
        }
    }
    internal sealed class WidgetConfiguration
    {
        public readonly List<WidgetState> Items = new List<WidgetState>();
        public WidgetConfiguration(IEnumerable<IIslandWidget> widgets) { foreach (var widget in widgets) Items.Add(new WidgetState(widget.Id)); }
        public WidgetState Get(string id) { return Items.Find(delegate(WidgetState item) { return item.Id == id; }); }
        public bool Move(string id, int offset)
        {
            int index = Items.FindIndex(delegate(WidgetState item) { return item.Id == id; });
            int target = index + offset;
            if (index < 0 || target < 0 || target >= Items.Count) return false;
            WidgetState entry = Items[index]; Items.RemoveAt(index); Items.Insert(target, entry); return true;
        }
    }
    internal static class WidgetSettingsStore
    {
        public static string ModulePath(string shellPath, string id)
        {
            if (id.Length == 0 || id.Any(delegate(char c) { return !(c >= 'a' && c <= 'z') && c != '-'; })) throw new ArgumentException("Invalid widget ID");
            return Path.Combine(Path.GetDirectoryName(shellPath), "widgets", id, "settings.xml");
        }
        private static XElement Read(string path)
        {
            try { return File.Exists(path) ? XElement.Load(path) : null; }
            catch (IOException) { return null; } catch (UnauthorizedAccessException) { return null; } catch (XmlException) { return null; }
        }
        public static Preferences Load(string shellPath, List<IIslandWidget> widgets, out WidgetConfiguration configuration)
        {
            // Legacy monolithic settings remain a fallback until a module has its own file.
            Preferences preferences = Preferences.Load(shellPath);
            configuration = new WidgetConfiguration(widgets);
            XElement shell = Read(shellPath);
            if (shell != null && shell.Element("Widgets") != null)
            {
                List<WidgetState> ordered = new List<WidgetState>();
                foreach (XElement element in shell.Element("Widgets").Elements("Widget"))
                {
                    string id = (string)element.Attribute("id");
                    WidgetState state = configuration.Get(id == "focus" || id == "stopwatch" ? "time" : id);
                    if (state != null && !ordered.Contains(state)) ordered.Add(state);
                }
                foreach (WidgetState state in configuration.Items) if (!ordered.Contains(state)) ordered.Add(state);
                configuration.Items.Clear(); configuration.Items.AddRange(ordered);
            }
            foreach (IIslandWidget widget in widgets)
            {
                XElement root = Read(ModulePath(shellPath, widget.Id));
                if (widget.Id == "time" && !File.Exists(ModulePath(shellPath, "time")))
                {
                    XElement timer = Read(ModulePath(shellPath, "focus")), stopwatch = Read(ModulePath(shellPath, "stopwatch"));
                    if (timer != null || stopwatch != null)
                    {
                        bool timerEnabled = true, stopwatchEnabled = true, flag;
                        if (timer != null && bool.TryParse((string)timer.Attribute("enabled"), out flag)) timerEnabled = flag;
                        if (stopwatch != null && bool.TryParse((string)stopwatch.Attribute("enabled"), out flag)) stopwatchEnabled = flag;
                        root = new XElement("Widget", new XAttribute("enabled", timerEnabled || stopwatchEnabled), timer == null ? new XElement("Settings") : timer.Element("Settings"));
                    }
                }
                if (root == null) continue;
                bool enabled;
                if (bool.TryParse((string)root.Attribute("enabled"), out enabled)) configuration.Get(widget.Id).Enabled = enabled;
                widget.ReadSettings(root.Element("Settings") ?? new XElement("Settings"), preferences);
            }
            return preferences;
        }
        private static void AtomicWrite(string path, XElement root)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string data = root.ToString();
            if (File.Exists(path) && File.ReadAllText(path) == data) return;
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, data, new System.Text.UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        public static void Save(string shellPath, Preferences preferences, List<IIslandWidget> widgets, WidgetConfiguration configuration)
        {
            try
            {
                XElement previous = Read(shellPath);
                string backup = shellPath + ".legacy.bak";
                if (previous != null && previous.Element("Widgets") == null && !File.Exists(backup)) File.Copy(shellPath, backup);
                foreach (IIslandWidget widget in widgets)
                    AtomicWrite(ModulePath(shellPath, widget.Id), new XElement("Widget", new XAttribute("version", 1), new XAttribute("enabled", configuration.Get(widget.Id).Enabled), widget.WriteSettings(preferences)));
                // Write shell last so an interrupted first migration retains its legacy fallback.
                XElement order = new XElement("Widgets");
                foreach (WidgetState state in configuration.Items) order.Add(new XElement("Widget", new XAttribute("id", state.Id)));
                AtomicWrite(shellPath, new XElement("Island", new XElement("DefaultsVersion", 2), new XElement("Edge", preferences.Edge), new XElement("Monitor", preferences.Monitor), new XElement("Dark", preferences.Dark), new XElement("Pinned", preferences.Pinned), new XElement("Delay", preferences.Delay), new XElement("CenterOnEdge", preferences.CenterOnEdge), new XElement("EdgePosition", preferences.EdgePosition.ToString(System.Globalization.CultureInfo.InvariantCulture)), order));
                preferences.SaveError = null;
            }
            catch (IOException) { preferences.SaveError = "Не удалось сохранить настройки виджетов"; }
            catch (UnauthorizedAccessException) { preferences.SaveError = "Нет доступа к настройкам виджетов"; }
        }
    }
}
