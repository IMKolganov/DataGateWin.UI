using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using DataGateMonitor.SharedModels.DataGateMonitor.Auth.Requests;
using DataGateMonitor.SharedModels.DataGateMonitor.Auth.Responses;
using DataGateWin.CrashReporting;
using DataGateWin.Services.Auth;
using DataGateWin.Services.Ui;

namespace DataGateWin.Views;

public partial class FreeTierOnboardingWindow
{
    private readonly IFreeTierAccessApiClient _api;
    private readonly DispatcherTimer _pollTimer;
    private FreeTierAccessStatusResponse _status;
    private bool _isBusy;
    private bool _allowRequestLinkCode;

    public FreeTierOnboardingWindow(
        IFreeTierAccessApiClient api,
        FreeTierAccessStatusResponse status)
    {
        InitializeComponent();
        FluentWindowChrome.Attach(this);

        _api = api ?? throw new ArgumentNullException(nameof(api));
        _status = status ?? throw new ArgumentNullException(nameof(status));

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _pollTimer.Tick += PollTimer_OnTick;

        ApplyStatusToUi();
        Loaded += (_, _) => _pollTimer.Start();
        Closed += (_, _) => _pollTimer.Stop();
    }

    private async void PollTimer_OnTick(object? sender, EventArgs e)
    {
        if (_isBusy)
            return;

        await RefreshStatusInternalAsync(showSuccessCloseMessage: false);
    }

    private async void RefreshStatus_OnClick(object sender, RoutedEventArgs e)
    {
        await RefreshStatusInternalAsync(showSuccessCloseMessage: true);
    }

    private async Task RefreshStatusInternalAsync(bool showSuccessCloseMessage)
    {
        if (_isBusy)
            return;

        SetBusy(true);
        try
        {
            var resp = await _api.GetStatusAsync(CancellationToken.None);
            var updated = resp.Data;
            if (updated == null)
            {
                StatusText.Text = "Could not refresh compliance status. Try again.";
                return;
            }

            _status = updated;
            ApplyStatusToUi();

            if (!FreeTierOnboardingPolicy.ShouldShow(_status))
            {
                if (showSuccessCloseMessage)
                    StatusText.Text = "Compliance confirmed. Closing onboarding...";

                DialogResult = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "FreeTierOnboardingWindow.RefreshStatus");
            StatusText.Text = "Status refresh failed. Please try again.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RequestCode_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        if (!_allowRequestLinkCode)
        {
            StatusText.Text = "Link-code request is not available for this account yet.";
            return;
        }

        if (!long.TryParse(TelegramIdTextBox.Text?.Trim(), out var telegramId) || telegramId <= 0)
        {
            StatusText.Text = "Enter a valid numeric Telegram ID.";
            return;
        }

        SetBusy(true);
        try
        {
            var resp = await _api.RequestAccountLinkCodeAsync(
                new RequestTelegramAccountLinkCodeRequest
                {
                    TelegramId = telegramId
                },
                CancellationToken.None);

            if (resp.Data == null || string.IsNullOrWhiteSpace(resp.Data.Code))
            {
                StatusText.Text = "Code request failed. Try again.";
                return;
            }

            LinkCodeText.Text = resp.Data.Code.Trim();
            LinkCodeExpiresText.Text = $"Expires in {resp.Data.ExpiresInSeconds} seconds.";
            LinkCodeBorder.Visibility = Visibility.Visible;
            StatusText.Text = "Send /link_account CODE in the Telegram bot private chat.";
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "FreeTierOnboardingWindow.RequestCode");
            StatusText.Text = "Failed to request link code. Ensure Telegram is registered in the bot.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OpenChannel_OnClick(object sender, RoutedEventArgs e)
    {
        var url = ToTelegramChannelUrl(_status.RequiredChannel);
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "FreeTierOnboardingWindow.OpenChannel");
        }
    }

    private void ApplyStatusToUi()
    {
        PlanText.Text = $"Plan: {_status.ActivePlanName ?? "Free/Default"}";
        RequiredChannelText.Text = $"Subscribe to {_status.RequiredChannel ?? "@DataGateVPNBot"} or link your Telegram account.";
        OpenChannelButton.Content = $"Open {_status.RequiredChannel ?? "required channel"}";
        _allowRequestLinkCode = _status.CanRequestAccountLinkCode;
        RequestCodeButton.IsEnabled = !_isBusy && _allowRequestLinkCode;

        if (_status.IsMergedAccount)
        {
            StatusText.Text = "Account merge detected. Waiting for compliance update...";
            return;
        }

        if (_status.IsChannelSubscribed)
        {
            StatusText.Text = "Channel subscription detected. Waiting for compliance update...";
            return;
        }

        if (!_status.CanRequestAccountLinkCode)
            StatusText.Text = "Link-code request is currently unavailable for this account.";
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        RequestCodeButton.IsEnabled = !busy && _allowRequestLinkCode;
        TelegramIdTextBox.IsEnabled = !busy;
        OpenChannelButton.IsEnabled = !busy;
    }

    private static string ToTelegramChannelUrl(string? requiredChannel)
    {
        if (string.IsNullOrWhiteSpace(requiredChannel))
            return "";

        var value = requiredChannel.Trim();
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return value;

        if (value.StartsWith("@", StringComparison.Ordinal))
            value = value[1..];

        return $"https://t.me/{value}";
    }
}
