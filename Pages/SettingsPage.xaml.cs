using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DataGateWin.Configuration;
using Wpf.Ui.Appearance;

namespace DataGateWin.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage()
    {
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

    private void LogoutButton_OnClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Logout clicked.");
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