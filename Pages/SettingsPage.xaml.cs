using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Appearance;

namespace DataGateWin.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();

        var currentTheme = ApplicationThemeManager.GetAppTheme();
        ThemeToggle.IsChecked = currentTheme == ApplicationTheme.Dark;

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
        // TODO: replace with real API call
        await Task.Delay(500);

        // Example placeholder
        LatestVersionText.Text = "1.2.0";
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