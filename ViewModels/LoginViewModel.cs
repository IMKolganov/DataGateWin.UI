using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataGateMonitor.SharedModels.DataGateMonitor.Auth.Requests;
using DataGateMonitor.SharedModels.DataGateMonitor.Auth.Responses;
using DataGateWin.Configuration;
using DataGateWin.CrashReporting;
using DataGateWin.Localization;
using DataGateWin.Services.Auth;

namespace DataGateWin.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly GoogleAuthService _googleAuthService;
    private readonly AuthApiClient _authApi;
    private readonly AuthSession _session;
    private readonly string _apiBaseUrl;
    private CancellationTokenSource? _cts;
    private string? _loginChallengeId;

    public event EventHandler<string>? SignedIn;

    public LoginViewModel(
        GoogleAuthService googleAuthService,
        AuthApiClient authApi,
        AuthSession session,
        GoogleAuthSettings googleSettings,
        ApiSettings apiSettings)
    {
        _googleAuthService = googleAuthService ?? throw new ArgumentNullException(nameof(googleAuthService));
        _authApi = authApi ?? throw new ArgumentNullException(nameof(authApi));
        _session = session ?? throw new ArgumentNullException(nameof(session));

        ClientId = googleSettings.ClientId;
        Port = googleSettings.RedirectPort;
        _apiBaseUrl = apiSettings.BaseUrl;

        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException("GoogleAuth:ClientId is missing.");

        if (Port <= 0 || Port > 65535)
            throw new InvalidOperationException("GoogleAuth:RedirectPort is invalid.");

        if (string.IsNullOrWhiteSpace(_apiBaseUrl))
            throw new InvalidOperationException("Api:BaseUrl is missing.");

        StatusText = Loc.T("Login_Status_NotSignedIn");
    }

    public string ClientId { get; }
    public int Port { get; }

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyPropertyChangedFor(nameof(IsGoogleSignInVisible))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifyTotpCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackFromTotpCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGoogleSignInVisible))]
    [NotifyCanExecuteChangedFor(nameof(VerifyTotpCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackFromTotpCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private bool _isTotpChallengeVisible;

    [ObservableProperty]
    private string _totpLeadText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyTotpCommand))]
    private string _totpCode = "";

    public bool IsNotBusy => !IsBusy;
    public bool IsGoogleSignInVisible => !IsTotpChallengeVisible;

    private bool CanSignIn => IsNotBusy && !IsTotpChallengeVisible;
    private bool CanVerifyTotp => IsNotBusy && IsTotpChallengeVisible && TotpCode.Trim().Length >= 6;
    private bool CanBackFromTotp => IsNotBusy && IsTotpChallengeVisible;

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        IsBusy = true;
        StatusText = Loc.T("Login_Status_OpeningBrowser");
        _cts = new CancellationTokenSource();

        try
        {
            StatusText = Loc.T("Login_Status_WaitingGoogle");
            var apiResponse = await _googleAuthService.SignInAndLoginAsync(
                ClientId, Port, _apiBaseUrl, _cts.Token);

            if (!apiResponse.Success || apiResponse.Data == null)
            {
                StatusText = Loc.T("Login_Status_Failed");
                return;
            }

            await CompleteLoginAsync(apiResponse.Data, _cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.T("Login_Status_Cancelled");
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "LoginViewModel.SignIn");
            StatusText = Loc.T("Login_Status_FailedFmt", ex.Message);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanVerifyTotp))]
    private async Task VerifyTotpAsync()
    {
        var challengeId = _loginChallengeId;
        if (string.IsNullOrWhiteSpace(challengeId))
            return;

        var code = TotpCode.Trim();
        if (code.Length < 6)
        {
            StatusText = Loc.T("Login_Totp_Error_CodeRequired");
            return;
        }

        IsBusy = true;
        StatusText = Loc.T("Login_Totp_Status_Verifying");
        _cts = new CancellationTokenSource();

        try
        {
            var apiResponse = await _authApi.TotpVerifyLoginAsync(
                new TotpVerifyLoginRequest { LoginChallengeId = challengeId, Code = code },
                _cts.Token).ConfigureAwait(true);

            if (!apiResponse.Success || apiResponse.Data == null)
            {
                StatusText = Loc.T("Login_Status_Failed");
                return;
            }

            await CompleteLoginAsync(apiResponse.Data, _cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.T("Login_Status_Cancelled");
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "LoginViewModel.VerifyTotp");
            StatusText = LoginFlow.IsLoginChallengeExpiredMessage(ex.Message)
                ? Loc.T("Login_Totp_ChallengeExpired")
                : Loc.T("Login_Status_FailedFmt", ex.Message);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBackFromTotp))]
    private void BackFromTotp()
    {
        ClearTotpChallenge();
        StatusText = Loc.T("Login_Status_NotSignedIn");
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _cts?.Cancel();

    private async Task CompleteLoginAsync(GoogleLoginResponse data, CancellationToken ct)
    {
        switch (LoginFlow.Resolve(data))
        {
            case ResolvedLoginFlow.TotpChallenge challenge:
                EnterTotpChallenge(challenge.LoginChallengeId, challenge.DisplayName);
                StatusText = Loc.T("Login_Totp_Status_EnterCode");
                break;
            case ResolvedLoginFlow.Tokens tokens:
                await _session.SetFromLoginAsync(tokens.Response, ct).ConfigureAwait(true);
                ClearTotpChallenge();
                StatusText = Loc.T("Login_Status_SignedInFmt", tokens.Response.DisplayName);
                SignedIn?.Invoke(this, tokens.Response.Token ?? "");
                break;
        }
    }

    private void EnterTotpChallenge(string loginChallengeId, string? displayName)
    {
        _loginChallengeId = loginChallengeId;
        TotpCode = "";
        TotpLeadText = string.IsNullOrWhiteSpace(displayName)
            ? Loc.T("Login_Totp_Lead")
            : Loc.T("Login_Totp_LeadNamedFmt", displayName);
        IsTotpChallengeVisible = true;
    }

    private void ClearTotpChallenge()
    {
        _loginChallengeId = null;
        TotpCode = "";
        TotpLeadText = "";
        IsTotpChallengeVisible = false;
    }

    partial void OnTotpCodeChanged(string value) => VerifyTotpCommand.NotifyCanExecuteChanged();
}
