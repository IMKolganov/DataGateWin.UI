using System.Globalization;
using System.Windows;
using DataGateWin.Configuration;
using DataGateWin.Localization;
using DataGateWin.Services.Ui;
using Wpf.Ui.Controls;

namespace DataGateWin.Views;

public partial class FirstRunConfigurationWindow : FluentWindow
{
    public FirstRunConfigurationWindow(ApiSettings? existingApi, GoogleAuthSettings? existingGoogle)
    {
        InitializeComponent();

        FluentWindowChrome.Attach(this);

        ApiBaseUrlBox.Text = string.IsNullOrWhiteSpace(existingApi?.BaseUrl)
            ? DataGatePublicDefaults.ApiBaseUrl
            : existingApi.BaseUrl.Trim();

        GoogleClientIdBox.Text = string.IsNullOrWhiteSpace(existingGoogle?.ClientId)
            ? DataGatePublicDefaults.GoogleDesktopClientId
            : existingGoogle.ClientId.Trim();

        var port = existingGoogle?.RedirectPort ?? 0;
        RedirectPortBox.Text = port > 0
            ? port.ToString(CultureInfo.InvariantCulture)
            : AppsettingsConnection.DefaultRedirectPort.ToString(CultureInfo.InvariantCulture);
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        StatusText.Visibility = Visibility.Collapsed;

        var api = new ApiSettings { BaseUrl = ApiBaseUrlBox.Text.Trim() };
        var google = new GoogleAuthSettings { ClientId = GoogleClientIdBox.Text.Trim() };

        if (!int.TryParse(RedirectPortBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
        {
            ShowError(Loc.T("FirstRun_Err_Port"));
            return;
        }

        google.RedirectPort = port;

        if (!AppsettingsConnection.IsComplete(api, google))
        {
            if (string.IsNullOrWhiteSpace(api.BaseUrl))
                ShowError(Loc.T("FirstRun_Err_BaseUrl"));
            else if (!Uri.TryCreate(api.BaseUrl, UriKind.Absolute, out var u)
                     || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
                ShowError(Loc.T("FirstRun_Err_BaseUrl"));
            else if (string.IsNullOrWhiteSpace(google.ClientId))
                ShowError(Loc.T("FirstRun_Err_ClientId"));
            else
                ShowError(Loc.T("FirstRun_Err_Port"));

            return;
        }

        try
        {
            AppsettingsConnection.SaveFile(AppContext.BaseDirectory, api, google);
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("FirstRun_Err_SaveFailedFmt", ex.Message));
            return;
        }

        DialogResult = true;
        Close();
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
