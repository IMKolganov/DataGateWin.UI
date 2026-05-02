using System.Windows;
using System.Windows.Controls;
using DataGateWin.Configuration;
using DataGateWin.Localization;
using DataGateWin.Services.Auth;
using DataGateWin.Services.Support;
using DataGateWin.Services.Ui;
using DataGateWin.ViewModels;
using Microsoft.Extensions.Configuration;
using Wpf.Ui.Controls;

namespace DataGateWin.Views;

public partial class LoginWindow : FluentWindow
{
    private readonly AuthStateStore _authState;
    private bool _suppressLanguageCombo;

    public LoginWindow(AuthStateStore authState)
    {
        InitializeComponent();

        FluentWindowChrome.Attach(this);

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

            var main = new MainWindow(_authState, App.AuthedApiHttp);
            Application.Current.MainWindow = main;
            main.Show();

            Close();
        };

        DataContext = vm;

        UiLanguageService.LanguageChanged += OnUiLanguageChanged;
        Closed += (_, _) => UiLanguageService.LanguageChanged -= OnUiLanguageChanged;
    }

    private void ReportIssue_OnClick(object sender, RoutedEventArgs e)
    {
        new ReportIssueDialog { Owner = this }.ShowDialog();
    }

    private void TelegramChannel_OnClick(object sender, RoutedEventArgs e)
    {
        TelegramChannel.OpenPublicChannel();
    }

    private void OnUiLanguageChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(PopulateLoginLanguageCombo);
    }

    private void LoginWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        PopulateLoginLanguageCombo();
    }

    private void PopulateLoginLanguageCombo()
    {
        var pref = UiLanguageService.GetStoredLanguagePreference();
        _suppressLanguageCombo = true;
        LoginLanguageCombo.Items.Clear();
        LoginLanguageCombo.Items.Add(new ComboBoxItem
        {
            Tag = UiLanguageService.SystemPreference,
            Content = UiLanguageService.GetLanguageDisplayName(UiLanguageService.SystemPreference),
        });
        foreach (var code in UiLanguageService.GetLanguagePickerCodes())
        {
            LoginLanguageCombo.Items.Add(new ComboBoxItem
            {
                Tag = code,
                Content = UiLanguageService.GetLanguageDisplayName(code),
            });
        }

        ComboBoxItem? match = null;
        foreach (ComboBoxItem item in LoginLanguageCombo.Items)
        {
            if (item.Tag is string t && string.Equals(t, pref, StringComparison.OrdinalIgnoreCase))
            {
                match = item;
                break;
            }
        }

        LoginLanguageCombo.SelectedItem = match ?? LoginLanguageCombo.Items[0] as ComboBoxItem;

        _suppressLanguageCombo = false;
    }

    private void LoginLanguageCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageCombo)
            return;

        if (LoginLanguageCombo.SelectedItem is not ComboBoxItem { Tag: string code })
            return;

        UiLanguageService.Apply(code, persist: true);
    }
}