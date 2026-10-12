using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Xml.Linq;

namespace DynamicIsland
{
    internal sealed partial class Island
    {
        private void CheckQuickLinks(string directory, List<string> results)
        {
            int count = preferences.QuickApps.Count;
            TextBox input = Find<TextBox>("QuickLinkAddress");
            input.Text = " example.com/Docs?a=1&b=2#part ";
            Find<Button>("AddQuickLink").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            QuickApp link = preferences.QuickApps[count];
            Assert(preferences.QuickApps.Count == count + 1 && input.Text == "" && link.Target == "https://example.com/Docs?a=1&b=2#part" && link.Icon == "home", "Pasted website addresses get HTTPS, an Iconly icon, and clear the draft", results);
            Assert(link.SameTarget("https://EXAMPLE.COM/Docs?a=1&b=2#part") && !link.SameTarget("https://example.com/docs?a=1&b=2#part"), "Link duplicates ignore host case but preserve case-sensitive URL paths", results);
            input.Text = link.Target; AddQuickLink();
            Assert(preferences.QuickApps.Count == count + 1 && Find<TextBlock>("QuickAppStatus").Text.Contains("уже"), "Duplicate links are explained without adding another card", results);
            string normalized;
            Assert(!QuickApp.TryLink("javascript:alert(1)", out normalized) && !QuickApp.TryLink("file:///C:/test.exe", out normalized) && !QuickApp.TryLink("ms-settings:", out normalized) && !QuickApp.TryLink("notepad.exe", out normalized) && !QuickApp.TryLink("https://one.example\nhttps://two.example", out normalized), "Website input rejects commands, local files and multiple pasted addresses", results);
            foreach (QuickApp app in QuickApp.Defaults()) Assert(QuickApp.FromXml(app.ToXml()).Target == app.Target, "Default quick action remains a program: " + app.Name, results);
            QuickAccessWidget module = new QuickAccessWidget(); Preferences restored = new Preferences();
            module.ReadSettings(XElement.Parse(module.WriteSettings(preferences).ToString()), restored);
            Assert(restored.QuickApps.Count == count + 1 && restored.QuickApps[count].Target == link.Target && restored.QuickApps[count].Icon == "home", "Links survive module settings XML with query parameters and fragment", results);
            input.Text = "https://example.org/";
            input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(Window), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            Assert(preferences.QuickApps.Count == count + 2 && input.Text == "", "Enter adds the pasted link", results);
            Grid row = (Grid)Find<StackPanel>("QuickAppEditor").Children[count];
            TextBox name = (TextBox)row.Children[0]; name.Text = "Документация";
            name.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, name, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            Assert(link.Name == "Документация", "Links can be renamed in the existing editor", results);
            input.Focus(); grace = DateTime.MinValue; preferences.Delay = 0; HandleHover(false);
            Assert(expanded, "Editing a link keeps the island open", results);
            input.Text = "https://draft.example/";
            input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(Window), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert(input.Text == "" && expanded, "Escape clears a link draft without collapsing the island", results);
            input.BringIntoView(); Window.UpdateLayout(); ChangeTheme(false); Snapshot(Path.Combine(directory, "island-quick-links-light.png"));
            ChangeTheme(true); Snapshot(Path.Combine(directory, "island-quick-links-dark.png")); ChangeTheme(false);
            ((Button)row.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(preferences.QuickApps.Count == count + 1 && !preferences.QuickApps.Contains(link), "Removing a link updates quick access", results);
            ShowSettings(false); Window.UpdateLayout(); Find<UniformGrid>("QuickAppsGrid").BringIntoView(); Window.UpdateLayout();
            Snapshot(Path.Combine(directory, "island-top-grip-links.png"));
        }
    }
}
