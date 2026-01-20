using System;
using System.Windows;
using DataGateWin.Configuration;
using DataGateWin.Services;
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

        _authState = authState;

        var settings = App.AppConfiguration
                           .GetSection("GoogleAuth")
                           .Get<GoogleAuthSettings>()
                       ?? throw new InvalidOperationException("GoogleAuth settings are missing.");

        var loopback = new GoogleAuthLoopback();
        var authService = new GoogleAuthService(loopback);

        var vm = new LoginViewModel(authService, settings);
        vm.SignedIn += (_, code) =>
        {
            _authState.SetAuthorized(code);

            var main = new MainWindow(_authState);
            Application.Current.MainWindow = main;
            main.Show();

            Close();
        };

        DataContext = vm;
    }
}