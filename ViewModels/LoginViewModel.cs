using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataGateWin.Configuration;
using DataGateWin.Services.Auth;

namespace DataGateWin.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly GoogleAuthService _googleAuthService;
    private readonly AuthSession _session;
    private readonly string _apiBaseUrl;
    private CancellationTokenSource? _cts;

    public event EventHandler<string>? SignedIn;

    public LoginViewModel(
        GoogleAuthService googleAuthService,
        AuthSession session,
        GoogleAuthSettings googleSettings,
        ApiSettings apiSettings)
    {
        _googleAuthService = googleAuthService ?? throw new ArgumentNullException(nameof(googleAuthService));
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
    }

    public string ClientId { get; }
    public int Port { get; }

    [ObservableProperty]
    private string _statusText = "Not signed in.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task SignInAsync()
    {
        IsBusy = true;
        StatusText = "Opening browser...";

        _cts = new CancellationTokenSource();

        try
        {
            StatusText = "Waiting for Google sign-in...";

            var apiResponse = await _googleAuthService.SignInAndLoginAsync(
                ClientId,
                Port,
                _apiBaseUrl,
                _cts.Token);

            if (!apiResponse.Success || apiResponse.Data == null)
            {
                StatusText = "Sign-in failed.";
                return;
            }

            await _session.SetFromLoginAsync(apiResponse.Data, _cts.Token);

            StatusText = $"Signed in as {apiResponse.Data.DisplayName}.";
            SignedIn?.Invoke(this, apiResponse.Data.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Sign-in cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Sign-in failed: {ex.Message}";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel()
    {
        _cts?.Cancel();
    }
}
