using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataGateWin.Configuration;
using DataGateWin.Services;

namespace DataGateWin.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly GoogleAuthService _googleAuthService;
    private CancellationTokenSource? _cts;

    public event EventHandler<string>? SignedIn;

    public LoginViewModel(GoogleAuthService googleAuthService, GoogleAuthSettings settings)
    {
        _googleAuthService = googleAuthService;

        ClientId = settings.ClientId;
        Port = settings.RedirectPort;

        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException("GoogleAuth:ClientId is missing in appsettings.json.");

        if (Port <= 0 || Port > 65535)
            throw new InvalidOperationException("GoogleAuth:RedirectPort is invalid in appsettings.json.");
    }

    public string ClientId { get; }
    public int Port { get; }

    [ObservableProperty]
    private string statusText = "Not signed in.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool isBusy;

    public bool IsNotBusy => !IsBusy;

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task SignInAsync()
    {
        IsBusy = true;
        StatusText = "Opening browser...";

        _cts = new CancellationTokenSource();

        try
        {
            var result = await _googleAuthService.SignInAsync(ClientId, Port, _cts.Token);

            if (result.IsSuccess)
            {
                StatusText = "Signed in successfully.";
                SignedIn?.Invoke(this, result.AuthorizationCode ?? "");
                return;
            }

            var desc = string.IsNullOrWhiteSpace(result.ErrorDescription) ? "" : $" ({result.ErrorDescription})";
            StatusText = $"Sign-in failed: {result.Error}{desc}";
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
