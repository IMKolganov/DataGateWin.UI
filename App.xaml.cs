using System.IO;
using System.Security.Principal;
using System.Windows;
using DataGateWin.Views;
using Microsoft.Extensions.Configuration;

namespace DataGateWin;

public partial class App : Application
{
    public static IConfiguration AppConfiguration { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
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
                    MessageBoxImage.Error
                );

                Shutdown();
                return;
            }

            var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(configPath))
            {
                MessageBox.Show(
                    "Configuration file was not found.\n\n" +
                    $"Expected path:\n{configPath}\n\n" +
                    "Please place appsettings.json next to the application executable and restart.",
                    "Configuration file missing",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                Shutdown();
                return;
            }

            AppConfiguration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            base.OnStartup(e);

            var authState = new Services.AuthStateStore();

            // MVP: always show login first
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
                MessageBoxImage.Error
            );

            Shutdown();
        }
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
