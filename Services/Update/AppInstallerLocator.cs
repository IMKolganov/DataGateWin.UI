using System.IO;

namespace DataGateWin.Services.Update;

public static class AppInstallerLocator
{
    public const string InstallerExeName = "DataGateWin.Installer.exe";

    public const string InstallerUpdateArgument = "update";

    /// <summary>Official download / reinstall page (full installer).</summary>
    public const string DownloadPageUrl = "https://datagateapp.com/download";

    public static string? TryFindInstallerExe()
    {
        var baseDir = AppContext.BaseDirectory;

        var candidates = new[]
        {
            Path.Combine(baseDir, "Installer", InstallerExeName),
            Path.Combine(baseDir, "installer", InstallerExeName),
            Path.Combine(baseDir, InstallerExeName)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
