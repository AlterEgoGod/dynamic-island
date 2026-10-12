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
        private void BeginFocusEdit()
        {
            if (editingFocus) return;
            editingFocus = true; focusInputInvalid = false;
            originalFocusText = Find<TextBox>("TimerLabel").Text;
            Find<TextBlock>("TimerHint").Text = "Минуты, мин:сек или ч:мин:сек";
        }
        private void CancelFocusEdit()
        {
            editingFocus = false; focusInputInvalid = false; UpdateClock();
        }
        private bool CommitFocusEdit()
        {
            if (!editingFocus) return true;
            string text = Find<TextBox>("TimerLabel").Text.Trim();
            if (text == originalFocusText) { CancelFocusEdit(); return true; }
            if (text.Length == 0) { SetFocusSeconds(0); return true; }
            long seconds;
            if (!FocusTimeInput.TryParse(text, out seconds))
            {
                focusInputInvalid = true; Find<TextBlock>("TimerHint").Text = "Введите минуты, мин:сек или ч:мин:сек";
                return false;
            }
            SetFocusSeconds(seconds);
            return true;
        }
        private void SetFocusMinutes(int minutes) { SetFocusSeconds((long)minutes * 60); }
        private void SetFocusSeconds(long seconds)
        {
            timerCue.Stop(); focusInputInvalid = false; editingFocus = false;
            Find<TextBlock>("Footer").Text = "Перемещайте остров за ручку сверху";
            focus.SetSeconds(seconds); preferences.FocusSeconds = seconds; Save(); UpdateClock();
        }
        private void FitFocusReadout()
        {
            TextBox input = Find<TextBox>("TimerLabel");
            double available = input.ActualWidth > 0 ? input.ActualWidth : 130;
            FormattedText text = new FormattedText(input.Text.Length == 0 ? "00:00" : input.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(input.FontFamily, input.FontStyle, input.FontWeight, input.FontStretch), 28, Brushes.Black, VisualTreeHelper.GetDpi(input).PixelsPerDip);
            input.FontSize = Math.Max(12, Math.Min(28, 28 * (available - 4) / Math.Max(1, text.WidthIncludingTrailingWhitespace)));
        }
        private void BuildPresets()
        {
            WrapPanel quick = Find<WrapPanel>("FocusPresets");
            System.Windows.Controls.Primitives.UniformGrid editor = Find<System.Windows.Controls.Primitives.UniformGrid>("PresetEditor");
            quick.Children.Clear(); editor.Children.Clear(); presetButtons.Clear();
            foreach (int minutes in preferences.Presets)
            {
                int selected = minutes;
                Button preset = new Button { Content = minutes.ToString(), Tag = minutes, MinWidth = 27, Height = 28, Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(0, 0, 2, 3), FontSize = 11, ToolTip = minutes + " минут", Style = (Style)Window.Resources["PresetButton"] };
                System.Windows.Automation.AutomationProperties.SetName(preset, "Таймер " + minutes + " минут");
                preset.Click += delegate { SetFocusMinutes(selected); };
                quick.Children.Add(preset); presetButtons.Add(preset);
                Grid row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = minutes + " мин", FontSize = 12, Margin = new Thickness(10, 0, 2, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = minutes + " минут" });
                Button remove = new Button { Tag = minutes, Width = 30, Height = 32, Padding = new Thickness(7), ToolTip = "Удалить шаблон " + minutes + " мин", Content = new ContentControl { Content = Icons.Load("close"), Width = 16, Height = 16 } };
                System.Windows.Automation.AutomationProperties.SetName(remove, "Удалить шаблон " + minutes + " минут");
                remove.Click += delegate { RemovePreset(selected); };
                Grid.SetColumn(remove, 1); row.Children.Add(remove);
                Border chip = new Border { Child = row, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 6, 6) };
                chip.SetResourceReference(Border.BackgroundProperty, "Surface"); editor.Children.Add(chip);
            }
            UpdatePresetAppearance();
        }
        private void AddPreset()
        {
            int minutes;
            if (!int.TryParse(Find<TextBox>("NewPresetMinutes").Text.Trim(), out minutes) || minutes < 1)
            {
                Find<TextBlock>("PresetValidation").Text = "Введите целое положительное число минут."; return;
            }
            if (preferences.Presets.Contains(minutes)) { Find<TextBlock>("PresetValidation").Text = "Такой шаблон уже есть."; return; }
            preferences.Presets.Add(minutes); preferences.Presets.Sort(); Save(); BuildPresets();
            Find<TextBox>("NewPresetMinutes").Clear(); Find<Button>("AddPreset").Focus();
            Find<TextBlock>("PresetValidation").Text = "Добавлено: " + minutes + " мин. Шаблон доступен в карточке таймера.";
        }
        private void RemovePreset(int minutes)
        {
            preferences.Presets.Remove(minutes); Save(); BuildPresets();
            Find<TextBlock>("PresetValidation").Text = preferences.Presets.Count == 0 ? "Шаблонов пока нет. Добавьте время ниже." : "Шаблон удалён. Текущий отсчёт продолжается.";
        }
        private void UpdatePresetAppearance()
        {
            foreach (Button button in presetButtons)
            {
                int minutes = (int)button.Tag;
                button.SetResourceReference(Control.BackgroundProperty, (long)minutes * 60 == preferences.FocusSeconds ? "Surface" : "Card");
                button.SetResourceReference(Control.ForegroundProperty, (long)minutes * 60 == preferences.FocusSeconds ? "Accent" : "Ink");
            }
        }
        private void UpdateTimerDetails()
        {
            DateTime now = DateTime.UtcNow;
            double remaining = focus.Remaining(now).TotalSeconds;
            Find<ProgressBar>("TimerProgress").Value = focus.Progress(now);
            Find<Button>("TimerRestart").IsEnabled = focus.Duration > TimeSpan.Zero && (focus.Running || remaining < focus.Duration.TotalSeconds);
            Find<Button>("TimerAddMinute").IsEnabled = focus.Duration > TimeSpan.Zero && focus.Duration.TotalSeconds <= FocusTimeInput.MaxSeconds - 60;
            string action = focus.Running ? "Приостановить" : remaining <= 0 && focus.Duration > TimeSpan.Zero ? "Повторить" : remaining < focus.Duration.TotalSeconds ? "Продолжить" : "Запустить";
            Find<Button>("TimerButton").ToolTip = action + " таймер";
            System.Windows.Automation.AutomationProperties.SetName(Find<Button>("TimerButton"), action + " таймер");
            if (!editingFocus && !focusInputInvalid && focus.Running)
            {
                DateTime end = DateTime.Now.AddSeconds(remaining);
                Find<TextBlock>("TimerHint").Text = "До " + end.ToString(end.Date == DateTime.Today ? "HH:mm:ss" : "d MMM, HH:mm") + " · " + Math.Floor(focus.Progress(now) * 100) + "%";
            }
        }
        private void UpdateFocusClock()
        {
            bool finished = focus.Tick(DateTime.UtcNow);
            if (finished)
            {
                if (tray != null) { tray.BalloonTipTitle = "Время для перерыва"; tray.BalloonTipText = "Таймер " + FocusTimeInput.Format(preferences.FocusSeconds) + " завершён. Можно немного отдохнуть."; tray.ShowBalloonTip(5000); }
                Find<TextBlock>("Footer").Text = "Отличная работа. Время отдохнуть.";
                if (!smoke) timerCue.Play();
            }
            long seconds = (long)Math.Ceiling(focus.Remaining(DateTime.UtcNow).TotalSeconds);
            if (!editingFocus) Find<TextBox>("TimerLabel").Text = focus.Duration == TimeSpan.Zero ? "" : FocusTimeInput.Format(seconds);
            if (!focusInputInvalid && !editingFocus) Find<TextBlock>("TimerHint").Text = focus.Duration == TimeSpan.Zero ? "Выберите время для таймера" : focus.Running ? "Идёт таймер" : seconds == (long)focus.Duration.TotalSeconds ? "Нажмите на время для изменения" : seconds == 0 ? "Время для перерыва" : "На паузе";
            UpdatePresetAppearance();
            if (timerShowingPause != focus.Running)
            {
                timerShowingPause = focus.Running;
                Find<ContentControl>("TimerIcon").Content = Icons.Load(focus.Running ? "pause" : "play");
            }
            UpdateTimerDetails();
            if (tray != null) tray.Text = focus.Running ? "Dynamic Island · таймер " + FocusTimeInput.Format(seconds) : "Dynamic Island · " + Find<TextBlock>("QuotaBadge").Text;
        }

    }
}
