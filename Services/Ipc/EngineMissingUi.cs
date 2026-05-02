using System.Diagnostics;
using System.IO;
using System.Windows;
using DataGateWin.Localization;
using DataGateWin.Services.Update;

namespace DataGateWin.Services.Ipc;

public static class EngineMissingUi
{
    public static bool IsEngineMissingException(Exception ex)
    {
        if (ex is not FileNotFoundException fn)
            return false;

        if (fn.FileName != null &&
            fn.FileName.EndsWith("engine.exe", StringComparison.OrdinalIgnoreCase))
            return true;

        return fn.Message.Contains("Engine executable not found", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Shows reinstall / download guidance. Safe from any thread.</summary>
    public static void ShowDialog(Window? owner)
    {
        var d = Application.Current?.Dispatcher;
        if (d != null && !d.CheckAccess())
        {
            d.Invoke(() => ShowDialog(owner));
            return;
        }

        var installerPath = AppInstallerLocator.TryFindInstallerExe();

        if (!string.IsNullOrEmpty(installerPath))
        {
            var r = MessageBox.Show(
                owner,
                Loc.T("Msg_EngineMissingBodyWithInstaller", AppInstallerLocator.DownloadPageUrl),
                Loc.T("Msg_EngineMissingTitle"),
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (r == MessageBoxResult.Yes)
            {
                TryLaunchInstaller(installerPath);
                Application.Current?.Shutdown();
            }
            else if (r == MessageBoxResult.No)
            {
                OpenDownloadPage();
            }
        }
        else
        {
            var r = MessageBox.Show(
                owner,
                Loc.T("Msg_EngineMissingBodyNoInstaller", AppInstallerLocator.DownloadPageUrl),
                Loc.T("Msg_EngineMissingTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (r == MessageBoxResult.Yes)
                OpenDownloadPage();
        }
    }

    private static void TryLaunchInstaller(string installerPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = AppInstallerLocator.InstallerUpdateArgument,
            UseShellExecute = true,
            WorkingDirectory = AppContext.BaseDirectory
        });
    }

    private static void OpenDownloadPage()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = AppInstallerLocator.DownloadPageUrl,
            UseShellExecute = true
        });
    }
}
