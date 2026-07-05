using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using DataGateMonitor.SharedModels.DataGateMonitor.Auth.Responses;
using DataGateWin.CrashReporting;
using DataGateWin.Localization;
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
                StatusText.Text = Loc.T("FreeTierOnboarding_StatusRefreshFailed");
                return;
            }

            _status = updated;
            ApplyStatusToUi();

            if (!FreeTierOnboardingPolicy.ShouldShow(_status))
            {
                if (showSuccessCloseMessage)
                    StatusText.Text = Loc.T("FreeTierOnboarding_ComplianceConfirmed");

                DialogResult = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "FreeTierOnboardingWindow.RefreshStatus");
            StatusText.Text = Loc.T("FreeTierOnboarding_StatusRefreshFailed");
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
            StatusText.Text = Loc.T("FreeTierOnboarding_LinkNotAvailable");
            return;
        }

        SetBusy(true);
        try
        {
            var resp = await _api.RequestAccountLinkCodeAsync(CancellationToken.None);

            if (resp.Data == null || string.IsNullOrWhiteSpace(resp.Data.Code))
            {
                StatusText.Text = Loc.T("FreeTierOnboarding_CodeRequestFailed");
                return;
            }

            LinkCodeText.Text = resp.Data.Code.Trim();
            LinkCodeExpiresText.Text = Loc.T("FreeTierOnboarding_ExpiresFmt", resp.Data.ExpiresInSeconds);
            LinkCodeBorder.Visibility = Visibility.Visible;
            StatusText.Text = Loc.T("FreeTierOnboarding_CodeSentHint");
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "FreeTierOnboardingWindow.RequestCode");
            StatusText.Text = Loc.T("FreeTierOnboarding_RequestFailedEnsureRegistered");
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
        var planName = _status.ActivePlanName ?? Loc.T("FreeTierOnboarding_DefaultPlanName");
        var channelName = _status.RequiredChannel ?? "@DataGateVPNBot";
        PlanText.Text = Loc.T("FreeTierOnboarding_PlanFmt", planName);
        RequiredChannelText.Text = Loc.T("FreeTierOnboarding_RequiredChannelFmt", channelName);
        OpenChannelButton.Content = Loc.T("FreeTierOnboarding_OpenChannelFmt", channelName);
        _allowRequestLinkCode = _status.CanRequestAccountLinkCode;
        RequestCodeButton.IsEnabled = !_isBusy && _allowRequestLinkCode;

        if (_status.IsMergedAccount)
        {
            StatusText.Text = Loc.T("FreeTierOnboarding_MergeDetected");
            return;
        }

        if (_status.IsChannelSubscribed)
        {
            StatusText.Text = Loc.T("FreeTierOnboarding_ChannelDetected");
            return;
        }

        if (!_status.CanRequestAccountLinkCode)
            StatusText.Text = Loc.T("FreeTierOnboarding_LinkNotAvailable");
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        RequestCodeButton.IsEnabled = !busy && _allowRequestLinkCode;
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
