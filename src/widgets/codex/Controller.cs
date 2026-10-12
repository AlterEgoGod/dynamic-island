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
        private async void RefreshLimits()
        {
            if (smoke || disposed || !WidgetEnabled("codex") || limitsBusy || loginLaunching || limitsClient.LoggingIn) return;
            limitsBusy = true; Find<Button>("LimitsRefreshButton").IsEnabled = false;
            Find<TextBlock>("LimitsStatus").Text = "Обновляем лимиты…";
            try
            {
                LimitsSnapshot snapshot = await limitsClient.Read();
                if (disposed) return;
                limits = snapshot; limitsError = snapshot.HasData ? "" : "Codex не вернул квоту для этих периодов.";
            }
            catch (Exception error)
            {
                if (disposed) return;
                limitsError = error is TimeoutException || error is InvalidOperationException ? error.Message : "Не удалось обновить. Проверьте вход и подключение.";
            }
            finally
            {
                limitsBusy = false;
                if (!disposed) { Find<Button>("LimitsRefreshButton").IsEnabled = true; RenderLimits(); }
            }
        }

        private void RenderLimits()
        {
            RenderQuota("FiveHour", limits == null ? null : limits.FiveHour);
            RenderQuota("Weekly", limits == null ? null : limits.Weekly);
            QuotaWindow main = limits == null ? null : limits.MainQuota;
            string period = limits != null && limits.WeeklyPrimary ? "неделя" : "5 ч";
            Find<TextBlock>("QuotaBadge").Text = "Codex · " + period + " · " + (main == null ? "—" : main.RemainingPercent.ToString("0.#", CultureInfo.GetCultureInfo("ru-RU")) + "%");
            string status;
            if (!string.IsNullOrEmpty(limitsError)) status = limitsError + (limits != null && limits.HasData ? " Данные от " + limits.UpdatedAt.ToLocalTime().ToString("HH:mm") + "." : "");
            else if (limits == null) status = "Лимиты ещё не получены.";
            else status = "Остаток квоты · обновлено " + limits.UpdatedAt.ToLocalTime().ToString("HH:mm:ss") + " · авто 30 с";
            Find<TextBlock>("LimitsStatus").Text = status;
            Find<TextBlock>("LimitsStatus").ToolTip = status;
            if (!loginLaunching && !limitsClient.LoggingIn)
                Find<TextBlock>("SettingsLoginStatus").Text = !string.IsNullOrEmpty(limitsError) ? limitsError : limits != null && limits.HasData ? "Аккаунт подключён. Лимиты обновляются автоматически." : "Войдите через ChatGPT, чтобы получать лимиты аккаунта.";
            UpdateLoginButtons();
            Find<Button>("LimitsLoginButton").Visibility = !string.IsNullOrEmpty(limitsError) ? Visibility.Visible : Visibility.Collapsed;
            Find<Border>("LimitsCard").ToolTip = limits == null ? "Лимиты аккаунта Codex через ChatGPT" : "Тариф: " + limits.PlanLabel;
        }
        private void RenderQuota(string name, QuotaWindow quota)
        {
            Find<TextBlock>(name + "Value").Text = quota == null ? "—" : quota.RemainingPercent.ToString("0.#", CultureInfo.GetCultureInfo("ru-RU")) + "%";
            Find<TextBlock>(name + "Reset").Text = quota == null || !quota.ResetsAt.HasValue ? "Сброс: —" : "Сброс: " + quota.ResetsAt.Value.ToLocalTime().ToString("d MMM, HH:mm", CultureInfo.GetCultureInfo("ru-RU"));
            ProgressBar progress = Find<ProgressBar>(name + "Bar");
            double target = quota == null ? 0 : quota.RemainingPercent;
            progress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, new DoubleAnimation(progress.Value, target, TimeSpan.FromMilliseconds(400)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
            progress.ToolTip = quota == null ? "Нет данных" : "Осталось " + Find<TextBlock>(name + "Value").Text;
        }
        private async void LoginCodex()
        {
            if (smoke || disposed || loginLaunching) return;
            loginLaunching = true; UpdateLoginButtons();
            SetLoginStatus("Открываем вход через ChatGPT…");
            try
            {
                if (!limitsClient.LoggingIn || string.IsNullOrEmpty(loginUrl)) loginUrl = await limitsClient.BeginLogin();
                if (!disposed)
                {
                    Process.Start(new ProcessStartInfo(loginUrl) { UseShellExecute = true });
                    SetLoginStatus("Завершите вход в браузере. Лимиты обновятся автоматически.");
                }
            }
            catch (Exception error)
            {
                if (!disposed) SetLoginStatus(error is InvalidOperationException || error is TimeoutException ? error.Message : "Не удалось открыть браузер. Повторите вход.");
            }
            finally { loginLaunching = false; if (!disposed) UpdateLoginButtons(); }
        }
        private void SetLoginStatus(string status)
        {
            Find<TextBlock>("SettingsLoginStatus").Text = status;
            Find<TextBlock>("LimitsStatus").Text = status;
        }
        private void UpdateLoginButtons()
        {
            Find<Button>("LimitsLoginButton").IsEnabled = Find<Button>("SettingsLoginButton").IsEnabled = !loginLaunching;
            Find<Button>("SettingsLoginButton").Content = loginLaunching ? "Открываем…" : limitsClient.LoggingIn ? "Продолжить вход" : "Войти через браузер";
        }
        private void OnLoginCompleted()
        {
            if (!disposed) Window.Dispatcher.BeginInvoke(new Action(delegate
            {
                if (disposed) return;
                loginUrl = ""; UpdateLoginButtons(); SetLoginStatus("Вход выполнен. Обновляем лимиты…"); RefreshLimits();
            }));
        }
    }
}
