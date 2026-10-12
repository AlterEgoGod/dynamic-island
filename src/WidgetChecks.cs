using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace DynamicIsland
{
    internal sealed partial class Island
    {
        private void CheckWidgets(string directory, List<string> results)
        {
            ResetSettings(); ShowSettings(true); Window.UpdateLayout();
            Assert(widgets.Count == 5 && widgetConfiguration.Items.TrueForAll(delegate(WidgetState item) { return item.Enabled; }), "All five widgets are enabled by default", results);
            Assert(widgetRows.Values.All(delegate(Border row) { return ((Grid)((StackPanel)row.Child).Children[0]).Children.Count == 2; }), "Settings headers contain only title and toggle, without reorder controls", results);
            SetWidgetEditing(true);
            Assert(!inlineUp["clock"].IsEnabled && !inlineDown["codex"].IsEnabled, "Reordering stops at first and last widgets in the inline editor", results);
            inlineUp["time"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            SetWidgetEditing(false); ShowSettings(false); Window.UpdateLayout();
            Assert(widgetConfiguration.Items[1].Id == "time" && Find<Border>("StopwatchCard").Parent == Find<Border>("FocusCard").Parent, "Timer and stopwatch move together as one time widget", results);
            SetFocusSeconds(120); focus.Toggle(DateTime.UtcNow); counter.Toggle(DateTime.UtcNow);
            SetWidgetEnabled("time", false);
            Assert(!focus.Running && !counter.Running && !Find<Border>("FocusCard").IsVisible && !Find<Border>("StopwatchCard").IsVisible, "Disabling timers pauses them and removes their views", results);
            SetWidgetEnabled("time", true); Assert(!focus.Running && focus.Duration.TotalSeconds == 120, "Re-enabling focus preserves the paused duration without starting it", results);
            foreach (IIslandWidget widget in widgets) SetWidgetEnabled(widget.Id, false);
            Window.UpdateLayout();
            Assert(Find<StackPanel>("Overview").Children.Count == 1 && Find<Button>("SettingsButton").IsVisible && !mediaPolling.IsEnabled && !limitsPolling.IsEnabled, "All widgets can be disabled while settings remain reachable and polling stops", results);
            ShowSettings(true); Find<ScrollViewer>("SettingsScroll").ScrollToTop(); Window.UpdateLayout();
            Snapshot(Path.Combine(directory, "island-widget-settings-light.png")); ChangeTheme(true); Snapshot(Path.Combine(directory, "island-widget-settings-dark.png"));
            ResetSettings(); ShowSettings(false);
            Assert(widgetConfiguration.Items[0].Id == "clock" && widgetConfiguration.Items[2].Id == "time" && widgetConfiguration.Items.TrueForAll(delegate(WidgetState item) { return item.Enabled; }), "Reset restores widget defaults and ordering", results);
            Assert(IsDragHandle(Find<Border>("Grip")) && IsDragHandle(Find<Border>("CollapsedBar")) && !IsDragHandle(Find<TextBlock>("AppHeading")) && !IsDragHandle(Find<Border>("MusicCard")) && !IsDragHandle(Find<TextBox>("TimerLabel")), "Only the dedicated grip and collapsed strip can initiate dragging", results);
            Border grip = Find<Border>("Grip");
            Point gripBottom = grip.TranslatePoint(new Point(0, grip.ActualHeight), Window);
            Point headerTop = Find<Grid>("HeaderDragZone").TranslatePoint(new Point(), Window);
            Assert(grip.VerticalAlignment == VerticalAlignment.Top && gripBottom.Y <= headerTop.Y, "Top drag grip stays above the header without overlapping its actions", results);
            Find<Button>("EditWidgetsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            inlineSwitches["music"].IsChecked = false; inlineSwitches["music"].RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Assert(editingWidgets && inlineSwitches.Count == 5 && inlineSwitches["music"].IsVisible && !WidgetEnabled("music"), "Inline editor keeps disabled widgets reachable for re-enabling", results);
            inlineUp["time"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(widgetConfiguration.Items[1].Id == "time", "Inline Iconly arrow moves the combined widget", results);
            inlineSwitches["music"].IsChecked = true; inlineSwitches["music"].RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Find<ScrollViewer>("OverviewScroll").ScrollToTop(); ChangeTheme(false); Window.UpdateLayout();
            Snapshot(Path.Combine(directory, "island-inline-edit-light.png")); ChangeTheme(true); Snapshot(Path.Combine(directory, "island-inline-edit-dark.png"));
            grace = DateTime.MinValue; preferences.Delay = 0; HandleHover(false);
            Assert(expanded && editingWidgets, "Inline editing prevents auto-collapse", results);
            SetWidgetEditing(false); Assert(WidgetEnabled("music") && inlineSwitches.Count == 0, "Leaving edit mode keeps changes and hides editing controls", results);
            ResetSettings(); ShowSettings(false); ChangeTheme(false); Window.UpdateLayout(); Snapshot(Path.Combine(directory, "island-time-widget.png"));

            string testRoot = Path.Combine(directory, "widget-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot); string shell = Path.Combine(testRoot, "settings.xml");
            Preferences legacy = new Preferences { FocusSeconds = 91, Dark = true, Delay = 350, CodexPath = "test-codex.exe" };
            legacy.Presets.Clear(); legacy.QuickApps.Clear(); legacy.Save(shell);
            List<IIslandWidget> catalog = WidgetCatalog.Create(); WidgetConfiguration config;
            Preferences migrated = WidgetSettingsStore.Load(shell, catalog, out config);
            Assert(migrated.FocusSeconds == 91 && migrated.Presets.Count == 0 && migrated.QuickApps.Count == 0, "Legacy widget settings migrate without losing custom or empty values", results);
            config.Get("music").Enabled = false; config.Move("music", -1);
            WidgetSettingsStore.Save(shell, migrated, catalog, config);
            Assert(File.Exists(shell + ".legacy.bak") && Preferences.Load(shell + ".legacy.bak").FocusSeconds == 91, "First migration preserves a readable legacy backup", results);
            Assert(migrated.SaveError == null && catalog.All(delegate(IIslandWidget widget) { return File.Exists(WidgetSettingsStore.ModulePath(shell, widget.Id)); }), "Every module writes its own settings folder", results);
            XElement storedShell = XElement.Load(shell);
            Assert(storedShell.Element("FocusSeconds") == null && storedShell.Element("QuickApps") == null && storedShell.Element("CodexPath") == null, "Shell configuration no longer stores widget-specific preferences", results);
            Preferences restored = WidgetSettingsStore.Load(shell, catalog, out config);
            Assert(restored.Dark && restored.Delay == 350 && restored.FocusSeconds == 91 && restored.Presets.Count == 0 && restored.QuickApps.Count == 0 && restored.CodexPath == "test-codex.exe" && !config.Get("music").Enabled && config.Items[2].Id == "music", "Widget values, enabled states and ordering survive reload", results);
            File.WriteAllText(WidgetSettingsStore.ModulePath(shell, "music"), "<broken");
            restored = WidgetSettingsStore.Load(shell, catalog, out config);
            Assert(config.Get("music").Enabled && restored.FocusSeconds == 91, "A malformed module file falls back without losing other modules", results);
            storedShell.Element("Widgets").AddFirst(new XElement("Widget", new XAttribute("id", "../unknown")), new XElement("Widget", new XAttribute("id", "focus")));
            storedShell.Save(shell); WidgetSettingsStore.Load(shell, catalog, out config);
            Assert(config.Items.Count == 5 && config.Items.Select(delegate(WidgetState item) { return item.Id; }).Distinct().Count() == 5, "Unknown and duplicate widget IDs cannot create duplicate views or paths", results);
            string blockedRoot = Path.Combine(testRoot, "blocked"); Directory.CreateDirectory(blockedRoot);
            File.WriteAllText(Path.Combine(blockedRoot, "widgets"), "test path occupied by a file");
            WidgetSettingsStore.Save(Path.Combine(blockedRoot, "settings.xml"), restored, catalog, config);
            Assert(restored.SaveError != null && !File.Exists(Path.Combine(blockedRoot, "settings.xml")), "A module write failure is reported and does not replace shell preferences", results);
            string splitRoot = Path.Combine(testRoot, "split-timers"), splitShell = Path.Combine(splitRoot, "settings.xml"); Directory.CreateDirectory(splitRoot);
            new XElement("Island", new XElement("DefaultsVersion", 2), new XElement("Widgets", new XElement("Widget", new XAttribute("id", "music")), new XElement("Widget", new XAttribute("id", "stopwatch")), new XElement("Widget", new XAttribute("id", "clock")), new XElement("Widget", new XAttribute("id", "focus")))).Save(splitShell);
            string timerPath = WidgetSettingsStore.ModulePath(splitShell, "focus"), stopwatchPath = WidgetSettingsStore.ModulePath(splitShell, "stopwatch");
            Directory.CreateDirectory(Path.GetDirectoryName(timerPath)); Directory.CreateDirectory(Path.GetDirectoryName(stopwatchPath));
            new XElement("Widget", new XAttribute("enabled", false), new XElement("Settings", new XElement("DurationSeconds", 777), new XElement("Presets", new XElement("Minutes", 24)))).Save(timerPath);
            new XElement("Widget", new XAttribute("enabled", true), new XElement("Settings")).Save(stopwatchPath);
            restored = WidgetSettingsStore.Load(splitShell, catalog, out config);
            Assert(config.Items.Count == 5 && config.Items[1].Id == "time" && config.Get("time").Enabled && restored.FocusSeconds == 777 && restored.Presets.SequenceEqual(new[] { 24 }), "Separate timer modules migrate to one enabled time widget at their first position without losing settings", results);
            new XElement("Widget", new XAttribute("enabled", false), new XElement("Settings")).Save(stopwatchPath);
            WidgetSettingsStore.Load(splitShell, catalog, out config);
            Assert(!config.Get("time").Enabled, "Two disabled legacy timers migrate to a disabled time widget", results);
        }
    }
}
