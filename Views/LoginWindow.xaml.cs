using System.Windows;
using DataGateWin.Configuration;
using DataGateWin.Services.Auth;
using DataGateWin.ViewModels;
using Microsoft.Extensions.Configuration;
using Wpf.Ui.Controls;

namespace DataGateWin.Views;

public partial class LoginWindow : FluentWindow
{
    private readonly AuthStateStore _authState;

    public LoginWindow(AuthStateStore authState)
    {
        InitializeComponent();

        _authState = authState ?? throw new ArgumentNullException(nameof(authState));

        var googleSettings = App.AppConfiguration
                                 .GetSection("GoogleAuth")
                                 .Get<GoogleAuthSettings>()
                             ?? throw new InvalidOperationException("GoogleAuth settings are missing.");

        var apiSettings = App.AppConfiguration
                              .GetSection("Api")
                              .Get<ApiSettings>()
                          ?? throw new InvalidOperationException("Api settings are missing.");

        var vm = new LoginViewModel(App.GoogleAuth, App.Session, googleSettings, apiSettings);

        vm.SignedIn += (_, accessToken) =>
        {
            _authState.SetAuthorized(accessToken);

            var main = new MainWindow(_authState);
            Application.Current.MainWindow = main;
            main.Show();

            Close();
        };

        DataContext = vm;
    }
}