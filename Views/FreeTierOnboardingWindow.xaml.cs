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
    private readonly DispatcherTimer _countdownTimer;
    private FreeTierAccessStatusResponse _status;
    private string? _linkCode;
    private DateTimeOffset _codeExpiresAtUtc;
    private bool _isBusy;
    private bool _linkCodeExpiredNotice;

    public FreeTierOnboardingWindow(
        IFreeTierAccessApiClient api,
        FreeTierAccessStatusResponse status)
    {
        InitializeComponent();
        FluentWindowChrome.Attach(this);

        _api = api ?? throw new ArgumentNullException(nameof(api));
        _status = status ?? throw new ArgumentNullException(nameof(status));

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _pollTimer.Tick += (_, _) => _ = RefreshStatusInternalAsync(showSuccessCloseMessage: false);

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => UpdateLinkCodeCountdown();

        ApplyStatusToUi();
        Loaded += (_, _) =>
        {
            _pollTimer.Start();
            UpdateLinkCodeCountdown();
        };
        Closed += (_, _) =>
        {
            _pollTimer.Stop();
            _countdownTimer.Stop();
        };
    }

    private async void CheckAgain_OnClick(object sender, RoutedEventArgs e) =>
        await RefreshStatusInternalAsync(showSuccessCloseMessage: true);

    private async void PrimaryAction_OnClick(object sender, RoutedEventArgs e)
    {
        var mode = FreeTierOnboardingPolicy.GetCopyMode(_status);
        if (mode == FreeTierOnboardingCopyMode.LinkAccount && _linkCode == null)
        {
            await RequestCodeInternalAsync();
            return;
        }

        if (_linkCode != null)
        {
            OpenTelegramUrl(FreeTierOnboardingPolicy.DefaultTelegramBotUrl);
            return;
        }

        OpenTelegramUrl(FreeTierOnboardingPolicy.ToTelegramChannelUrl(_status.RequiredChannel));
    }

    private void OpenChannel_OnClick(object sender, RoutedEventArgs e) =>
        OpenTelegramUrl(FreeTierOnboardingPolicy.ToTelegramChannelUrl(_status.RequiredChannel));

    private void CopyCode_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_linkCode))
            return;

        try
        {
            Clipboard.SetText(_linkCode);
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "FreeTierOnboardingWindow.CopyCode");
        }
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
                ShowError(Loc.T("FreeTierOnboarding_StatusRefreshFailed"));
                return;
            }

            _status = updated;
            ApplyStatusToUi();

            if (!FreeTierOnboardingPolicy.ShouldShow(_status))
            {
                DialogResult = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "FreeTierOnboardingWindow.RefreshStatus");
            ShowError(Loc.T("FreeTierOnboarding_StatusRefreshFailed"));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RequestCodeInternalAsync()
    {
        if (_isBusy || !_status.CanRequestAccountLinkCode)
            return;

        SetBusy(true);
        _linkCodeExpiredNotice = false;
        try
        {
            var resp = await _api.RequestAccountLinkCodeAsync(CancellationToken.None);
            if (resp.Data == null || string.IsNullOrWhiteSpace(resp.Data.Code))
            {
                ShowError(resp.Message ?? Loc.T("FreeTierOnboarding_CodeRequestFailed"));
                return;
            }

            _linkCode = resp.Data.Code.Trim();
            _codeExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(resp.Data.ExpiresInSeconds);
            ClearError();
            ApplyStatusToUi();
            UpdateLinkCodeCountdown();
            _countdownTimer.Start();
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "FreeTierOnboardingWindow.RequestCode");
            ShowError(Loc.T("FreeTierOnboarding_CodeRequestFailed"));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyStatusToUi()
    {
        var channelLabel = FreeTierOnboardingPolicy.ResolveChannelLabel(_status);
        var copyMode = FreeTierOnboardingPolicy.GetCopyMode(_status);

        var titleKey = copyMode == FreeTierOnboardingCopyMode.LinkAccount
            ? "FreeTierOnboarding_TitleLink"
            : "FreeTierOnboarding_TitleSubscribe";
        var title = Loc.T(titleKey);
        Title = title;
        WindowTitleBar.Title = title;

        BodyText.Text = copyMode switch
        {
            FreeTierOnboardingCopyMode.LinkAccount => Loc.T("FreeTierOnboarding_BodyLink", channelLabel),
            FreeTierOnboardingCopyMode.SubscribeOnly => Loc.T("FreeTierOnboarding_BodySubscribeOnly", channelLabel),
            _ => Loc.T("FreeTierOnboarding_BodyGeneric", channelLabel),
        };

        OpenChannelButton.Visibility = copyMode == FreeTierOnboardingCopyMode.Generic
            ? Visibility.Collapsed
            : Visibility.Visible;

        CodeExpiredText.Visibility = _linkCodeExpiredNotice && _linkCode == null
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_linkCode != null)
        {
            LinkCodeBorder.Visibility = Visibility.Visible;
            LinkCodeText.Text = _linkCode;
            LinkCodeStepsText.Text = Loc.T("FreeTierOnboarding_CodeStepsWithVpn", _linkCode);
            PrimaryActionButton.Content = Loc.T("FreeTierOnboarding_OpenBot");
        }
        else
        {
            LinkCodeBorder.Visibility = Visibility.Collapsed;
            PrimaryActionButton.Content = copyMode == FreeTierOnboardingCopyMode.LinkAccount
                ? Loc.T("FreeTierOnboarding_GetCode")
                : Loc.T("FreeTierOnboarding_OpenChannel");
        }

        PrimaryActionButton.IsEnabled = !_isBusy &&
            (copyMode != FreeTierOnboardingCopyMode.LinkAccount || _status.CanRequestAccountLinkCode || _linkCode != null);
    }

    private void UpdateLinkCodeCountdown()
    {
        if (_linkCode == null || _codeExpiresAtUtc == default)
        {
            _countdownTimer.Stop();
            LinkCodeExpiresText.Text = "";
            LinkCodeExpiresSoonText.Visibility = Visibility.Collapsed;
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (FreeTierOnboardingPolicy.IsLinkCodeExpired(_codeExpiresAtUtc, now))
        {
            _linkCode = null;
            _codeExpiresAtUtc = default;
            _linkCodeExpiredNotice = true;
            _countdownTimer.Stop();
            ApplyStatusToUi();
            return;
        }

        var secondsLeft = (int)Math.Ceiling((_codeExpiresAtUtc - now).TotalSeconds);
        LinkCodeExpiresText.Text = Loc.T(
            "FreeTierOnboarding_CodeExpires",
            FreeTierOnboardingPolicy.FormatCountdown(secondsLeft));

        if (FreeTierOnboardingPolicy.ShouldWarnLinkCodeExpiringSoon(secondsLeft))
        {
            LinkCodeExpiresSoonText.Text = Loc.T(
                "FreeTierOnboarding_CodeExpiresSoon",
                FreeTierOnboardingPolicy.FormatCountdown(secondsLeft));
            LinkCodeExpiresSoonText.Visibility = Visibility.Visible;
        }
        else
        {
            LinkCodeExpiresSoonText.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        ErrorText.Text = "";
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        CheckAgainButton.IsEnabled = !busy;
        OpenChannelButton.IsEnabled = !busy;
        CopyCodeButton.IsEnabled = !busy && _linkCode != null;
        ApplyStatusToUi();
    }

    private void OpenTelegramUrl(string url)
    {
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
            CrashReporter.ReportNonFatal(ex, "FreeTierOnboardingWindow.OpenTelegramUrl");
        }
    }
}
