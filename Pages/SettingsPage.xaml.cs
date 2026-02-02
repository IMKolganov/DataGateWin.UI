using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DataGateWin.Configuration;
using DataGateWin.Services;
using DataGateWin.Services.Auth;
using DataGateWin.Views;
using Wpf.Ui.Appearance;

namespace DataGateWin.Pages;

public partial class SettingsPage : Page
{
    private readonly AuthStateStore _authState;

    public SettingsPage(AuthStateStore authState)
    {
        _authState = authState;

        InitializeComponent();

        ThemeToggle.IsChecked =
            !string.Equals(App.Settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);

        LoadVersionInfo();
    }

    private void LoadVersionInfo()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        CurrentVersionText.Text = version?.ToString() ?? "Unknown";

        _ = LoadLatestVersionAsync();
    }

    private async Task LoadLatestVersionAsync()
    {
        await Task.Delay(500);
        LatestVersionText.Text = "1.2.0";
    }

    private void ThemeToggle_OnChecked(object sender, RoutedEventArgs e)
    {
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);

        App.Settings.Theme = "Dark";
        AppSettingsStore.SaveSafe(App.Settings);
    }

    private void ThemeToggle_OnUnchecked(object sender, RoutedEventArgs e)
    {
        ApplicationThemeManager.Apply(ApplicationTheme.Light);

        App.Settings.Theme = "Light";
        AppSettingsStore.SaveSafe(App.Settings);
    }

    private async void LogoutButton_OnClick(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Are you sure you want to log out?",
            "Logout",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            await App.Session.LogoutAsync(CancellationToken.None);

            _authState.Clear();

            var login = new LoginWindow(_authState);

            Application.Current.MainWindow = login;
            login.Show();

            var currentWindow = Window.GetWindow(this);
            currentWindow?.Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Logout failed:\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void AboutButton_OnClick(object sender, RoutedEventArgs e)
    {
        var wnd = new AboutWindow
        {
            Owner = Window.GetWindow(this)
        };

        wnd.ShowDialog();
    }
}