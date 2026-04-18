using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DataGateWin.Configuration;
using DataGateWin.Localization;
using DataGateWin.Services.Auth;
using DataGateWin.Services.Update;
using DataGateWin.Views;
using Wpf.Ui.Appearance;

namespace DataGateWin.Pages;

public partial class SettingsPage : Page
{
    private readonly AuthStateStore _authState;
    private bool _suppressLanguageCombo;

    public SettingsPage(AuthStateStore authState)
    {
        _authState = authState;

        InitializeComponent();

        ThemeToggle.IsChecked =
            !string.Equals(App.Settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);

        LoadVersionInfo();
    }

    private void SettingsPage_OnLoaded(object sender, RoutedEventArgs e)
    {
        var pref = UiLanguageService.GetStoredLanguagePreference();
        _suppressLanguageCombo = true;
        ComboBoxItem? match = null;
        foreach (ComboBoxItem item in LanguageCombo.Items)
        {
            if (item.Tag is string t && string.Equals(t, pref, StringComparison.OrdinalIgnoreCase))
            {
                match = item;
                break;
            }
        }

        LanguageCombo.SelectedItem = match ?? LanguageCombo.Items[0] as ComboBoxItem;

        _suppressLanguageCombo = false;
    }

    private void LanguageCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageCombo)
            return;

        if (LanguageCombo.SelectedItem is not ComboBoxItem { Tag: string code })
            return;

        UiLanguageService.Apply(code, persist: true);
    }

    private void LoadVersionInfo()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        CurrentVersionText.Text = version?.ToString() ?? Loc.T("Settings_UnknownVersion");

        _ = LoadLatestVersionAsync();
    }

    private async Task LoadLatestVersionAsync()
    {
        string text;
        try
        {
            var checker = new GitHubUpdateChecker(
                new HttpClient(),
                "IMKolganov",
                "DataGateWin");

            var latest = await checker.TryGetLatestReleaseVersionForDisplayAsync(CancellationToken.None)
                .ConfigureAwait(false);
            text = latest ?? Loc.T("Settings_LatestVersionUnavailable");
        }
        catch
        {
            text = Loc.T("Settings_LatestVersionUnavailable");
        }

        await Application.Current.Dispatcher.InvokeAsync(() => LatestVersionText.Text = text);
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
            Loc.T("Msg_LogoutConfirm"),
            Loc.T("Msg_LogoutTitle"),
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
                Loc.T("Msg_LogoutFailedFmt", ex.Message),
                Loc.T("Msg_ErrorTitle"),
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
