using System.Windows;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Appearance;

namespace DataGateWin.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();

        // Initialize toggle state from current theme.
        var current = ApplicationThemeManager.GetAppTheme();
        ThemeToggle.IsChecked = current == ApplicationTheme.Dark;
    }

    private void ThemeToggle_OnChecked(object sender, RoutedEventArgs e)
    {
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
    }

    private void ThemeToggle_OnUnchecked(object sender, RoutedEventArgs e)
    {
        ApplicationThemeManager.Apply(ApplicationTheme.Light);
    }

    private void LogoutButton_OnClick(object sender, RoutedEventArgs e)
    {
        // TODO: call your AuthStateStore/service logout logic here
        // Example:
        // _authState.Logout();
        //
        // Then navigate to login window / page.

        MessageBox.Show("Logout clicked.");
    }
}