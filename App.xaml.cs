using System.IO;
using System.Net.Http;
using System.Security.Principal;
using System.Windows;
using DataGateWin.Configuration;
using DataGateWin.Services;
using DataGateWin.Services.Auth;
using DataGateWin.Services.Tray;
using DataGateWin.Views;
using Microsoft.Extensions.Configuration;
using Wpf.Ui.Appearance;

namespace DataGateWin;

public partial class App : Application
{
    public static IConfiguration AppConfiguration { get; private set; } = null!;
    public static AuthApiClient AuthApi { get; private set; } = null!;
    public static AuthSession Session { get; private set; } = null!;
    public static HttpClient AuthedApiHttp { get; private set; } = null!;
    public static GoogleAuthService GoogleAuth { get; private set; } = null!;

    private TrayService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ApplicationThemeManager.Apply(ApplicationTheme.Dark);

        _ = RunStartupAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Unregister();
        base.OnExit(e);
    }

    private async Task RunStartupAsync()
    {
        try
        {
            if (!IsRunningAsAdministrator())
            {
                MessageBox.Show(
                    "This application must be run with administrator privileges.\n\n" +
                    "Please restart the application as an administrator.",
                    "Administrator privileges required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown();
                return;
            }

            var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(configPath))
            {
                MessageBox.Show(
                    $"Configuration file was not found:\n{configPath}",
                    "Configuration missing",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown();
                return;
            }

            AppConfiguration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var apiSettings = AppConfiguration.GetSection("Api").Get<ApiSettings>()
                ?? throw new InvalidOperationException("Api settings are missing.");

            if (string.IsNullOrWhiteSpace(apiSettings.BaseUrl))
                throw new InvalidOperationException("Api:BaseUrl is missing.");

            var googleSettings = AppConfiguration.GetSection("GoogleAuth").Get<GoogleAuthSettings>()
                ?? throw new InvalidOperationException("GoogleAuth settings are missing.");

            if (string.IsNullOrWhiteSpace(googleSettings.ClientId))
                throw new InvalidOperationException("GoogleAuth:ClientId is missing.");

            if (googleSettings.RedirectPort <= 0 || googleSettings.RedirectPort > 65535)
                throw new InvalidOperationException("GoogleAuth:RedirectPort is invalid.");

            var deviceId = DeviceInfo.GetOrCreateDeviceId();
            var userAgent = DeviceInfo.GetUserAgent();

            var baseUri = new Uri(apiSettings.BaseUrl, UriKind.Absolute);

            AuthApi = new AuthApiClient(new HttpClient { BaseAddress = baseUri });

            Session = new AuthSession(
                AuthApi,
                new FileTokenStore("DataGateWin"),
                deviceId,
                userAgent);

            await Session.InitializeAsync(CancellationToken.None);

            AuthedApiHttp = new HttpClient(
                new AuthenticatedHttpHandler(Session, new HttpClientHandler()))
            {
                BaseAddress = baseUri
            };

            GoogleAuth = new GoogleAuthService(new HttpClient());

            var authState = new AuthStateStore();
            var token = await Session.GetValidAccessTokenAsync(CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(token))
            {
                authState.SetAuthorized(token);

                var main = new MainWindow(authState);
                MainWindow = main;

                main.Show();

                _tray = new TrayService();
                _tray.AttachMainWindow(main);
                _tray.Register();

                return;
            }

            var login = new LoginWindow(authState);
            MainWindow = login;
            login.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Startup failed:\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown();
        }
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void Tray_Open_Click(object sender, RoutedEventArgs e)
    {
        if (MainWindow == null)
            return;

        MainWindow.Show();
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    private void Tray_Exit_Click(object sender, RoutedEventArgs e)
    {
        _tray?.Unregister();
        Shutdown();
    }
}
