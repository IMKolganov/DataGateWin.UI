using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Principal;
using System.Windows;
using DataGateWin.Configuration;
using DataGateWin.CrashReporting;
using DataGateWin.Localization;
using DataGateWin.Services.Auth;
using DataGateWin.Services.Ipc;
using DataGateWin.Services.Tray;
using DataGateWin.Services.Update;
using DataGateWin.Views;
using Microsoft.Extensions.Configuration;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace DataGateWin;

public partial class App : Application
{
    public static IConfiguration AppConfiguration { get; private set; } = null!;
    public static AuthApiClient AuthApi { get; private set; } = null!;
    public static AuthSession Session { get; private set; } = null!;
    public static HttpClient AuthedApiHttp { get; private set; } = null!;
    public static GoogleAuthService GoogleAuth { get; private set; } = null!;
    public static AppSettings Settings { get; private set; } = new();

    private TrayService? _tray;
    
    private readonly EnginePathResolver _enginePathResolver = new();
    private string? _engineExePath;

    protected override void OnStartup(StartupEventArgs e)
    {
        InstallCrashReportingHandlers();
        SessionEnding += OnSessionEnding;
        base.OnStartup(e);
        
        Settings = AppSettingsStore.LoadSafe();
        UiLanguageService.ApplyFromSettings();

        var themeName = Settings.Theme;
        if (!string.Equals(themeName, "Light", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(themeName, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            themeName = "Dark";
            Settings.Theme = themeName;
            AppSettingsStore.SaveSafe(Settings);
        }

        var theme = string.Equals(themeName, "Light", StringComparison.OrdinalIgnoreCase)
            ? ApplicationTheme.Light
            : ApplicationTheme.Dark;

        EnsureThemesDictionary(theme);

        ApplicationThemeManager.Apply(theme);
        _ = RunStartupAsync();
    }
    
    
    private void EnsureThemesDictionary(ApplicationTheme theme)
    {
        var dictionaries = Resources.MergedDictionaries;

        var existing = dictionaries
            .FirstOrDefault(d => d is ThemesDictionary);

        if (existing is not null)
            dictionaries.Remove(existing);

        dictionaries.Insert(0, new ThemesDictionary { Theme = theme });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        TryGracefulEngineShutdownSync();

        try { AppSettingsStore.SaveSafe(Settings); } catch (Exception ex) { CrashReporter.ReportNonFatal(ex, "App.OnExit.SaveSettings"); }
        try { _tray?.Unregister(); } catch (Exception ex) { CrashReporter.ReportNonFatal(ex, "App.OnExit.TrayUnregister"); }

        base.OnExit(e);
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        TryGracefulEngineShutdownSync();
    }

    private void TryGracefulEngineShutdownSync()
    {
        try
        {
            EngineSessionService.TryStopActiveSessionSafeAsync(TimeSpan.FromSeconds(20))
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "App.TryGracefulEngineShutdown.StopSession");
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(_engineExePath) && File.Exists(_engineExePath))
                KillEngineProcessesByExactPathOnce(_engineExePath);
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "App.TryGracefulEngineShutdown.KillEngine");
        }
    }
    
    private async Task RunStartupAsync()
    {
        try
        {
            // Release builds always require elevation. Debug + F5: IDE is not elevated, so without this
            // the app exits immediately after the admin MessageBox and looks like "nothing happens".
            if (ShouldQuitForMissingAdministrator())
            {
                MessageBox.Show(
                    Loc.T("Msg_AdminBody"),
                    Loc.T("Msg_AdminTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown();
                return;
            }
            
            _engineExePath = _enginePathResolver.ResolveEngineExePath();
            var configDir = AppContext.BaseDirectory;
            var configPath = Path.Combine(configDir, "appsettings.json");

            while (true)
            {
                AppsettingsConnection.TryLoadFile(configPath, out var api, out var google);

                if (AppsettingsConnection.IsComplete(api, google))
                    break;

                var dlg = new FirstRunConfigurationWindow(api, google);
                if (dlg.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }

            AppConfiguration = new ConfigurationBuilder()
                .SetBasePath(configDir)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var apiSettings = AppConfiguration.GetSection("Api").Get<ApiSettings>()
                ?? throw new InvalidOperationException("Api settings are missing.");

            if (string.IsNullOrWhiteSpace(apiSettings.BaseUrl))
                throw new InvalidOperationException("Api:BaseUrl is missing.");

            ConfigureCrashReporting(apiSettings.BaseUrl);
            _ = CrashReporter.FlushPendingAsync(CancellationToken.None);

            var googleSettings = AppConfiguration.GetSection("GoogleAuth").Get<GoogleAuthSettings>()
                ?? throw new InvalidOperationException("GoogleAuth settings are missing.");

            if (string.IsNullOrWhiteSpace(googleSettings.ClientId))
                throw new InvalidOperationException("GoogleAuth:ClientId is missing.");

            if (googleSettings.RedirectPort <= 0 || googleSettings.RedirectPort > 65535)
                throw new InvalidOperationException("GoogleAuth:RedirectPort is invalid.");

            var deviceId = DeviceInfo.GetOrCreateDeviceId();
            var userAgent = DeviceInfo.GetUserAgent();

            var baseUri = new Uri(apiSettings.BaseUrl, UriKind.Absolute);

            var startupHttpTimeout = TimeSpan.FromSeconds(30);
            AuthApi = new AuthApiClient(new HttpClient { BaseAddress = baseUri, Timeout = startupHttpTimeout });

            Session = new AuthSession(
                AuthApi,
                new FileTokenStore("DataGateWin"),
                deviceId,
                userAgent);

            await Session.InitializeAsync(CancellationToken.None);

            AuthedApiHttp = new HttpClient(
                new AuthenticatedHttpHandler(Session, new HttpClientHandler()))
            {
                BaseAddress = baseUri,
                Timeout = TimeSpan.FromMinutes(2)
            };

            GoogleAuth = new GoogleAuthService(new HttpClient { Timeout = TimeSpan.FromMinutes(2) });

            var authState = new AuthStateStore();
            var token = await Session.GetValidAccessTokenAsync(CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(token))
            {
                authState.SetAuthorized(token);

                var main = new MainWindow(authState, AuthedApiHttp);
                MainWindow = main;

                main.Show();
                
                _ = Task.Run(async () =>
                {
                    var checker = new GitHubUpdateChecker(
                        new HttpClient(),
                        "IMKolganov",
                        "DataGateWin"
                    );

                    await checker.CheckForUpdateAsync(CancellationToken.None);
                });

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
            CrashReporter.ReportNonFatal(ex, "StartupFailed");

            MessageBox.Show(
                Loc.T("Msg_StartupFailedBodyFmt", ex.Message),
                Loc.T("Msg_StartupFailedTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown();
        }
    }

    private void InstallCrashReportingHandlers()
    {
        CrashReporter.InstallDomainHandlers();
        DispatcherUnhandledException += (_, args) =>
        {
            CrashReporter.HandleDispatcherUnhandled(args.Exception);
        };
    }

    private static void ConfigureCrashReporting(string apiBaseUrl)
    {
        var crashSettings = AppConfiguration.GetSection("CrashReporting").Get<CrashReportingConfiguration>()
            ?? AppsettingsConnection.CreateDefaultCrashReporting();

        if (string.IsNullOrWhiteSpace(crashSettings.BaseUrl))
            crashSettings.BaseUrl = apiBaseUrl;

        if (string.IsNullOrWhiteSpace(crashSettings.ProcessName))
            crashSettings.ProcessName = CrashReporter.DefaultProcessName;

        crashSettings.CrashToken ??= "";

        CrashReporter.Configure(crashSettings);
    }

    /// <summary>
    /// Release builds require elevation (see app.manifest). Debug builds use app.manifest.debug.xml (asInvoker)
    /// and skip this gate so the app can run under the IDE without admin.
    /// To test a Release build locally without elevation, set env DATAGATE_WIN_SKIP_ADMIN_CHECK=1.
    /// </summary>
    private static bool ShouldQuitForMissingAdministrator()
    {
#if DEBUG
        return false;
#else
        var skip = Environment.GetEnvironmentVariable("DATAGATE_WIN_SKIP_ADMIN_CHECK");
        if (string.Equals(skip, "1", StringComparison.Ordinal)
            || string.Equals(skip, "true", StringComparison.OrdinalIgnoreCase))
            return false;

        return !IsRunningAsAdministrator();
#endif
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
    
    private static void KillEngineProcessesByExactPathOnce(string engineExePath)
    {
        var targetPath = Path.GetFullPath(engineExePath).TrimEnd(Path.DirectorySeparatorChar);

        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(targetPath)))
        {
            try
            {
                var procPath = p.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(procPath))
                    continue;

                procPath = Path.GetFullPath(procPath).TrimEnd(Path.DirectorySeparatorChar);

                if (!string.Equals(procPath, targetPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    // Best-effort graceful close (often does nothing for console/service-like processes)
                    if (!p.HasExited)
                    {
                        p.CloseMainWindow();
                        p.WaitForExit(500);
                    }
                }
                catch (Exception ex)
                {
                    CrashReporter.ReportNonFatal(ex, "App.KillEngineProcesses.CloseMainWindow");
                }

                if (!p.HasExited)
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(1500);
                }
            }
            catch (Exception ex)
            {
                CrashReporter.ReportNonFatal(ex, "App.KillEngineProcesses");
                // ignore single-process failures
            }
            finally
            {
                try { p.Dispose(); } catch (Exception ex) { CrashReporter.ReportNonFatal(ex, "App.KillEngineProcesses.Dispose"); }
            }
        }
    }
}
