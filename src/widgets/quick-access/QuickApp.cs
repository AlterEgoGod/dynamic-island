using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
namespace DynamicIsland
{
    internal sealed class QuickApp
    {
        public string Name, Target, Icon;
        public QuickApp(string name, string target, string icon) { Name = name; Target = target; Icon = icon; }
        public bool SameTarget(string target)
        {
            string left, right;
            if (TryLink(Target, out left) && TryLink(target, out right))
            {
                Uri a = new Uri(left), b = new Uri(right);
                return string.Equals(a.GetLeftPart(UriPartial.Authority), b.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a.PathAndQuery + a.Fragment, b.PathAndQuery + b.Fragment, StringComparison.Ordinal);
            }
            return string.Equals(Target, target, StringComparison.OrdinalIgnoreCase);
        }
        public static bool TryLink(string input, out string target)
        {
            target = null;
            if (string.IsNullOrWhiteSpace(input)) return false;
            string text = input.Trim();
            if (IsProgram(text)) return false;
            foreach (char c in text) if (char.IsControl(c) || c == '\\') return false;
            bool explicitScheme = text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
            if (!explicitScheme)
            {
                if (text.Contains("://") || (!text.Contains(".") && !text.StartsWith("localhost", StringComparison.OrdinalIgnoreCase))) return false;
                text = "https://" + text;
            }
            Uri uri;
            if (!Uri.TryCreate(text, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || string.IsNullOrEmpty(uri.Host) || uri.HostNameType == UriHostNameType.Unknown || uri.UserInfo.Length > 0) return false;
            target = uri.AbsoluteUri; return true;
        }
        public static List<QuickApp> Defaults()
        {
            return new List<QuickApp> { new QuickApp("Файлы", "explorer.exe", "folder"), new QuickApp("Заметки", "notepad.exe", "document"), new QuickApp("Калькулятор", "calc.exe", "calculator"), new QuickApp("Система", "ms-settings:", "setting") };
        }
        public static bool IsProgram(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return false;
            foreach (QuickApp app in Defaults()) if (string.Equals(app.Target, target, StringComparison.OrdinalIgnoreCase)) return true;
            try
            {
                string ext = Path.GetExtension(target).ToLowerInvariant();
                return Path.IsPathRooted(target) && (ext == ".exe" || ext == ".lnk" || ext == ".appref-ms");
            }
            catch (ArgumentException) { return false; }
        }
        public static QuickApp FromXml(XElement item)
        {
            string target = (string)item.Attribute("target"), name = (string)item.Attribute("name");
            string link = null; Uri saved;
            bool isLink = Uri.TryCreate(target, UriKind.Absolute, out saved)
                && (saved.Scheme == Uri.UriSchemeHttp || saved.Scheme == Uri.UriSchemeHttps) && TryLink(target, out link);
            if ((!IsProgram(target) && !isLink) || string.IsNullOrWhiteSpace(name)) return null;
            if (isLink) target = link;
            string icon = "brand";
            foreach (QuickApp app in Defaults()) if (app.SameTarget(target)) icon = app.Icon;
            return new QuickApp(name.Length > 60 ? name.Substring(0, 60) : name, target, isLink ? "home" : icon);
        }
        public XElement ToXml() { return new XElement("App", new XAttribute("name", Name), new XAttribute("target", Target)); }
    }

}
