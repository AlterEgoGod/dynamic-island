using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;

namespace DynamicIsland
{
    internal sealed partial class Island
    {
        private readonly List<IIslandWidget> widgets = WidgetCatalog.Create();
        private WidgetConfiguration widgetConfiguration;
        private readonly Dictionary<string, Border> widgetRows = new Dictionary<string, Border>();
        private readonly Dictionary<string, CheckBox> widgetSwitches = new Dictionary<string, CheckBox>();
        private bool widgetLifecycleReady;
        private bool editingWidgets;
        private readonly Dictionary<string, CheckBox> inlineSwitches = new Dictionary<string, CheckBox>();
        private readonly Dictionary<string, Button> inlineUp = new Dictionary<string, Button>(), inlineDown = new Dictionary<string, Button>();

        private bool WidgetEnabled(string id) { return widgetConfiguration == null || widgetConfiguration.Get(id).Enabled; }
        private FrameworkElement LoadWidgetView(string id, string file)
        {
            XElement xml;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("widgets." + id + "." + file)) xml = XElement.Load(stream);
            FrameworkElement root = (FrameworkElement)XamlReader.Parse(xml.ToString());
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            foreach (XElement element in xml.DescendantsAndSelf())
            {
                string name = (string)element.Attribute(x + "Name");
                if (name != null) Window.RegisterName(name, root.Name == name ? root : root.FindName(name));
            }
            LoadIcons(root);
            return root;
        }
        private void InitializeWidgets()
        {
            Find<Button>("EditWidgetsButton").Click += delegate { SetWidgetEditing(!editingWidgets); };
            foreach (IIslandWidget widget in widgets)
            {
                widget.View = LoadWidgetView(widget.Id, "View.xaml");
                widget.SettingsView = LoadWidgetView(widget.Id, "Settings.xaml");
                string id = widget.Id;
                Grid header = new Grid();
                header.ColumnDefinitions.Add(new ColumnDefinition());
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                TextBlock title = new TextBlock { Text = widget.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
                header.Children.Add(title);
                CheckBox enabled = new CheckBox { IsChecked = widgetConfiguration.Get(id).Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Показывать «" + widget.Title + "»" };
                System.Windows.Automation.AutomationProperties.SetName(enabled, "Включить виджет «" + widget.Title + "»");
                enabled.Click += delegate { SetWidgetEnabled(id, enabled.IsChecked == true); };
                Grid.SetColumn(enabled, 1); header.Children.Add(enabled); widgetSwitches[id] = enabled;
                StackPanel body = new StackPanel(); body.Children.Add(header);
                widget.SettingsView.Margin = new Thickness(0, 8, 0, 0); body.Children.Add(widget.SettingsView);
                Border row = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12), Child = body };
                row.SetResourceReference(Border.BackgroundProperty, "Card"); widgetRows[id] = row;
            }
            LayoutWidgets();
        }
        private void LayoutWidgets()
        {
            StackPanel overview = Find<StackPanel>("Overview");
            // Detach compact views from their old row before creating the new ordering.
            foreach (IIslandWidget widget in widgets)
            {
                Panel parent = widget.View.Parent as Panel;
                if (parent != null) parent.Children.Remove(widget.View);
                widget.View.Margin = new Thickness(0);
                Grid.SetColumn(widget.View, 0);
            }
            overview.Children.Clear();
            List<IIslandWidget> visible = new List<IIslandWidget>();
            foreach (WidgetState state in widgetConfiguration.Items)
            {
                IIslandWidget widget = widgets.Find(delegate(IIslandWidget item) { return item.Id == state.Id; });
                widget.View.Visibility = state.Enabled ? Visibility.Visible : Visibility.Collapsed;
                if (state.Enabled || editingWidgets) visible.Add(widget);
                if (widgetLifecycleReady) widget.SetEnabled(state.Enabled, ChangeWidgetLifecycle);
                widgetSwitches[state.Id].IsChecked = state.Enabled;
            }
            inlineSwitches.Clear(); inlineUp.Clear(); inlineDown.Clear();
            Find<TextBlock>("TimeWidgetHeading").Visibility = editingWidgets ? Visibility.Collapsed : Visibility.Visible;
            if (editingWidgets)
            {
                TextBlock hint = new TextBlock { Text = "Редактирование виджетов · нажмите карандаш, чтобы закончить", TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 12) };
                hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); overview.Children.Add(hint);
            }
            for (int index = 0; index < visible.Count; index++)
            {
                IIslandWidget current = visible[index];
                if (editingWidgets)
                {
                    StackPanel body = new StackPanel();
                    body.Children.Add(InlineWidgetHeader(current));
                    if (WidgetEnabled(current.Id)) { current.View.Margin = new Thickness(0, 10, 0, 0); body.Children.Add(current.View); }
                    Border card = new Border { Child = body, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 12) };
                    card.SetResourceReference(Border.BorderBrushProperty, "Line"); overview.Children.Add(card);
                }
                else if (current.Compact && index + 1 < visible.Count && visible[index + 1].Compact)
                {
                    Grid pair = new Grid { Margin = new Thickness(0, index == 0 ? 0 : 14, 0, 0) };
                    pair.ColumnDefinitions.Add(new ColumnDefinition()); pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); pair.ColumnDefinitions.Add(new ColumnDefinition());
                    pair.Children.Add(current.View); IIslandWidget next = visible[++index]; Grid.SetColumn(next.View, 2); pair.Children.Add(next.View); overview.Children.Add(pair);
                }
                else { current.View.Margin = new Thickness(0, index == 0 ? 0 : 14, 0, 0); overview.Children.Add(current.View); }
            }
            if (visible.Count == 0)
            {
                TextBlock empty = new TextBlock { Text = "Все виджеты выключены. Включите нужные в настройках.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 24, 12, 24) };
                empty.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); overview.Children.Add(empty);
            }
            Find<Border>("QuotaBadgeContainer").Visibility = WidgetEnabled("codex") ? Visibility.Visible : Visibility.Collapsed;
            StackPanel settings = Find<StackPanel>("WidgetSettingsHost"); settings.Children.Clear();
            TextBlock heading = new TextBlock { Text = "Виджеты", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) }; settings.Children.Add(heading);
            TextBlock help = new TextBlock { Text = "Порядок меняется через карандаш на главном экране. Здесь — включение и параметры виджетов.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 12) }; help.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); settings.Children.Add(help);
            for (int index = 0; index < widgetConfiguration.Items.Count; index++)
            {
                string id = widgetConfiguration.Items[index].Id;
                settings.Children.Add(widgetRows[id]);
            }
        }
        private void SetWidgetEnabled(string id, bool enabled)
        {
            widgetConfiguration.Get(id).Enabled = enabled; LayoutWidgets(); Save();
        }
        private void MoveWidget(string id, int direction)
        {
            if (!widgetConfiguration.Move(id, direction)) return;
            LayoutWidgets(); Save();
            if (editingWidgets) inlineSwitches[id].BringIntoView(); else widgetRows[id].BringIntoView();
        }
        private void SetWidgetEditing(bool value)
        {
            if (editingFocus) CommitFocusEdit();
            if (mediaAdjusting) CancelMediaAdjustment();
            editingWidgets = value;
            if (value) { if (musicOpen) CloseMusic(); ShowSettings(false); }
            Find<Button>("EditWidgetsButton").SetResourceReference(Control.BackgroundProperty, value ? "AccentSoft" : "Surface");
            Find<Button>("EditWidgetsButton").ToolTip = value ? "Закончить редактирование" : "Редактировать виджеты";
            LayoutWidgets(); grace = DateTime.UtcNow.AddSeconds(1);
        }
        private Grid InlineWidgetHeader(IIslandWidget widget)
        {
            string id = widget.Id;
            Grid row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 3; i++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = widget.Title, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) });
            CheckBox toggle = new CheckBox { IsChecked = WidgetEnabled(id), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Включить / выключить «" + widget.Title + "»" };
            System.Windows.Automation.AutomationProperties.SetName(toggle, (string)toggle.ToolTip);
            toggle.Click += delegate { SetWidgetEnabled(id, toggle.IsChecked == true); inlineSwitches[id].BringIntoView(); }; Grid.SetColumn(toggle, 1); row.Children.Add(toggle); inlineSwitches[id] = toggle;
            Button up = InlineArrow("arrow-up", "Поднять «" + widget.Title + "»"), down = InlineArrow("arrow-down", "Опустить «" + widget.Title + "»");
            int index = widgetConfiguration.Items.FindIndex(delegate(WidgetState entry) { return entry.Id == id; });
            up.IsEnabled = index > 0; down.IsEnabled = index < widgets.Count - 1;
            up.Click += delegate { MoveWidget(id, -1); }; down.Click += delegate { MoveWidget(id, 1); };
            Grid.SetColumn(up, 2); Grid.SetColumn(down, 3); row.Children.Add(up); row.Children.Add(down); inlineUp[id] = up; inlineDown[id] = down;
            return row;
        }
        private Button InlineArrow(string icon, string label)
        {
            ContentControl content = new ContentControl { Content = Icons.Load(icon), Width = 20, Height = 20 }; content.SetResourceReference(Control.ForegroundProperty, "Accent");
            Button button = new Button { Content = content, Width = 32, Height = 32, Padding = new Thickness(6), ToolTip = label };
            System.Windows.Automation.AutomationProperties.SetName(button, label); return button;
        }
        private void ChangeWidgetLifecycle(string id, bool enabled)
        {
            if (id == "time" && !enabled)
            {
                if (editingFocus) CancelFocusEdit();
                if (focus.Running) focus.Toggle(DateTime.UtcNow);
                timerCue.Stop(); UpdateClock();
            }
            if (id == "time" && !enabled) { if (counter.Running) counter.Toggle(DateTime.UtcNow); UpdateStopwatch(); }
            if (id == "music")
            {
                if (enabled && !smoke && Window.IsLoaded) { mediaPolling.Start(); RefreshMedia(); }
                else { mediaPolling.Stop(); if (mediaAdjusting) CancelMediaAdjustment(); if (musicSources != null) musicSources.IsOpen = false; }
            }
            if (id == "codex")
            {
                if (enabled && !smoke && Window.IsLoaded) { limitsPolling.Start(); RefreshLimits(); }
                else limitsPolling.Stop();
            }
        }
        private void StartWidgets()
        {
            widgetLifecycleReady = true; LayoutWidgets();
        }
    }
}
