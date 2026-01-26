using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataGateWin.Services.Auth;

namespace DataGateWin.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AuthStateStore _authState;

    public MainViewModel(AuthStateStore authState)
    {
        _authState = authState;

        StatusText = _authState.IsAuthorized
            ? "Authorized. Ready to connect."
            : "Not authorized. Please sign in first.";
    }

    [ObservableProperty]
    private string statusText;

    [RelayCommand]
    private void Connect()
    {
        // TODO: connect logic will be added later
        StatusText = "Connect action is not implemented yet.";
    }
}