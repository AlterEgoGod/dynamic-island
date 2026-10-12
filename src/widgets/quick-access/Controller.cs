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
        private void BuildQuickApps()
        {
            UniformGrid quick = Find<UniformGrid>("QuickAppsGrid");
            StackPanel editor = Find<StackPanel>("QuickAppEditor");
            quick.Children.Clear(); editor.Children.Clear();
            Find<TextBlock>("QuickAppsEmpty").Visibility = preferences.QuickApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (QuickApp item in preferences.QuickApps)
            {
                QuickApp entry = item;
                StackPanel face = new StackPanel();
                ContentControl icon = new ContentControl { Content = Icons.Load(entry.Icon), Width = 25, Height = 25, Margin = new Thickness(0, 0, 0, 8) };
                icon.SetResourceReference(Control.ForegroundProperty, "Accent"); face.Children.Add(icon);
                TextBlock label = new TextBlock { Text = entry.Name, FontSize = 11, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                face.Children.Add(label);
                Button launch = new Button { Content = face, Style = (Style)Window.Resources["CardButton"], Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(5), ToolTip = entry.Name + "\n" + entry.Target };
                System.Windows.Automation.AutomationProperties.SetName(launch, "Открыть " + entry.Name);
                launch.Click += delegate
                {
                    if (smoke) return;
                    string link;
                    if (!QuickApp.TryLink(entry.Target, out link) && Path.IsPathRooted(entry.Target) && !File.Exists(entry.Target)) { Find<TextBlock>("Footer").Text = "Программа не найдена. Добавьте её заново в настройках."; return; }
                    Launch(entry.Target);
                };
                quick.Children.Add(launch);
                Grid row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                TextBox name = new TextBox { Text = entry.Name, MaxLength = 60, ToolTip = entry.Target, Margin = new Thickness(0, 0, 6, 0) };
                name.SetResourceReference(Control.BackgroundProperty, "Surface");
                System.Windows.Automation.AutomationProperties.SetName(name, "Название элемента " + entry.Name);
                Action rename = delegate
                {
                    string text = name.Text.Trim();
                    if (text.Length == 0) { name.Text = entry.Name; return; }
                    entry.Name = text; label.Text = text; launch.ToolTip = text + "\n" + entry.Target;
                    System.Windows.Automation.AutomationProperties.SetName(launch, "Открыть " + text); Save();
                };
                name.LostKeyboardFocus += delegate { rename(); };
                name.KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs args) { if (args.Key == System.Windows.Input.Key.Enter) { rename(); Find<Button>("AddQuickApp").Focus(); args.Handled = true; } };
                row.Children.Add(name);
                Button remove = new Button { Width = 36, Height = 34, Padding = new Thickness(8), Content = new ContentControl { Content = Icons.Load("close"), Width = 18, Height = 18 }, ToolTip = "Убрать из быстрого доступа" };
                System.Windows.Automation.AutomationProperties.SetName(remove, "Убрать " + entry.Name);
                remove.Click += delegate { preferences.QuickApps.Remove(entry); Save(); BuildQuickApps(); Find<TextBlock>("QuickAppStatus").Text = "Элемент убран из быстрого доступа."; };
                Grid.SetColumn(remove, 1); row.Children.Add(remove); editor.Children.Add(row);
            }
        }
        private bool AddQuickApp(string path)
        {
            if (!QuickApp.IsProgram(path) || preferences.QuickApps.Exists(delegate(QuickApp app) { return app.SameTarget(path); })) return false;
            preferences.QuickApps.Add(new QuickApp(Path.GetFileNameWithoutExtension(path), path, "brand")); return true;
        }
        private void AddQuickLink()
        {
            TextBox input = Find<TextBox>("QuickLinkAddress"); string target;
            if (!QuickApp.TryLink(input.Text, out target)) { Find<TextBlock>("QuickAppStatus").Text = "Введите адрес сайта, например https://example.com."; input.Focus(); return; }
            if (preferences.QuickApps.Exists(delegate(QuickApp app) { return app.SameTarget(target); })) { Find<TextBlock>("QuickAppStatus").Text = "Эта ссылка уже добавлена."; input.Focus(); return; }
            string name = new Uri(target).DnsSafeHost;
            preferences.QuickApps.Add(new QuickApp(name.Length > 60 ? name.Substring(0, 60) : name, target, "home"));
            Save(); BuildQuickApps(); input.Clear(); input.Focus();
            Find<TextBlock>("QuickAppStatus").Text = "Ссылка добавлена. Название можно изменить выше.";
        }
        private void ChooseQuickApps()
        {
            if (smoke) return;
            featureDialogOpen = true;
            try
            {
                Microsoft.Win32.OpenFileDialog picker = new Microsoft.Win32.OpenFileDialog { Title = "Добавить программы в быстрый доступ", Filter = "Программы и ярлыки|*.exe;*.lnk;*.appref-ms", Multiselect = true, CheckFileExists = true, DereferenceLinks = false };
                if (picker.ShowDialog(Window) != true) return;
                int count = 0; foreach (string path in picker.FileNames) if (AddQuickApp(path)) count++;
                Save(); BuildQuickApps(); Find<TextBlock>("QuickAppStatus").Text = count == 0 ? "Эти программы уже добавлены." : "Добавлено: " + count + ". Названия можно изменить выше.";
            }
            finally { featureDialogOpen = false; grace = DateTime.UtcNow.AddSeconds(2); }
        }
    }
}
