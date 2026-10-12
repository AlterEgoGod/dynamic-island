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

namespace DynamicIsland
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            bool smoke = Array.IndexOf(args, "--smoke-test") >= 0;
            bool self = Array.IndexOf(args, "--self-test") >= 0;
            if (self) return Checks.Run();
            if (Array.IndexOf(args, "--music-test") >= 0) return MusicCheck.Run();
            if (Array.IndexOf(args, "--music-probe") >= 0) return MusicCheck.Run(true);
            if (Array.IndexOf(args, "--media-test") >= 0) return DesktopMediaCheck.Run();
            if (Array.IndexOf(args, "--media-command-test") >= 0) return MediaCommandCheck.Run();
            if (Array.IndexOf(args, "--limits-test") >= 0) return Checks.LiveLimits();
            bool first;
            using (Mutex instance = new Mutex(true, "Local\\DynamicIsland.Desktop.1", out first))
            {
                if (!first && !smoke) return 0;
                try
                {
                    Application app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    Island island = new Island(app, smoke);
                    app.Run(island.Window);
                    return island.ExitCode;
                }
                catch (Exception error)
                {
                    string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
                    File.WriteAllText(log, error.ToString());
                    if (!smoke) MessageBox.Show("Не удалось запустить остров. Подробности: " + log, "Dynamic Island");
                    return 1;
                }
                finally { if (first) instance.ReleaseMutex(); }
            }
        }
    }

    internal enum Edge { Top, Bottom, Left, Right }

    internal sealed class Preferences
    {
        public Edge Edge = Edge.Top;
        public string Monitor = "";
        public bool Dark;
        public bool Pinned;
        public int Delay = 200;
        public long FocusSeconds;
        public int FocusMinutes { get { return (int)(FocusSeconds / 60); } set { FocusSeconds = (long)value * 60; } }
        public string CodexPath = "";
        public double EdgePosition = 0.5;
        public bool CenterOnEdge = true;
        public List<int> Presets = new List<int> { 5, 10, 15, 30, 60, 90 };
        public List<QuickApp> QuickApps = QuickApp.Defaults();
        public string MusicService = "yandex";
        public string SaveError;
        public static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "settings.xml"); } }

        public static Preferences Load(string path = null)
        {
            Preferences value = new Preferences();
            path = path ?? FilePath;
            try
            {
                if (!File.Exists(path)) return value;
                XElement root = XElement.Load(path);
                Edge edge;
                if (Enum.TryParse((string)root.Element("Edge"), out edge) && Enum.IsDefined(typeof(Edge), edge)) value.Edge = edge;
                value.Monitor = (string)root.Element("Monitor") ?? "";
                bool flag;
                if (bool.TryParse((string)root.Element("Dark"), out flag)) value.Dark = flag;
                if (bool.TryParse((string)root.Element("Pinned"), out flag)) value.Pinned = flag;
                if (bool.TryParse((string)root.Element("CenterOnEdge"), out flag)) value.CenterOnEdge = flag;
                int delay;
                if (int.TryParse((string)root.Element("Delay"), out delay)) value.Delay = Math.Max(0, Math.Min(1500, delay));
                if (int.TryParse((string)root.Element("FocusMinutes"), out delay) && delay >= 0) value.FocusMinutes = delay;
                long seconds;
                if (long.TryParse((string)root.Element("FocusSeconds"), out seconds) && seconds >= 0 && seconds <= FocusTimeInput.MaxSeconds) value.FocusSeconds = seconds;
                int version;
                if (!int.TryParse((string)root.Element("DefaultsVersion"), out version) || version < 2)
                {
                    if (value.FocusSeconds == 25 * 60) value.FocusSeconds = 0;
                    if (value.Delay == 500) value.Delay = 200;
                    value.CenterOnEdge = true;
                }
                value.CodexPath = (string)root.Element("CodexPath") ?? "";
                value.MusicService = MusicServices.Normalize((string)root.Element("MusicService"));
                XElement apps = root.Element("QuickApps");
                if (apps != null)
                {
                    value.QuickApps.Clear();
                    foreach (XElement item in apps.Elements("App"))
                    {
                        QuickApp entry = QuickApp.FromXml(item);
                        if (entry != null && !value.QuickApps.Exists(delegate(QuickApp a) { return a.SameTarget(entry.Target); })) value.QuickApps.Add(entry);
                    }
                }
                double position;
                if (double.TryParse((string)root.Element("EdgePosition"), NumberStyles.Float, CultureInfo.InvariantCulture, out position) && !double.IsNaN(position) && !double.IsInfinity(position)) value.EdgePosition = Math.Max(0, Math.Min(1, position));
                if (value.CenterOnEdge) value.EdgePosition = 0.5;
                XElement presets = root.Element("Presets");
                if (presets != null)
                {
                    value.Presets.Clear();
                    foreach (XElement item in presets.Elements("Minutes"))
                        if (int.TryParse(item.Value, out delay) && delay >= 1 && !value.Presets.Contains(delay)) value.Presets.Add(delay);
                    value.Presets.Sort();
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (XmlException) { }
            return value;
        }

        public void Save(string path = null)
        {
            path = path ?? FilePath;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temporary = path + ".tmp";
                XElement presets = new XElement("Presets"); foreach (int minutes in Presets) presets.Add(new XElement("Minutes", minutes));
                XElement apps = new XElement("QuickApps"); foreach (QuickApp entry in QuickApps) apps.Add(entry.ToXml());
                new XElement("Island", new XElement("DefaultsVersion", 2), new XElement("Edge", Edge), new XElement("Monitor", Monitor), new XElement("Dark", Dark), new XElement("Pinned", Pinned), new XElement("Delay", Delay), new XElement("FocusMinutes", FocusMinutes), new XElement("FocusSeconds", FocusSeconds), new XElement("CodexPath", CodexPath), new XElement("EdgePosition", EdgePosition.ToString(CultureInfo.InvariantCulture)), new XElement("CenterOnEdge", CenterOnEdge), new XElement("MusicService", MusicService), presets, apps).Save(temporary);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                SaveError = null;
            }
            catch (IOException) { SaveError = "Не удалось сохранить настройки"; }
            catch (UnauthorizedAccessException) { SaveError = "Нет доступа к сохранению настроек"; }
        }
    }

    internal static class Placement
    {
        public const int ExpandedWidth = 480, ExpandedHeight = 680;
        // Screen coordinates are physical pixels; all widget dimensions are device-independent.
        public static Drawing.Rectangle Get(Drawing.Rectangle area, Edge edge, bool expanded, double scale, double position = 0.5, int expandedWidth = ExpandedWidth, int expandedHeight = ExpandedHeight)
        {
            int width = Math.Min(area.Width, (int)Math.Round((expanded ? expandedWidth : (edge == Edge.Left || edge == Edge.Right ? 8 : 132)) * scale));
            int height = Math.Min(area.Height, (int)Math.Round((expanded ? expandedHeight : (edge == Edge.Left || edge == Edge.Right ? 132 : 8)) * scale));
            width = Math.Max(1, width); height = Math.Max(1, height);
            int x = Math.Max(area.Left, Math.Min(area.Right - width, area.Left + (int)Math.Round(position * area.Width) - width / 2));
            int y = Math.Max(area.Top, Math.Min(area.Bottom - height, area.Top + (int)Math.Round(position * area.Height) - height / 2));
            if (edge == Edge.Top) y = area.Top;
            if (edge == Edge.Bottom) y = area.Bottom - height;
            if (edge == Edge.Left) x = area.Left;
            if (edge == Edge.Right) x = area.Right - width;
            return new Drawing.Rectangle(x, y, width, height);
        }
        public static Edge NearestEdge(Drawing.Rectangle area, Drawing.Point point)
        {
            int best = Math.Abs(point.Y - area.Top); Edge edge = Edge.Top;
            int distance = Math.Abs(point.Y - area.Bottom); if (distance < best) { best = distance; edge = Edge.Bottom; }
            distance = Math.Abs(point.X - area.Left); if (distance < best) { best = distance; edge = Edge.Left; }
            distance = Math.Abs(point.X - area.Right); if (distance < best) edge = Edge.Right;
            return edge;
        }
        public static double Position(Drawing.Rectangle area, Edge edge, Drawing.Rectangle widget)
        {
            double value = edge == Edge.Top || edge == Edge.Bottom ? (widget.Left + widget.Width / 2.0 - area.Left) / area.Width : (widget.Top + widget.Height / 2.0 - area.Top) / area.Height;
            return Math.Max(0, Math.Min(1, value));
        }
    }

    internal sealed partial class Island
    {
        public Window Window { get; private set; }
        public int ExitCode { get; private set; }
        private readonly Application app;
        private readonly bool smoke;
        private Preferences preferences;
        private readonly FocusTimer focus = new FocusTimer();
        private readonly CounterStopwatch counter = new CounterStopwatch();
        private readonly DispatcherTimer counterTicks = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly TimerCue timerCue = new TimerCue();
        private readonly List<Button> presetButtons = new List<Button>();
        private bool focusInputInvalid;
        private bool editingFocus, loginLaunching;
        private string originalFocusText = "", loginUrl = "";
        private readonly DispatcherTimer polling = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        private readonly DispatcherTimer clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer limitsPolling = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        private LimitsClient limitsClient = new LimitsClient();
        private LimitsSnapshot limits;
        private bool limitsBusy;
        private string limitsError = "";
        private readonly TranslateTransform contentOffset = new TranslateTransform();
        private int morphGeneration;
        private bool suppressHover, dragCandidate, dragging;
        private Drawing.Point dragStartPoint;
        private Drawing.Rectangle dragOrigin;
        private readonly Stopwatch leaveClock = new Stopwatch();
        private Forms.NotifyIcon tray;
        private Drawing.Icon trayIcon;
        private Forms.ContextMenuStrip trayMenu;
        private IntPtr handle;
        private bool expanded, animating, loading = true, settingsOpen, disposed;
        private DateTime grace = DateTime.MinValue;
        private HwndSource source;
        private bool hotkey, timerShowingPause, counterShowingPause;
        private Border panel, bar;
        private Grid content;

        public Island(Application app, bool smoke)
        {
            this.app = app; this.smoke = smoke;
            widgetConfiguration = new WidgetConfiguration(widgets);
            preferences = smoke ? new Preferences() : WidgetSettingsStore.Load(Preferences.FilePath, widgets, out widgetConfiguration);
            focus.SetSeconds(preferences.FocusSeconds);
            limitsClient.ConfiguredPath = preferences.CodexPath;
            limitsClient.LoginCompleted += OnLoginCompleted;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Panel.xaml")) Window = (Window)XamlReader.Load(stream);
            panel = Find<Border>("PanelBorder"); bar = Find<Border>("CollapsedBar"); content = Find<Grid>("PanelContent");
            panel.SizeChanged += delegate { panel.Clip = new RectangleGeometry(new Rect(0, 0, panel.ActualWidth, panel.ActualHeight)); };
            content.RenderTransform = contentOffset;
            content.Opacity = 0;
            InitializeWidgets();
            LoadIcons(Window);
            WireControls();
            BuildPresets();
            BuildQuickApps(); WireFeatures();
            Find<CheckBox>("DarkMode").IsChecked = preferences.Dark;
            Find<CheckBox>("CenterOnEdge").IsChecked = preferences.CenterOnEdge;
            Find<Slider>("HideDelay").Value = preferences.Delay;
            ApplyAppearance();
            loading = false;
            Window.SourceInitialized += OnSourceInitialized;
            Window.MouseEnter += delegate { Poll(null, EventArgs.Empty); };
            Window.PreviewMouseLeftButtonDown += DragMouseDown;
            Window.PreviewMouseMove += DragMouseMove;
            Window.PreviewMouseLeftButtonUp += DragMouseUp;
            Window.LostMouseCapture += delegate { if (dragging || dragCandidate) EndDrag(dragStartPoint, true); };
            Window.Closed += delegate { Dispose(); };
            Window.PreviewKeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e)
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    if (Find<TextBox>("QuickLinkAddress").IsKeyboardFocusWithin && Find<TextBox>("QuickLinkAddress").Text.Length > 0) Find<TextBox>("QuickLinkAddress").Clear();
                    else if (editingWidgets) SetWidgetEditing(false);
                    else if (mediaAdjusting) CancelMediaAdjustment();
                    else if (editingFocus) { CancelFocusEdit(); Find<Button>("TimerButton").Focus(); }
                    else if (dragging || dragCandidate) EndDrag(dragStartPoint, true); else CollapseByUser();
                    e.Handled = true;
                }
            };
            Window.ContextMenu = MakeContextMenu();
            polling.Tick += Poll;
            clock.Tick += delegate { UpdateClock(); };
            counterTicks.Tick += delegate { UpdateStopwatch(); };
            limitsPolling.Tick += delegate { RefreshLimits(); };
            Window.Loaded += delegate
            {
                UpdateClock(); UpdateStopwatch(); SetExpanded(false, true);
                if (preferences.Pinned) SetExpanded(true, true);
                polling.Start(); clock.Start();
                if (smoke) BeginSmokeTest();
                StartWidgets();
                if (!smoke) Save();
            };
        }

        private T Find<T>(string name) where T : FrameworkElement { return (T)Window.FindName(name); }

        private void OnSourceInitialized(object sender, EventArgs args)
        {
            handle = new WindowInteropHelper(Window).Handle;
            source = HwndSource.FromHwnd(handle); source.AddHook(WindowMessage);
            int style = Native.GetWindowLong(handle, -20);
            Native.SetWindowLong(handle, -20, style | 0x80); // WS_EX_TOOLWINDOW: no Alt-Tab entry.
            hotkey = !smoke && Native.RegisterHotKey(handle, 1, 0x4003, 0x20);
            if (!hotkey && !smoke) Find<TextBlock>("ShortcutLabel").Text = "Через значок в трее";
            if (!smoke) CreateTray();
        }

        private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x312 && wParam.ToInt32() == 1)
            {
                if (expanded) { CollapseByUser(); }
                else Reveal(false);
                handled = true;
            }
            if (message == 0x7E || message == 0x2E0 || (message == 0x1A && wParam.ToInt32() == 0x2F))
                Window.Dispatcher.BeginInvoke(new Action(delegate { if (!dragging && !dragCandidate) Snap(); }), DispatcherPriority.Background);
            if (message == 0x84 && !dragging && !dragCandidate) // Fully transparent parts of the stable canvas never capture the desktop.
            {
                long packed = lParam.ToInt64();
                if (!VisibleBounds().Contains((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff))) { handled = true; return new IntPtr(-1); }
            }
            return IntPtr.Zero;
        }

        private Forms.Screen Monitor()
        {
            foreach (Forms.Screen screen in Forms.Screen.AllScreens) if (screen.DeviceName == preferences.Monitor) return screen;
            return Forms.Screen.PrimaryScreen;
        }
        private Drawing.Rectangle Area() { return Monitor().WorkingArea; }
        private Drawing.Rectangle LayoutBounds(bool open)
        {
            bool wide = musicOpen && musicWide;
            return Placement.Get(Area(), preferences.Edge, open, Scale(), preferences.EdgePosition, wide ? 960 : Placement.ExpandedWidth, wide ? 780 : Placement.ExpandedHeight);
        }
        private double Scale()
        {
            uint dpi;
            try { dpi = Native.GetDpiForWindow(handle); } catch (EntryPointNotFoundException) { dpi = 96; }
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }
        private void ApplyBounds(Drawing.Rectangle rect) { Native.SetWindowPos(handle, new IntPtr(-1), rect.X, rect.Y, rect.Width, rect.Height, 0x10 | 0x40); }
        private Drawing.Rectangle CurrentBounds()
        {
            Native.RECT rect; Native.GetWindowRect(handle, out rect);
            return new Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        private void Poll(object sender, EventArgs args)
        {
            // Smoke checks supply their own pointer coordinates; real MouseEnter events must not interfere.
            if (smoke || disposed || dragging || dragCandidate) return;
            Native.POINT cursor;
            if (Native.GetCursorPos(out cursor)) HandlePointer(cursor.X, cursor.Y);
            else HandleHover(Window.IsMouseOver);
        }
        private void HandlePointer(int x, int y)
        {
            bool inside = animating && !expanded ? LayoutBounds(false).Contains(x, y) : VisibleBounds().Contains(x, y);
            HandleHover(inside);
        }
        private void HandleHover(bool inside)
        {
            if (dragging || dragCandidate) return;
            if (suppressHover) { if (!inside) suppressHover = false; leaveClock.Reset(); return; }
            if (animating && expanded) { leaveClock.Reset(); return; }
            if (inside)
            {
                leaveClock.Reset();
                if (!expanded) SetExpanded(true, false);
            }
            else if (expanded && !preferences.Pinned && !editingWidgets && !editingFocus && !featureDialogOpen && !mediaAdjusting && !(music != null && music.HoldsOpen) && !(Find<StackPanel>("QuickAppEditor").IsKeyboardFocusWithin) && !Find<TextBox>("QuickLinkAddress").IsKeyboardFocusWithin && !(Find<TextBox>("NewPresetMinutes").IsKeyboardFocusWithin && Find<TextBox>("NewPresetMinutes").Text.Length > 0) && DateTime.UtcNow >= grace &&
                     !Window.ContextMenu.IsOpen && (musicSources == null || !musicSources.IsOpen) && (trayMenu == null || !trayMenu.Visible))
            {
                if (!leaveClock.IsRunning) leaveClock.Start();
                if (leaveClock.ElapsedMilliseconds >= preferences.Delay) SetExpanded(false, false);
            }
            else leaveClock.Reset();
        }

        private void SetExpanded(bool value, bool immediately)
        {
            if (handle == IntPtr.Zero) return;
            if (!value && editingFocus) CancelFocusEdit();
            if (!value && mediaAdjusting) CancelMediaAdjustment();
            Drawing.Rectangle previous = VisibleBounds();
            StopMorph(); expanded = value; leaveClock.Reset();
            animating = !immediately && SystemParameters.ClientAreaAnimation;
            double scale = Scale();
            Drawing.Rectangle full = LayoutBounds(true);
            Drawing.Rectangle target = LayoutBounds(value);
            content.Width = Math.Max(1, full.Width / scale - 50);
            content.Height = Math.Max(110, full.Height / scale - 48);
            // Both endpoints live in one native canvas, including the collapsed strip. Never relocate the window on hover.
            panel.Width = previous.Width / scale; panel.Height = previous.Height / scale;
            Canvas.SetLeft(panel, (previous.Left - full.Left) / scale); Canvas.SetTop(panel, (previous.Top - full.Top) / scale);
            panel.Clip = new RectangleGeometry(new Rect(0, 0, panel.Width, panel.Height));
            panel.Visibility = Visibility.Visible; bar.Visibility = Visibility.Collapsed;
            if (CurrentBounds() != full) ApplyBounds(full);
            if (immediately || !SystemParameters.ClientAreaAnimation) { FinishAnimation(); return; }
            animating = true;
            int generation = morphGeneration;
            TimeSpan duration = TimeSpan.FromMilliseconds(value ? 420 : 300);
            DoubleAnimation width = Morph(panel.Width, target.Width / scale, duration);
            width.Completed += delegate { if (!disposed && generation == morphGeneration) FinishAnimation(); };
            panel.BeginAnimation(FrameworkElement.WidthProperty, width);
            panel.BeginAnimation(FrameworkElement.HeightProperty, Morph(panel.Height, target.Height / scale, duration));
            panel.BeginAnimation(Canvas.LeftProperty, Morph(Canvas.GetLeft(panel), (target.Left - full.Left) / scale, duration));
            panel.BeginAnimation(Canvas.TopProperty, Morph(Canvas.GetTop(panel), (target.Top - full.Top) / scale, duration));
            DoubleAnimation fade = Morph(content.Opacity, value ? 1 : 0, TimeSpan.FromMilliseconds(value ? 260 : 160));
            if (value && content.Opacity < 0.05) fade.BeginTime = TimeSpan.FromMilliseconds(140);
            content.BeginAnimation(UIElement.OpacityProperty, fade);
            contentOffset.BeginAnimation(TranslateTransform.YProperty, Morph(contentOffset.Y, 0, duration));
        }
        private static DoubleAnimation Morph(double first, double last, TimeSpan duration)
        {
            return new DoubleAnimation(first, last, duration) { EasingFunction = new SmoothEase { EasingMode = EasingMode.EaseIn } };
        }
        private Drawing.Rectangle VisibleBounds()
        {
            FrameworkElement visible = panel.Visibility == Visibility.Visible ? panel : bar;
            if (double.IsNaN(visible.Width) || double.IsNaN(visible.Height)) return LayoutBounds(expanded);
            Drawing.Rectangle root = CurrentBounds(); double scale = Scale();
            double x = Canvas.GetLeft(visible), y = Canvas.GetTop(visible);
            return new Drawing.Rectangle(root.Left + (int)Math.Round((double.IsNaN(x) ? 0 : x) * scale), root.Top + (int)Math.Round((double.IsNaN(y) ? 0 : y) * scale), Math.Max(1, (int)Math.Round(visible.Width * scale)), Math.Max(1, (int)Math.Round(visible.Height * scale)));
        }
        private void StopMorph()
        {
            morphGeneration++;
            foreach (DependencyProperty property in new[] { FrameworkElement.WidthProperty, FrameworkElement.HeightProperty, Canvas.LeftProperty, Canvas.TopProperty })
            {
                double current = (double)panel.GetValue(property); panel.BeginAnimation(property, null); panel.SetValue(property, current);
            }
            double opacity = content.Opacity; content.BeginAnimation(UIElement.OpacityProperty, null); content.Opacity = opacity;
            double offset = contentOffset.Y; contentOffset.BeginAnimation(TranslateTransform.YProperty, null); contentOffset.Y = offset;
            animating = false;
        }
        private void FinishAnimation()
        {
            StopMorph();
            double scale = Scale();
            Drawing.Rectangle full = LayoutBounds(true);
            Drawing.Rectangle target = LayoutBounds(expanded);
            panel.Width = target.Width / scale; panel.Height = target.Height / scale;
            Canvas.SetLeft(panel, (target.Left - full.Left) / scale); Canvas.SetTop(panel, (target.Top - full.Top) / scale);
            Drawing.Rectangle strip = LayoutBounds(false);
            bar.Width = strip.Width / scale; bar.Height = strip.Height / scale;
            Canvas.SetLeft(bar, (strip.Left - full.Left) / scale); Canvas.SetTop(bar, (strip.Top - full.Top) / scale);
            panel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            bar.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
            content.Opacity = expanded ? 1 : 0; contentOffset.Y = 0;
            if (CurrentBounds() != full) ApplyBounds(full);
        }
        private void Snap() { ApplyAppearance(); SetExpanded(expanded, true); }
        private void CollapseByUser() { suppressHover = true; SetPinned(false); SetExpanded(false, false); }

        private void Reveal(bool settings)
        {
            grace = DateTime.UtcNow.AddSeconds(3);
            suppressHover = false;
            ShowSettings(settings); SetExpanded(true, false);
        }
        private void ShowSettings(bool visible)
        {
            if (visible && editingWidgets) SetWidgetEditing(false);
            if (musicOpen) CloseMusic();
            settingsOpen = visible;
            Find<StackPanel>("Overview").Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            Find<StackPanel>("Settings").Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            Find<ScrollViewer>("OverviewScroll").Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            Find<ScrollViewer>("SettingsScroll").Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            Find<Grid>("MainFooter").Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            Find<Grid>("SettingsActions").Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            Find<Button>("SettingsButton").Background = (Brush)Window.Resources[visible ? "AccentSoft" : "Surface"];
        }

        private void WireControls()
        {
            Find<Button>("SettingsButton").Click += delegate { ShowSettings(!settingsOpen); };
            Find<Button>("HideButton").Click += delegate { CollapseByUser(); };
            Find<Button>("PinButton").Click += delegate { SetPinned(!preferences.Pinned); };
            Find<Button>("TimerButton").Click += delegate
            {
                if (focusInputInvalid) { Find<TextBox>("TimerLabel").Focus(); return; }
                if (editingFocus && !CommitFocusEdit()) return;
                if (focus.Duration == TimeSpan.Zero) { Find<TextBox>("TimerLabel").Focus(); return; }
                timerCue.Stop(); focus.Toggle(DateTime.UtcNow); Find<TextBlock>("Footer").Text = "Перемещайте остров за ручку сверху"; UpdateClock();
            };
            TextBox time = Find<TextBox>("TimerLabel");
            time.TextChanged += delegate { FitFocusReadout(); };
            time.SizeChanged += delegate { FitFocusReadout(); };
            time.GotKeyboardFocus += delegate { BeginFocusEdit(); time.SelectAll(); };
            time.PreviewMouseLeftButtonDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (!time.IsKeyboardFocusWithin) { time.Focus(); time.SelectAll(); e.Handled = true; } };
            time.KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == System.Windows.Input.Key.Enter) { if (CommitFocusEdit()) Find<Button>("TimerButton").Focus(); e.Handled = true; } };
            time.LostKeyboardFocus += delegate { if (editingFocus && !CommitFocusEdit()) { editingFocus = false; UpdateClock(); } };
            Find<Button>("AddPreset").Click += delegate { AddPreset(); };
            Find<TextBox>("NewPresetMinutes").KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == System.Windows.Input.Key.Enter) { AddPreset(); e.Handled = true; } };
            Find<Button>("StopwatchButton").Click += delegate { counter.Toggle(DateTime.UtcNow); UpdateStopwatch(); };
            Find<Button>("StopwatchReset").Click += delegate { counter.Reset(); UpdateStopwatch(); };
            Find<Button>("LimitsRefreshButton").Click += delegate { RefreshLimits(); };
            Find<Button>("LimitsLoginButton").Click += delegate { LoginCodex(); };
            Find<Button>("SettingsLoginButton").Click += delegate { LoginCodex(); };
            Find<Button>("ResetSettingsButton").Click += delegate { ResetSettings(); };
            Find<Button>("QuitButton").Click += delegate { Window.Close(); };
            Find<CheckBox>("DarkMode").Checked += delegate { ChangeTheme(true); };
            Find<CheckBox>("DarkMode").Unchecked += delegate { ChangeTheme(false); };
            Find<CheckBox>("CenterOnEdge").Checked += delegate { if (!loading) SetCenterOnEdge(true); };
            Find<CheckBox>("CenterOnEdge").Unchecked += delegate { if (!loading) SetCenterOnEdge(false); };
            Find<Slider>("HideDelay").ValueChanged += delegate
            {
                preferences.Delay = (int)Find<Slider>("HideDelay").Value;
                Find<TextBlock>("DelayLabel").Text = preferences.Delay == 0 ? "Сразу" : preferences.Delay + " мс";
                if (!loading) Save();
            };
        }

        private void Launch(string executable)
        {
            try { Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true }); }
            catch (Exception error)
            {
                if (!(error is System.ComponentModel.Win32Exception) && !(error is InvalidOperationException)) throw;
                Find<TextBlock>("Footer").Text = "Не удалось открыть приложение";
            }
        }

        private static bool InteractiveSource(DependencyObject node)
        {
            while (node != null)
            {
                if (node is Microsoft.Web.WebView2.Wpf.WebView2CompositionControl || node is System.Windows.Controls.Primitives.ButtonBase || node is System.Windows.Controls.Primitives.TextBoxBase || node is System.Windows.Controls.Primitives.Selector || node is System.Windows.Controls.Primitives.RangeBase) return true;
                if (node is Visual || node is System.Windows.Media.Media3D.Visual3D) node = VisualTreeHelper.GetParent(node);
                else node = LogicalTreeHelper.GetParent(node);
            }
            return false;
        }
        private Drawing.Point Pointer(System.Windows.Input.MouseEventArgs args)
        {
            Native.POINT point;
            if (Native.GetCursorPos(out point)) return new Drawing.Point(point.X, point.Y);
            Point screen = Window.PointToScreen(args.GetPosition(Window)); return new Drawing.Point((int)screen.X, (int)screen.Y);
        }
        private void DragMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs args)
        {
            if (!IsDragHandle(args.OriginalSource as DependencyObject)) return;
            dragCandidate = true; dragStartPoint = Pointer(args);
            Window.CaptureMouse(); args.Handled = true;
        }
        private bool IsDragHandle(DependencyObject node)
        {
            while (node != null)
            {
                if (node == Find<Border>("Grip") || node == Find<Border>("CollapsedBar")) return true;
                if (node is Visual || node is System.Windows.Media.Media3D.Visual3D) node = VisualTreeHelper.GetParent(node); else node = LogicalTreeHelper.GetParent(node);
            }
            return false;
        }
        private void DragMouseMove(object sender, System.Windows.Input.MouseEventArgs args)
        {
            if (!dragCandidate && !dragging) return;
            Drawing.Point point = Pointer(args);
            if (!dragging && (Math.Abs(point.X - dragStartPoint.X) >= SystemParameters.MinimumHorizontalDragDistance * Scale() || Math.Abs(point.Y - dragStartPoint.Y) >= SystemParameters.MinimumVerticalDragDistance * Scale())) BeginDrag(dragStartPoint);
            if (dragging) MoveDrag(point);
            args.Handled = true;
        }
        private void DragMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs args)
        {
            if (!dragCandidate && !dragging) return;
            EndDrag(Pointer(args), false); args.Handled = true;
        }
        private void BeginDrag(Drawing.Point point)
        {
            dragging = true; dragCandidate = false; suppressHover = false; leaveClock.Reset();
            FinishAnimation(); dragStartPoint = point; dragOrigin = CurrentBounds();
            panel.CornerRadius = new CornerRadius(26); bar.CornerRadius = new CornerRadius(5);
            Window.Cursor = System.Windows.Input.Cursors.SizeAll;
        }
        private void MoveDrag(Drawing.Point point)
        {
            if (!dragging) return;
            ApplyBounds(new Drawing.Rectangle(dragOrigin.Left + point.X - dragStartPoint.X, dragOrigin.Top + point.Y - dragStartPoint.Y, dragOrigin.Width, dragOrigin.Height));
            Edge edge = Placement.NearestEdge(Forms.Screen.FromPoint(point).WorkingArea, point);
            Find<TextBlock>("Footer").Text = "Отпустите — " + (edge == Edge.Top ? "сверху" : edge == Edge.Bottom ? "снизу" : edge == Edge.Left ? "слева" : "справа");
        }
        private void EndDrag(Drawing.Point point, bool cancel)
        {
            bool moved = dragging; dragging = false; dragCandidate = false;
            Window.ReleaseMouseCapture(); Window.ClearValue(FrameworkElement.CursorProperty);
            if (moved && !cancel)
            {
                Forms.Screen monitor = Forms.Screen.FromPoint(point);
                preferences.Monitor = monitor.DeviceName;
                preferences.Edge = Placement.NearestEdge(monitor.WorkingArea, point);
                preferences.EdgePosition = preferences.CenterOnEdge ? 0.5 : Placement.Position(monitor.WorkingArea, preferences.Edge, VisibleBounds());
                Save();
            }
            grace = DateTime.UtcNow.AddSeconds(2); leaveClock.Reset(); Snap();
            Find<TextBlock>("Footer").Text = "Перемещайте остров за ручку сверху.";
        }

        private void ResetSettings()
        {
            if (editingWidgets) SetWidgetEditing(false);
            if (dragging || dragCandidate) EndDrag(dragStartPoint, true);
            timerCue.Stop(); editingFocus = false; focusInputInvalid = false;
            preferences = new Preferences();
            widgetConfiguration = new WidgetConfiguration(widgets);
            focus.SetSeconds(preferences.FocusSeconds); counter.Reset();
            limitsClient.ConfiguredPath = "";
            loading = true;
            Find<CheckBox>("DarkMode").IsChecked = preferences.Dark;
            Find<CheckBox>("CenterOnEdge").IsChecked = preferences.CenterOnEdge;
            Find<Slider>("HideDelay").Value = preferences.Delay;
            Find<TextBox>("NewPresetMinutes").Clear();
            Find<TextBlock>("PresetValidation").Text = "Добавляйте свои интервалы, удаляйте ненужные.";
            Find<Button>("PinButton").ToolTip = "Оставить панель раскрытой";
            loading = false;
            BuildPresets(); BuildQuickApps(); Find<TextBox>("QuickLinkAddress").Clear(); ResetMusic(); UpdateClock(); UpdateStopwatch();
            suppressHover = false; leaveClock.Reset(); grace = DateTime.UtcNow.AddSeconds(3);
            LayoutWidgets(); Save(); Snap();
            Find<TextBlock>("Footer").Text = preferences.SaveError ?? "Все настройки виджета сброшены.";
            Find<TextBlock>("PresetValidation").Text = preferences.SaveError ?? "Настройки сброшены. Шаблоны восстановлены.";
        }
        private void Save()
        {
            if (smoke) return;
            WidgetSettingsStore.Save(Preferences.FilePath, preferences, widgets, widgetConfiguration);
            if (preferences.SaveError != null) Find<TextBlock>("Footer").Text = preferences.SaveError;
        }
        private void ChangeTheme(bool dark)
        {
            if (loading) return;
            preferences.Dark = dark; ApplyAppearance(); Save();
        }
        private void SetPinned(bool pinned)
        {
            preferences.Pinned = pinned;
            Find<Button>("PinButton").Background = (Brush)Window.Resources[pinned ? "AccentSoft" : "Surface"];
            Find<Button>("PinButton").ToolTip = pinned ? "Включить автоматическое скрытие" : "Оставить панель раскрытой";
            if (pinned && !expanded) SetExpanded(true, false);
            Save();
        }

        private void SetCenterOnEdge(bool centered)
        {
            preferences.CenterOnEdge = centered;
            if (centered) preferences.EdgePosition = 0.5;
            Save(); grace = DateTime.UtcNow.AddSeconds(2); Snap();
        }

        private void ApplyAppearance()
        {
            ThemePalette.Apply(Window.Resources, preferences.Dark);
            if (music != null) music.SetTheme(preferences.Dark);
            UpdatePresetAppearance();
            panel.CornerRadius = Radius(26); bar.CornerRadius = Radius(5);
            Find<Button>("PinButton").SetResourceReference(Control.BackgroundProperty, preferences.Pinned ? "AccentSoft" : "Surface");
            Find<Button>("SettingsButton").SetResourceReference(Control.BackgroundProperty, settingsOpen ? "AccentSoft" : "Surface");
        }
        private CornerRadius Radius(double radius)
        {
            switch (preferences.Edge)
            {
                case Edge.Bottom: return new CornerRadius(radius, radius, 0, 0);
                case Edge.Left: return new CornerRadius(0, radius, radius, 0);
                case Edge.Right: return new CornerRadius(radius, 0, 0, radius);
                default: return new CornerRadius(0, 0, radius, radius);
            }
        }

        private ContextMenu MakeContextMenu()
        {
            ContextMenu menu = new ContextMenu();
            MenuItem pinned = new MenuItem { Header = "Оставлять раскрытой", IsCheckable = true };
            pinned.Click += delegate { SetPinned(pinned.IsChecked); };
            menu.Opened += delegate { pinned.IsChecked = preferences.Pinned; };
            menu.Items.Add(pinned);
            MenuItem settings = new MenuItem { Header = "Настройки" }; settings.Click += delegate { Reveal(true); }; menu.Items.Add(settings);
            menu.Items.Add(new Separator());
            MenuItem quit = new MenuItem { Header = "Выйти" }; quit.Click += delegate { Window.Close(); }; menu.Items.Add(quit);
            return menu;
        }

        private void CreateTray()
        {
            trayIcon = BuildTrayIcon();
            trayMenu = MakeTrayMenu();
            tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "Dynamic Island", Visible = true, ContextMenuStrip = trayMenu };
            tray.MouseClick += delegate(object sender, Forms.MouseEventArgs args)
            {
                if (args.Button == Forms.MouseButtons.Left) Window.Dispatcher.Invoke(new Action(delegate { Reveal(false); }));
            };
        }

        private Forms.ContextMenuStrip MakeTrayMenu()
        {
            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Открыть остров", null, delegate { Window.Dispatcher.Invoke(new Action(delegate { Reveal(false); })); });
            menu.Items.Add("Настройки", null, delegate { Window.Dispatcher.Invoke(new Action(delegate { Reveal(true); })); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Выйти", null, delegate { Window.Dispatcher.Invoke(new Action(delegate { Window.Close(); })); });
            return menu;
        }

        private static Drawing.Icon BuildTrayIcon()
        {
            Canvas canvas = new Canvas { Width = 32, Height = 32 };
            canvas.Children.Add(new System.Windows.Shapes.Ellipse { Width = 32, Height = 32, Fill = new SolidColorBrush(Color.FromRgb(112, 65, 217)) });
            ContentControl brand = new ContentControl { Content = Icons.Load("brand"), Foreground = Brushes.White, Width = 24, Height = 24 };
            Canvas.SetLeft(brand, 4); Canvas.SetTop(brand, 4); canvas.Children.Add(brand);
            canvas.Measure(new Size(32, 32)); canvas.Arrange(new Rect(0, 0, 32, 32)); canvas.UpdateLayout();
            RenderTargetBitmap rendered = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32); rendered.Render(canvas);
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(rendered));
            using (MemoryStream png = new MemoryStream())
            {
                encoder.Save(png); png.Position = 0;
                using (Drawing.Bitmap bitmap = new Drawing.Bitmap(png))
                {
                    IntPtr icon = bitmap.GetHicon();
                    try { using (Drawing.Icon original = Drawing.Icon.FromHandle(icon)) return (Drawing.Icon)original.Clone(); }
                    finally { Native.DestroyIcon(icon); }
                }
            }
        }

        private static void LoadIcons(DependencyObject root)
        {
            ContentControl target = root as ContentControl;
            if (target != null && target.Tag is string && ((string)target.Tag).StartsWith("icon:", StringComparison.Ordinal)) target.Content = Icons.Load(((string)target.Tag).Substring(5));
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                DependencyObject dependency = child as DependencyObject;
                if (dependency != null) LoadIcons(dependency);
            }
        }

        private void Dispose()
        {
            if (disposed) return;
            disposed = true; polling.Stop(); clock.Stop(); limitsPolling.Stop(); counterTicks.Stop(); StopMorph();
            timerCue.Dispose();
            mediaPolling.Stop(); desktopMedia.Dispose();
            if (music != null) music.Dispose();
            limitsClient.LoginCompleted -= OnLoginCompleted; limitsClient.Dispose();
            if (hotkey) Native.UnregisterHotKey(handle, 1);
            if (source != null) source.RemoveHook(WindowMessage);
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (trayMenu != null) trayMenu.Dispose();
            if (trayIcon != null) trayIcon.Dispose();
        }

        private void BeginSmokeTest()
        {
            polling.Stop();
            string directory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"));
            Directory.CreateDirectory(directory);
            List<string> results = new List<string>();
            DispatcherTimer test = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            int step = 0;
            test.Tick += delegate
            {
                try
                {
                    if (step == 0)
                    {
                        Assert(!expanded && VisibleBounds().Height <= 16, "Collapsed visible surface is only a strip", results);
                        Drawing.Rectangle rect = VisibleBounds(), native = CurrentBounds();
                        bool hitHandled = false;
                        IntPtr hit = WindowMessage(handle, 0x84, IntPtr.Zero, new IntPtr(((native.Top + native.Height / 2) << 16) | ((native.Left + 4) & 0xffff)), ref hitHandled);
                        Assert(hitHandled && hit == new IntPtr(-1), "Transparent canvas passes desktop clicks through", results);
                        HandlePointer(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
                        Assert(CurrentBounds() == native && VisibleBounds() == rect, "Hover starts at the exact strip position without relocating the native window", results);
                        if (SystemParameters.ClientAreaAnimation)
                        {
                            DispatcherTimer probe = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
                            int samples = 0;
                            probe.Tick += delegate
                            {
                                try
                                {
                                    Drawing.Rectangle full = Placement.Get(Area(), preferences.Edge, true, Scale(), preferences.EdgePosition);
                                    Drawing.Rectangle visible = VisibleBounds();
                                    if (animating && visible.Height == rect.Height && ++samples < 13) return;
                                    probe.Stop();
                                    Assert(animating && visible.Height > rect.Height && visible.Height < full.Height, "Hover morph has an intermediate visible size (animating=" + animating + ", strip=" + rect.Height + ", visible=" + visible.Height + ", full=" + full.Height + ")", results);
                                    Assert(CurrentBounds() == full, "Native window stays fixed during the hover animation", results);
                                    Snapshot(Path.Combine(directory, "island-opening.png"), false);
                                    Assert(panel.Clip != null && Math.Abs(panel.Clip.Bounds.Width - panel.ActualWidth) < 1, "Animated content is clipped to the visible island", results);
                                    HandlePointer(full.Left + 5, full.Bottom - 5);
                                    Assert(expanded, "Entering the transparent animation area does not interrupt opening", results);
                                }
                                catch (Exception error)
                                {
                                    probe.Stop(); ExitCode = 1; test.Stop(); results.Add("FAIL: " + error); File.WriteAllLines(Path.Combine(directory, "smoke-test.txt"), results.ToArray()); Window.Close();
                                }
                            };
                            probe.Start();
                        }
                    }
                    else if (step == 1)
                    {
                        Assert(expanded && !animating && panel.IsVisible, "Hover expands native panel", results);
                        Assert(Find<TextBlock>("AppHeading").Text == "Dynamic Island", "Full application heading", results);
                        Assert(preferences.Delay == 200 && preferences.CenterOnEdge && focus.Duration == TimeSpan.Zero && Find<TextBox>("TimerLabel").Text == "" && Find<TextBox>("NewPresetMinutes").Text == "", "Defaults have no focus time or preset draft, 200 ms delay, and centered placement", results);
                        limits = LimitsSnapshot.Parse(Json.Parse("{\"rateLimits\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":25,\"windowDurationMins\":300,\"resetsAt\":1900000000},\"secondary\":{\"usedPercent\":42,\"windowDurationMins\":10080,\"resetsAt\":1900400000}}}"), "plus");
                        RenderLimits(); Find<TextBlock>("LimitsStatus").Text = "Тестовые данные · проверка интерфейса";
                        Find<TextBlock>("SettingsLoginStatus").Text = "Тестовые данные · проверка интерфейса входа";
                        Assert(Find<TextBlock>("FiveHourValue").Text == "75%" && Find<TextBlock>("WeeklyValue").Text == "58%", "Both quota percentages render", results);
                        Window.UpdateLayout();
                        Assert(Math.Abs(Find<Border>("FocusCard").ActualHeight - Find<Border>("StopwatchCard").ActualHeight) < 1 && Math.Abs(Find<Border>("FocusCard").ActualWidth - Find<Border>("StopwatchCard").ActualWidth) <= 1, "Timer cards share aligned dimensions", results);
                        Point focusAction = Find<Button>("TimerButton").TranslatePoint(new Point(0, 0), content), counterAction = Find<Button>("StopwatchButton").TranslatePoint(new Point(0, 0), content);
                        Assert(Math.Abs(focusAction.Y - counterAction.Y) < 1 && Find<ContentControl>("TimerIcon").ActualWidth == Find<ContentControl>("StopwatchIcon").ActualWidth, "Timer action buttons and icons align", results);
                        Snapshot(Path.Combine(directory, "island-light.png"));
                        ChangeTheme(true); Find<CheckBox>("DarkMode").IsChecked = true;
                        Snapshot(Path.Combine(directory, "island-dark.png"));
                        ShowSettings(true);
                    }
                    else if (step == 2)
                    {
                        Snapshot(Path.Combine(directory, "island-settings-dark.png"));
                        Assert(Find<StackPanel>("Settings").IsVisible, "Settings and dark theme render", results);
                        Button quit = Find<Button>("QuitButton");
                        double bottom = quit.TranslatePoint(new Point(0, quit.ActualHeight), content).Y;
                        Assert(bottom <= content.ActualHeight && quit.TranslatePoint(new Point(0, 0), content).Y >= Find<ScrollViewer>("SettingsScroll").TranslatePoint(new Point(0, Find<ScrollViewer>("SettingsScroll").ActualHeight), content).Y, "Settings actions stay in a fixed row below the scrollable content", results);
                        Assert(Window.FindName("MonitorSelect") == null && Window.FindName("KeepOpen") == null && Window.FindName("EdgeTop") == null && Window.FindName("ResetTimer") == null, "Removed settings and timer reset are absent", results);
                        Assert(Window.FindName("FocusMinutes") == null && Window.FindName("ApplyFocus") == null && Window.FindName("ChooseCodex") == null && Find<Button>("SettingsLoginButton").IsVisible, "Direct time editing and browser login replace separate fields and file picker", results);
                        using (Forms.ContextMenuStrip menu = MakeTrayMenu())
                            Assert(menu.Items.Count == 4 && menu.Items[0].Text == "Открыть остров" && menu.Items[1].Text == "Настройки" && menu.Items[3].Text == "Выйти", "Tray contains open, settings and exit only", results);
                        Assert(string.Join(",", preferences.Presets) == "5,10,15,30,60,90", "Default presets match the requested intervals", results);
                        Find<TextBox>("NewPresetMinutes").Text = "20"; Find<Button>("AddPreset").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert(preferences.Presets.Contains(20) && presetButtons.Count == 7, "Adding a custom preset updates the main timer", results);
                        Assert(Find<TextBox>("NewPresetMinutes").Text == "", "Adding a preset clears its input", results);
                        Find<TextBox>("NewPresetMinutes").Text = "20";
                        Find<Button>("AddPreset").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert(preferences.Presets.Count == 7, "Duplicate presets are rejected", results);
                        Find<TextBox>("NewPresetMinutes").Text = "240"; AddPreset();
                        Assert(preferences.Presets.Contains(240) && presetButtons.Count == 8, "Presets longer than 180 minutes are accepted", results);
                        Find<TextBox>("NewPresetMinutes").Text = "0"; AddPreset();
                        Assert(preferences.Presets.Count == 8, "Invalid presets are rejected", results);
                        RemovePreset(240); SetFocusMinutes(5);
                        focus.Toggle(DateTime.UtcNow); RemovePreset(20);
                        Assert(focus.Running && !preferences.Presets.Contains(20) && presetButtons.Count == 6, "Removing a preset updates the main timer without stopping the current focus", results);
                        SetFocusSeconds(0);
                        ShowSettings(false);
                        BeginFocusEdit(); Find<TextBox>("TimerLabel").Text = "0";
                        Assert(!CommitFocusEdit() && preferences.FocusSeconds == 0, "Invalid focus duration does not modify timer", results);
                        Find<TextBox>("TimerLabel").Text = "45:30";
                        Assert(CommitFocusEdit() && !settingsOpen && preferences.FocusSeconds == 2730 && Find<TextBox>("TimerLabel").Text == "45:30", "Editing the actual timer digits applies minutes and seconds", results);
                        BeginFocusEdit(); Find<TextBox>("TimerLabel").Text = "12"; UpdateClock();
                        Assert(Find<TextBox>("TimerLabel").Text == "12", "Clock ticks do not overwrite a time being edited", results);
                        grace = DateTime.MinValue; preferences.Delay = 0; HandleHover(false);
                        Assert(expanded, "Pointer exit cannot discard an active time edit", results);
                        CancelFocusEdit();
                        Assert(preferences.FocusSeconds == 2730 && Find<TextBox>("TimerLabel").Text == "45:30", "Cancelling time editing restores the countdown", results);
                        focus.Toggle(DateTime.UtcNow); UpdateClock(); BeginFocusEdit(); CommitFocusEdit();
                        Assert(focus.Running, "Leaving an unchanged time does not reset or pause focus", results);
                        focus.Reset(); BeginFocusEdit(); Find<TextBox>("TimerLabel").Text = "45"; CommitFocusEdit();
                        Assert(preferences.FocusSeconds == 2700 && Find<TextBox>("TimerLabel").Text == "45:00", "Bare minutes are accepted in the timer readout", results);
                        foreach (Button preset in presetButtons)
                        {
                            int minutes = (int)preset.Tag;
                            focus.Toggle(DateTime.UtcNow);
                            preset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Assert(!focus.Running && preferences.FocusMinutes == minutes && focus.Duration == TimeSpan.FromMinutes(minutes), "Preset " + minutes + " selects and resets the focus duration", results);
                        }
                        SetFocusMinutes(45);
                        Find<Button>("StopwatchButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert(counter.Running && counterTicks.IsEnabled && !focus.Running, "Stopwatch starts independently of the focus timer", results);
                        SetPinned(true);
                        HandlePointer(Area().Left + 15, Area().Bottom - 15);
                        Assert(expanded && preferences.Pinned, "Pin retains expanded panel outside hover", results);
                        SetPinned(false); preferences.Delay = 200; grace = DateTime.MinValue; HandlePointer(Area().Left + 15, Area().Bottom - 15);
                    }
                    else if (step == 3)
                    {
                        Find<Button>("StopwatchButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert(!counter.Running && counter.Elapsed(DateTime.UtcNow).TotalMilliseconds >= 400 && Find<TextBlock>("StopwatchStatus").Text == "На паузе", "Stopwatch advances and pauses while panel is hidden", results);
                        Find<Button>("StopwatchReset").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert(counter.Elapsed(DateTime.UtcNow) == TimeSpan.Zero && Find<TextBlock>("StopwatchLabel").Text == "00:00.0", "Stopwatch reset clears the display", results);
                        HandlePointer(Area().Left + 15, Area().Bottom - 15);
                    }
                    else if (step == 4)
                    {
                        Assert(!expanded && bar.IsVisible, "Leaving collapses after configured delay", results);
                        foreach (Edge edge in Enum.GetValues(typeof(Edge)))
                        {
                            preferences.Edge = edge; Snap();
                            Drawing.Rectangle stable = CurrentBounds();
                            Assert(VisibleBounds() == Placement.Get(Area(), edge, false, Scale()), "Strip bounds for " + edge, results);
                            SetExpanded(true, true);
                            Assert(CurrentBounds() == Placement.Get(Area(), edge, true, Scale()), "Expanded native bounds for " + edge, results);
                            SetExpanded(false, true);
                            Assert(CurrentBounds() == stable, "Hover endpoints share the same native bounds for " + edge, results);
                        }
                        Drawing.Rectangle area = Area();
                        Find<CheckBox>("CenterOnEdge").IsChecked = false;
                        foreach (bool open in new[] { false, true })
                        foreach (Edge edge in Enum.GetValues(typeof(Edge)))
                        {
                            SetExpanded(open, true);
                            Drawing.Rectangle before = CurrentBounds();
                            Drawing.Rectangle visible = VisibleBounds();
                            Drawing.Point start = new Drawing.Point(visible.Left + visible.Width / 2, visible.Top + 4);
                            Drawing.Point drop = edge == Edge.Top ? new Drawing.Point(area.Left + area.Width / 3, area.Top + 2) : edge == Edge.Bottom ? new Drawing.Point(area.Left + area.Width * 2 / 3, area.Bottom - 2) : edge == Edge.Left ? new Drawing.Point(area.Left + 2, area.Top + area.Height / 3) : new Drawing.Point(area.Right - 2, area.Top + area.Height * 2 / 3);
                            BeginDrag(start); MoveDrag(drop);
                            Assert(CurrentBounds().Left == before.Left + drop.X - start.X && CurrentBounds().Top == before.Top + drop.Y - start.Y, "Drag follows the held pointer for " + edge + ", expanded=" + open, results);
                            double expected = Placement.Position(area, edge, VisibleBounds());
                            EndDrag(drop, false);
                            Assert(preferences.Edge == edge && Math.Abs(preferences.EdgePosition - expected) < 0.000001 && VisibleBounds() == Placement.Get(Area(), edge, open, Scale(), expected), "Drop snaps and retains position for " + edge + ", expanded=" + open, results);
                        }
                        Edge retained = preferences.Edge; double retainedPosition = preferences.EdgePosition;
                        Drawing.Rectangle original = CurrentBounds();
                        Drawing.Point origin = new Drawing.Point(original.Left + 4, original.Top + 4);
                        BeginDrag(origin); MoveDrag(new Drawing.Point(area.Left + 2, area.Top + area.Height / 2)); EndDrag(origin, true);
                        Assert(preferences.Edge == retained && preferences.EdgePosition == retainedPosition && CurrentBounds() == original, "Cancelled drag restores the saved position", results);
                        Find<CheckBox>("CenterOnEdge").IsChecked = true;
                        foreach (Edge edge in Enum.GetValues(typeof(Edge)))
                        {
                            Drawing.Rectangle before = VisibleBounds();
                            Drawing.Point start = new Drawing.Point(before.Left + 4, before.Top + 4);
                            Drawing.Point drop = edge == Edge.Top ? new Drawing.Point(area.Left + area.Width / 3, area.Top + 2) : edge == Edge.Bottom ? new Drawing.Point(area.Left + area.Width / 3, area.Bottom - 2) : edge == Edge.Left ? new Drawing.Point(area.Left + 2, area.Top + area.Height / 3) : new Drawing.Point(area.Right - 2, area.Top + area.Height / 3);
                            BeginDrag(start); MoveDrag(drop); EndDrag(drop, false);
                            Assert(preferences.CenterOnEdge && preferences.Edge == edge && preferences.EdgePosition == 0.5 && VisibleBounds() == Placement.Get(Area(), edge, true, Scale()), "Centered drop for " + edge, results);
                        }
                        Find<CheckBox>("CenterOnEdge").IsChecked = false;
                        Find<Slider>("HideDelay").Value = 0; grace = DateTime.MinValue; HandleHover(false);
                        Assert(!expanded && preferences.Delay == 0, "Zero delay starts collapse immediately after pointer exit", results);
                        focus.Toggle(DateTime.UtcNow); UpdateClock();
                        Assert(focus.Running && Find<TextBox>("TimerLabel").Text == "45:00", "Focus timer starts with configured duration", results);
                        focus.Reset();
                        SetExpanded(true, true); CollapseByUser(); HandleHover(true);
                        Assert(!expanded, "Close suppresses hover reopening while cursor stays over the widget", results);
                    }
                    else if (step == 5)
                    {
                        Assert(!expanded && !animating && bar.IsVisible, "Explicit close finishes as a strip", results);
                        HandleHover(false);
                        Drawing.Rectangle strip = VisibleBounds(); HandlePointer(strip.Left + strip.Width / 2, strip.Top + strip.Height / 2);
                        Assert(expanded, "Returning to the strip opens again after explicit close", results);
                    }
                    else if (step == 6)
                    {
                        Assert(expanded && !animating && CurrentBounds() == Placement.Get(Area(), preferences.Edge, true, Scale(), preferences.EdgePosition), "Repeated hover finishes at the saved edge position", results);
                        SetPinned(true); SetCenterOnEdge(true); SetFocusSeconds(90); focus.Toggle(DateTime.UtcNow); counter.Toggle(DateTime.UtcNow);
                        preferences.Presets = new List<int> { 20, 99 }; preferences.CodexPath = "old-codex.exe"; limitsClient.ConfiguredPath = preferences.CodexPath;
                        preferences.Monitor = "old-monitor"; preferences.EdgePosition = 0.2;
                        LimitsSnapshot retainedLimits = limits;
                        Find<Button>("ResetSettingsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert(preferences.Edge == Edge.Top && preferences.Monitor == "" && preferences.EdgePosition == 0.5 && preferences.CenterOnEdge && !preferences.Dark && !preferences.Pinned && preferences.Delay == 200 && preferences.FocusSeconds == 0 && string.Join(",", preferences.Presets) == "5,10,15,30,60,90" && preferences.CodexPath == "", "Reset restores every widget preference to defaults", results);
                        Assert(!focus.Running && !counter.Running && counter.Elapsed(DateTime.UtcNow) == TimeSpan.Zero && focus.Duration == TimeSpan.Zero, "Reset stops both timers and leaves focus time unset", results);
                        Assert(Find<CheckBox>("DarkMode").IsChecked == false && Find<CheckBox>("CenterOnEdge").IsChecked == true && Find<Slider>("HideDelay").Value == 200 && presetButtons.Count == 6 && Find<TextBox>("TimerLabel").Text == "" && Find<TextBox>("NewPresetMinutes").Text == "", "Reset immediately updates the visible controls", results);
                        Assert(object.ReferenceEquals(retainedLimits, limits) && limitsClient.ConfiguredPath == "", "Widget reset preserves account data and returns to automatic Codex discovery", results);
                        Assert(CurrentBounds() == Placement.Get(Area(), Edge.Top, true, Scale()), "Reset moves the widget to the default monitor and centered top edge", results);
                        ShowSettings(false);
                        SetFocusSeconds((long)int.MaxValue * 60);
                        preferences.Presets.Add(int.MaxValue); BuildPresets(); Window.UpdateLayout();
                        TextBox timeInput = Find<TextBox>("TimerLabel");
                        FormattedText timeMeasure = new FormattedText(timeInput.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(timeInput.FontFamily, timeInput.FontStyle, timeInput.FontWeight, timeInput.FontStretch), timeInput.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(timeInput).PixelsPerDip);
                        Assert(timeInput.Text == "2147483647:00" && timeMeasure.WidthIncludingTrailingWhitespace <= timeInput.ActualWidth, "Long time readout scales to fit instead of covering its action", results);
                        Snapshot(Path.Combine(directory, "island-long-duration.png"));
                        BeginFocusEdit(); timeInput.Text = ""; CommitFocusEdit();
                        Assert(preferences.FocusSeconds == 0 && focus.Duration == TimeSpan.Zero && !focus.Running, "Clearing the readout returns to an unset timer", results);
                        ResetSettings(); ShowSettings(true);
                        for (int minutes = 100; minutes <= 120; minutes++) preferences.Presets.Add(minutes);
                        BuildPresets(); Window.UpdateLayout();
                        Assert(Find<ScrollViewer>("SettingsScroll").ScrollableHeight > 0 && Find<Button>("ResetSettingsButton").TranslatePoint(new Point(0, 0), content).Y >= Find<ScrollViewer>("SettingsScroll").TranslatePoint(new Point(0, Find<ScrollViewer>("SettingsScroll").ActualHeight), content).Y, "Long preset lists scroll without hiding settings actions", results);
                        ResetSettings(); ShowSettings(false);
                        Drawing.Rectangle normal = CurrentBounds();
                        ApplyBounds(new Drawing.Rectangle(normal.Left, normal.Top, 320, 300)); panel.Width = 320 / Scale(); panel.Height = 300 / Scale();
                        content.Width = 320 / Scale() - 50; content.Height = 300 / Scale() - 48;
                        Window.UpdateLayout();
                        Assert(Find<ScrollViewer>("OverviewScroll").ScrollableHeight > 0 && Find<Button>("HideButton").TranslatePoint(new Point(Find<Button>("HideButton").ActualWidth, 0), content).X <= content.ActualWidth + 1, "Small work areas scroll while header actions stay inside the panel", results);
                        Snapshot(Path.Combine(directory, "island-small-area.png")); Snap();
                        CheckFeatures(directory, results);
                        CheckWidgets(directory, results);
                        test.Stop(); File.WriteAllLines(Path.Combine(directory, "smoke-test.txt"), results.ToArray());
                        Window.Close();
                    }
                    step++;
                }
                catch (Exception error)
                {
                    ExitCode = 1; test.Stop(); results.Add("FAIL: " + error); File.WriteAllLines(Path.Combine(directory, "smoke-test.txt"), results.ToArray());
                    Window.Close();
                }
            };
            test.Start();
        }
        private static void Assert(bool condition, string label, List<string> results)
        {
            if (!condition) throw new InvalidOperationException(label);
            results.Add("PASS: " + label);
        }
        private void Snapshot(string path, bool settle = true)
        {
            if (settle)
            {
            content.BeginAnimation(UIElement.OpacityProperty, null); content.Opacity = 1;
            foreach (string name in new[] { "FiveHour", "Weekly" })
            {
                ProgressBar progress = Find<ProgressBar>(name + "Bar");
                QuotaWindow quota = limits == null ? null : name == "FiveHour" ? limits.FiveHour : limits.Weekly;
                progress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
                progress.Value = quota == null ? 0 : quota.RemainingPercent;
            }
            }
            Window.UpdateLayout();
            RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(Window.ActualWidth), (int)Math.Ceiling(Window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(Window);
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (Stream file = File.Create(path)) encoder.Save(file);
        }
    }

    internal static class Icons
    {
        public static Viewbox Load(string name)
        {
            Canvas canvas = new Canvas { Width = 24, Height = 24, UseLayoutRounding = false, SnapsToDevicePixels = false };
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("icons." + name))
            {
                if (stream == null) throw new InvalidOperationException("Missing Iconly asset: " + name);
                XElement svg = XElement.Load(stream);
                Render(svg, canvas, new Dictionary<string, string> { { "fill", "black" }, { "stroke", "none" }, { "stroke-width", "1" }, { "fill-rule", "nonzero" }, { "stroke-linecap", "butt" }, { "stroke-linejoin", "miter" } }, Matrix.Identity);
            }
            return new Viewbox { Child = canvas, Stretch = Stretch.Uniform, UseLayoutRounding = false, SnapsToDevicePixels = false };
        }
        private static void Render(XElement element, Canvas canvas, Dictionary<string, string> inherited, Matrix parent)
        {
            string tag = element.Name.LocalName;
            if (tag == "title" || tag == "desc" || tag == "metadata") return;
            Dictionary<string, string> style = new Dictionary<string, string>(inherited);
            foreach (XAttribute attribute in element.Attributes()) style[attribute.Name.LocalName] = attribute.Value;
            Matrix transform = Transform((string)element.Attribute("transform")); transform.Append(parent);
            if (tag == "svg" || tag == "g") { foreach (XElement child in element.Elements()) Render(child, canvas, style, transform); return; }
            Geometry geometry;
            if (tag == "path") geometry = Geometry.Parse((style["fill-rule"] == "evenodd" ? "F0 " : "F1 ") + (string)element.Attribute("d"));
            else if (tag == "line") geometry = new LineGeometry(new Point(Number(element, "x1"), Number(element, "y1")), new Point(Number(element, "x2"), Number(element, "y2")));
            else if (tag == "circle" || tag == "ellipse") geometry = new EllipseGeometry(new Point(Number(element, "cx"), Number(element, "cy")), Number(element, tag == "circle" ? "r" : "rx"), Number(element, tag == "circle" ? "r" : "ry"));
            else if (tag == "polyline" || tag == "polygon") geometry = Geometry.Parse("M " + (string)element.Attribute("points") + (tag == "polygon" ? " Z" : ""));
            else if (tag == "rect") geometry = new RectangleGeometry(new Rect(Number(element, "x"), Number(element, "y"), Number(element, "width"), Number(element, "height")), Number(element, "rx"), Number(element, "ry"));
            else throw new InvalidOperationException("Unsupported Iconly SVG element: " + tag);
            if (geometry.IsFrozen) geometry = geometry.Clone();
            geometry.Transform = new MatrixTransform(transform);
            System.Windows.Shapes.Path path = new System.Windows.Shapes.Path { Data = geometry, UseLayoutRounding = false, SnapsToDevicePixels = false };
            path.StrokeStartLineCap = path.StrokeEndLineCap = style["stroke-linecap"] == "round" ? PenLineCap.Round : style["stroke-linecap"] == "square" ? PenLineCap.Square : PenLineCap.Flat;
            path.StrokeLineJoin = style["stroke-linejoin"] == "round" ? PenLineJoin.Round : style["stroke-linejoin"] == "bevel" ? PenLineJoin.Bevel : PenLineJoin.Miter;
            if (style["fill"] != "none") Tint(path, System.Windows.Shapes.Shape.FillProperty);
            if (style["stroke"] != "none") { path.StrokeThickness = double.Parse(style["stroke-width"], CultureInfo.InvariantCulture); Tint(path, System.Windows.Shapes.Shape.StrokeProperty); }
            string opacity; if (style.TryGetValue("opacity", out opacity)) path.Opacity = double.Parse(opacity, CultureInfo.InvariantCulture);
            canvas.Children.Add(path);
        }
        private static void Tint(System.Windows.Shapes.Path path, DependencyProperty property)
        {
            path.SetBinding(property, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(ContentControl), 1) });
        }
        private static double Number(XElement element, string attribute) { return double.Parse((string)element.Attribute(attribute) ?? "0", CultureInfo.InvariantCulture); }
        private static Matrix Transform(string value)
        {
            Matrix result = Matrix.Identity;
            if (string.IsNullOrEmpty(value)) return result;
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(value, @"(\w+)\s*\(([^)]+)\)"))
            {
                string[] tokens = System.Text.RegularExpressions.Regex.Split(match.Groups[2].Value.Trim(), @"[\s,]+");
                double[] numbers = Array.ConvertAll(tokens, delegate(string token) { return double.Parse(token, CultureInfo.InvariantCulture); });
                string command = match.Groups[1].Value;
                if (command == "translate") result.Translate(numbers[0], numbers.Length > 1 ? numbers[1] : 0);
                else if (command == "scale") result.Scale(numbers[0], numbers.Length > 1 ? numbers[1] : numbers[0]);
                else if (command == "rotate") { if (numbers.Length == 3) result.RotateAt(numbers[0], numbers[1], numbers[2]); else result.Rotate(numbers[0]); }
                else if (command == "matrix" && numbers.Length == 6) result.Append(new Matrix(numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5]));
                else throw new InvalidOperationException("Unsupported Iconly SVG transform: " + command);
            }
            return result;
        }
        public static void VerifyManifest()
        {
            object[] assets;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("icons.manifest"))
            using (StreamReader reader = new StreamReader(stream)) assets = (object[])new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(reader.ReadToEnd());
            foreach (object asset in assets)
            {
                IDictionary<string, object> metadata = Json.Map(asset);
                string file = Json.Text(metadata, "file");
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("icons." + Path.GetFileNameWithoutExtension(file)))
                using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                {
                    string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                    if (actual != Json.Text(metadata, "sha256")) throw new InvalidOperationException("Iconly asset checksum mismatch: " + file);
                    if (Json.Text(metadata, "style") != "Light" || Json.Text(metadata, "type") != "regular") throw new InvalidOperationException("Iconly asset style mismatch: " + file);
                }
            }
        }
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
    }

    internal static class Checks
    {
        public static int LiveLimits()
        {
            string directory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"));
            Directory.CreateDirectory(directory);
            try
            {
                WidgetConfiguration configuration;
                Preferences current = WidgetSettingsStore.Load(Preferences.FilePath, WidgetCatalog.Create(), out configuration);
                using (LimitsClient client = new LimitsClient { ConfiguredPath = current.CodexPath })
                {
                    LimitsSnapshot snapshot = client.Read().GetAwaiter().GetResult();
                    new XElement("Limits", new XAttribute("hasData", snapshot.HasData), new XAttribute("plan", snapshot.Plan),
                        new XElement("FiveHourRemaining", snapshot.FiveHour == null ? "unavailable" : snapshot.FiveHour.RemainingPercent.ToString(CultureInfo.InvariantCulture)),
                        new XElement("WeeklyRemaining", snapshot.Weekly == null ? "unavailable" : snapshot.Weekly.RemainingPercent.ToString(CultureInfo.InvariantCulture)),
                        new XElement("UpdatedUtc", snapshot.UpdatedAt.ToString("O"))).Save(Path.Combine(directory, "live-limits.xml"));
                    return snapshot.HasData ? 0 : 2;
                }
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(directory, "live-limits-error.txt"), error is InvalidOperationException || error is TimeoutException ? error.Message : "Limits request failed: " + error.GetType().Name);
                return 1;
            }
        }

        public static int Run()
        {
            List<string> passed = new List<string>();
            try
            {
                foreach (Drawing.Rectangle screen in new[] { new Drawing.Rectangle(0, 0, 1920, 1040), new Drawing.Rectangle(-2560, -200, 2560, 1400), new Drawing.Rectangle(1920, 0, 1366, 728), new Drawing.Rectangle(0, 0, 320, 300) })
                foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
                foreach (Edge edge in Enum.GetValues(typeof(Edge)))
                foreach (bool expanded in new[] { false, true })
                foreach (double position in new[] { 0.0, 0.1, 0.5, 0.9, 1.0 })
                {
                    Drawing.Rectangle rect = Placement.Get(screen, edge, expanded, scale, position);
                    Require(screen.Contains(rect), "Panel must remain within selected monitor");
                    Require(edge == Edge.Top ? rect.Top == screen.Top : edge == Edge.Bottom ? rect.Bottom == screen.Bottom : edge == Edge.Left ? rect.Left == screen.Left : rect.Right == screen.Right, "Panel must touch selected edge");
                    if (!expanded) Require(Math.Min(rect.Width, rect.Height) == (int)Math.Round(8 * scale), "Collapsed hit area must remain a small strip");
                }
                passed.Add("PASS: 640 edge/monitor/DPI/state/offset placement combinations, including corners");
                DateTime time = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
                FocusTimer timer = new FocusTimer(); timer.Toggle(time);
                Require(timer.Duration == TimeSpan.Zero && !timer.Running, "An unset focus time cannot start a default duration");
                timer.SetMinutes(25); timer.Toggle(time);
                Require(timer.Remaining(time.AddMinutes(5)) == TimeSpan.FromMinutes(20), "Focus elapsed time");
                timer.Toggle(time.AddMinutes(5)); Require(!timer.Running, "Pause state");
                Require(timer.Remaining(time.AddHours(3)) == TimeSpan.FromMinutes(20), "Pause must preserve remaining time");
                timer.Toggle(time.AddHours(3)); Require(timer.Tick(time.AddHours(4)), "Completion after sleep / delay");
                Require(!timer.Tick(time.AddHours(5)), "Completion notification must fire only once");
                timer.Reset(); Require(timer.Remaining(time) == TimeSpan.FromMinutes(25), "Timer reset");
                timer.SetMinutes(45); timer.Toggle(time);
                Require(timer.Remaining(time.AddMinutes(10)) == TimeSpan.FromMinutes(35), "Custom duration countdown");
                timer.Reset(); Require(timer.Remaining(time) == TimeSpan.FromMinutes(45), "Reset preserves configured duration");
                bool rejected = false; try { timer.SetMinutes(0); } catch (ArgumentOutOfRangeException) { rejected = true; }
                Require(rejected && timer.Duration == TimeSpan.FromMinutes(45), "Invalid duration must preserve existing timer");
                passed.Add("PASS: editable focus duration, pause/resume/reset and completion after sleep");
                long parsed;
                Require(FocusTimeInput.TryParse("5", out parsed) && parsed == 300, "Bare time means minutes");
                Require(FocusTimeInput.TryParse("01:30", out parsed) && parsed == 90 && FocusTimeInput.Format(parsed) == "01:30", "Time readout supports seconds");
                Require(FocusTimeInput.TryParse("0:01", out parsed) && parsed == 1 && FocusTimeInput.TryParse("180:00", out parsed) && parsed == 10800, "Time readout accepts duration boundaries");
                Require(FocusTimeInput.TryParse("1440:30", out parsed) && parsed == 86430 && FocusTimeInput.TryParse("2147483647", out parsed) && parsed == (long)int.MaxValue * 60, "Long durations parse without integer overflow");
                timer.SetMinutes(int.MaxValue); timer.Toggle(time);
                Require(timer.Remaining(time.AddSeconds(1)).TotalSeconds == (long)int.MaxValue * 60 - 1, "Long presets count down without overflowing");
                Require(FocusTimeInput.TryParse("1:02:03", out parsed) && parsed == 3723, "Timer supports hours, minutes and seconds");
                foreach (string invalid in new[] { "", "0", "0:00", "-1", "1:60", "1:60:3", "1:2:60", "-1:00:00", "99999999999:00:00", "2.5", "999999999999999999" })
                    Require(!FocusTimeInput.TryParse(invalid, out parsed), "Reject invalid time: " + invalid);
                timer.SetSeconds(90); timer.Toggle(time);
                Require(timer.Remaining(time.AddSeconds(31)) == TimeSpan.FromSeconds(59) && timer.Tick(time.AddSeconds(90)), "Custom seconds count down and finish accurately");
                passed.Add("PASS: direct time parsing, invalid input, second precision and duration boundaries");
                timer.SetSeconds(120); timer.Toggle(time);
                Require(Math.Abs(timer.Progress(time.AddSeconds(30)) - .25) < .0001, "Timer progress follows actual elapsed time");
                Require(timer.AddMinute(time.AddSeconds(30)) && timer.Running && timer.Duration.TotalSeconds == 180 && timer.Remaining(time.AddSeconds(30)).TotalSeconds == 150, "Adding one minute preserves running countdown");
                timer.Toggle(time.AddSeconds(30)); timer.AddMinute(time.AddSeconds(50));
                Require(!timer.Running && timer.Remaining(time.AddHours(1)).TotalSeconds == 210, "Adding a minute on pause preserves paused state");
                timer.Reset(); Require(timer.Remaining(time).TotalSeconds == 240 && timer.Progress(time) == 0, "Restart restores selected interval and progress");
                timer.SetSeconds(FocusTimeInput.MaxSeconds); Require(!timer.AddMinute(time), "Minute extension cannot overflow duration bounds");
                timer.SetSeconds(0); Require(!timer.AddMinute(time), "Adding a minute cannot assign an unset timer");
                passed.Add("PASS: timer progress, extension while running and paused, restart and overflow protection");
                CounterStopwatch counter = new CounterStopwatch(); counter.Toggle(time);
                Require(counter.Elapsed(time.AddMilliseconds(17500)) == TimeSpan.FromMilliseconds(17500), "Stopwatch elapsed time");
                counter.Toggle(time.AddMilliseconds(17500));
                Require(!counter.Running && counter.Elapsed(time.AddHours(3)) == TimeSpan.FromMilliseconds(17500), "Paused stopwatch must preserve elapsed time");
                counter.Toggle(time.AddHours(3));
                Require(counter.Elapsed(time.AddHours(3).AddSeconds(5)) == TimeSpan.FromMilliseconds(22500), "Stopwatch resume must exclude paused time");
                Require(CounterStopwatch.Format(TimeSpan.FromMilliseconds(22500)) == "00:22.5" && CounterStopwatch.Format(TimeSpan.FromSeconds(3661)) == "01:01:01", "Stopwatch tenths and hours formatting");
                counter.Reset(); Require(!counter.Running && counter.Elapsed(time.AddHours(5)) == TimeSpan.Zero, "Stopwatch reset stops and clears it");
                passed.Add("PASS: independent stopwatch elapsed time, pause/resume/reset and display formatting");
                LimitsSnapshot quota = LimitsSnapshot.Parse(Json.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":90,\"windowDurationMins\":300}},\"rateLimitsByLimitId\":{\"codex\":{\"limitId\":\"codex\",\"planType\":\"self_serve_business_prolite\",\"primary\":{\"usedPercent\":42,\"windowDurationMins\":10080,\"resetsAt\":1900000000},\"secondary\":{\"usedPercent\":25,\"windowDurationMins\":300}},\"other\":{\"primary\":{\"usedPercent\":100,\"windowDurationMins\":300}}}}"), "plus");
                Require(quota.FiveHour.RemainingPercent == 75 && quota.Weekly.RemainingPercent == 58 && quota.MainQuota == quota.Weekly && quota.Weekly.ResetsAt.HasValue, "Remaining percentages, swapped windows and Business main quota");
                quota = LimitsSnapshot.Parse(Json.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":125,\"windowDurationMins\":300},\"secondary\":null}}"), "plus");
                Require(quota.FiveHour.RemainingPercent == 0 && quota.Weekly == null && quota.MainQuota == quota.FiveHour, "Missing period is absent and exceeded quota clamps to zero");
                quota = LimitsSnapshot.Parse(Json.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":-5,\"windowDurationMins\":10080}}}"), "plus");
                Require(quota.Weekly.RemainingPercent == 100 && quota.MainQuota == null, "Do not substitute weekly quota for missing five-hour period");
                Require(!LimitsSnapshot.Parse(Json.Parse("{\"rateLimitsByLimitId\":{\"other\":{\"primary\":{\"usedPercent\":10,\"windowDurationMins\":300}}}}"), "plus").HasData, "Do not substitute unrelated limit buckets");
                Require(!LimitsSnapshot.Parse(Json.Parse("{\"rateLimits\":{\"primary\":{\"windowDurationMins\":300}}}"), "plus").HasData, "Missing usage must not appear as full quota");
                passed.Add("PASS: Codex quota conversion, bucket selection, plan selection and missing-data cases");
                double last = 0;
                for (int frame = 0; frame <= 100; frame++)
                {
                    double value = Motion.Ease(frame / 100.0); Require(value >= last && value <= 1, "Animation must move monotonically without overshoot"); last = value;
                }
                Require(Motion.Ease(0) == 0 && Motion.Ease(1) == 1 && Motion.Ease(0.001) < 0.000001 && 1 - Motion.Ease(0.999) < 0.000001, "Animation must ease smoothly at both ends");
                passed.Add("PASS: smooth animation curve, exact endpoints and no overshoot");
                foreach (string resource in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                {
                    if (resource.StartsWith("icons.", StringComparison.Ordinal) && resource != "icons.manifest") Icons.Load(resource.Substring(6));
                }
                Icons.VerifyManifest();
                passed.Add("PASS: every Iconly SVG renders and matches its Light/Regular manifest checksum");
                foreach (string[] palette in new[] { ThemePalette.Light, ThemePalette.Dark })
                foreach (int background in new[] { 0, 1, 6 })
                foreach (int foreground in new[] { 2, 3, 5 })
                    Require(ThemePalette.Contrast(palette[foreground], palette[background]) >= 4.5, "Text contrast must be at least 4.5:1 for " + palette[foreground] + " on " + palette[background]);
                passed.Add("PASS: light and dark text, secondary text and accents meet 4.5:1 contrast on every surface");
                byte[] chime = TimerCue.CreateWave();
                Require(chime.Length == 44 + (int)(TimerCue.SampleRate * TimerCue.Seconds) * 2 && System.Text.Encoding.ASCII.GetString(chime, 0, 4) == "RIFF" && TimerCue.Seconds > 3, "Completion cue is a valid 3.3-second PCM wave");
                passed.Add("PASS: timer completion cue duration and WAV structure without playing audio");
                string directory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"));
                Directory.CreateDirectory(directory);
                string settings = Path.Combine(directory, "test-settings.xml");
                Preferences original = new Preferences { Edge = Edge.Right, Monitor = "test-screen", Dark = true, Pinned = true, Delay = 800, FocusMinutes = 45, CodexPath = "test-codex.exe", EdgePosition = 0.37, CenterOnEdge = false };
                original.Save(settings); Require(original.SaveError == null, "Settings save");
                Preferences restored = Preferences.Load(settings);
                Require(restored.Edge == Edge.Right && restored.Monitor == "test-screen" && restored.Dark && restored.Pinned && restored.Delay == 800 && restored.FocusMinutes == 45 && restored.CodexPath == "test-codex.exe" && restored.EdgePosition == 0.37, "Preferences round trip, including drag position, focus duration and Codex path");
                original.Delay = 1200; original.Save(settings); Require(Preferences.Load(settings).Delay == 1200, "Atomic replacement of existing preferences");
                original.FocusSeconds = 91; original.Save(settings);
                Require(Preferences.Load(settings).FocusSeconds == 91, "Second precision survives restart");
                original.CenterOnEdge = true; original.Delay = 0; original.Presets = new List<int> { 10, 20, 45 }; original.Save(settings);
                restored = Preferences.Load(settings);
                Require(restored.CenterOnEdge && restored.EdgePosition == 0.5 && restored.Delay == 0 && string.Join(",", restored.Presets) == "10,20,45", "Centering, zero delay and custom presets survive restart");
                original.Presets.Clear(); original.Save(settings); Require(Preferences.Load(settings).Presets.Count == 0, "An intentionally empty preset list stays empty");
                File.WriteAllText(settings, "<Island><FocusMinutes>45</FocusMinutes></Island>");
                restored = Preferences.Load(settings); Require(restored.FocusMinutes == 45 && string.Join(",", restored.Presets) == "5,10,15,30,60,90", "Old preferences gain new defaults without changing saved focus duration");
                File.WriteAllText(settings, "<Island><Presets><Minutes>20</Minutes><Minutes>20</Minutes><Minutes>0</Minutes><Minutes>181</Minutes><Minutes>5</Minutes><Minutes>bad</Minutes></Presets></Island>");
                Require(string.Join(",", Preferences.Load(settings).Presets) == "5,20,181", "Valid long presets load while invalid and duplicate presets are ignored");
                File.WriteAllText(settings, "<Island><FocusMinutes>25</FocusMinutes><Delay>500</Delay><CenterOnEdge>false</CenterOnEdge></Island>");
                restored = Preferences.Load(settings); Require(restored.FocusSeconds == 0 && restored.Delay == 200 && restored.CenterOnEdge, "Former defaults migrate to unset time, 200 ms and centering");
                original.FocusSeconds = FocusTimeInput.MaxSeconds; original.Presets = new List<int> { 1440, int.MaxValue }; original.Save(settings);
                restored = Preferences.Load(settings); Require(restored.FocusSeconds == FocusTimeInput.MaxSeconds && restored.Presets[1] == int.MaxValue, "Long durations and presets survive restart");
                File.WriteAllText(settings, "<Island><Edge>900</Edge><Delay>999999</Delay><EdgePosition>NaN</EdgePosition></Island>");
                restored = Preferences.Load(settings); Require(restored.Edge == Edge.Top && restored.Delay == 1500 && restored.EdgePosition == 0.5, "Invalid settings must fall back or be clamped");
                File.WriteAllText(settings, "<broken"); Require(Preferences.Load(settings).Edge == Edge.Top, "Malformed settings must not prevent launch");
                new Preferences().Save(settings); restored = Preferences.Load(settings);
                Require(restored.Edge == Edge.Top && restored.Monitor == "" && restored.EdgePosition == 0.5 && !restored.Dark && !restored.Pinned && restored.CenterOnEdge && restored.Delay == 200 && restored.FocusSeconds == 0 && restored.CodexPath == "" && string.Join(",", restored.Presets) == "5,10,15,30,60,90", "Reset defaults replace previous settings and survive restart");
                File.Delete(settings);
                original.QuickApps = new List<QuickApp> { new QuickApp("Моя программа & заметки", @"C:\Apps\Music Player.exe", "brand") }; original.MusicService = "spotify"; original.Save(settings);
                restored = Preferences.Load(settings);
                Require(restored.QuickApps.Count == 1 && restored.QuickApps[0].Name == "Моя программа & заметки" && restored.QuickApps[0].Target == @"C:\Apps\Music Player.exe" && restored.MusicService == "spotify", "Custom programs and service survive restart with spaces and XML characters");
                original.QuickApps.Clear(); original.Save(settings); Require(Preferences.Load(settings).QuickApps.Count == 0, "Intentionally empty quick access stays empty");
                Require(QuickApp.IsProgram(@"C:\Apps\программа.lnk") && !QuickApp.IsProgram("https://example.com/app.exe") && !QuickApp.IsProgram(@"C:\script.cmd") && QuickApp.FromXml(new XElement("App", new XAttribute("target", "powershell.exe"), new XAttribute("name", "invalid"))) == null, "Quick access accepts programs and shortcuts, rejects unconfigured shell commands");
                Require(MusicServices.AllowedNavigation("https://accounts.spotify.com/login") && !MusicServices.AllowedNavigation("file:///C:/test") && !MusicServices.AllowedNavigation("javascript:alert(1)") && !MusicServices.AllowedNavigation("http://music.yandex.ru"), "Music navigation requires HTTPS and rejects local files or scripts");
                passed.Add("PASS: preferences, custom quick access, empty lists, service selection and music navigation policy");
                File.WriteAllLines(Path.Combine(directory, "self-test.txt"), passed.ToArray());
                return 0;
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test-error.log"), error.ToString());
                return 1;
            }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
