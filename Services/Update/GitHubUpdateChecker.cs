using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace DataGateWin.Services.Update;

public sealed class GitHubUpdateChecker
{
    private readonly HttpClient _http;
    private readonly string _owner;
    private readonly string _repo;

    public GitHubUpdateChecker(HttpClient http, string owner, string repo)
    {
        _http = http;
        _owner = owner;
        _repo = repo;

        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DataGateWin");
    }

    public async Task CheckForUpdateAsync(CancellationToken ct)
    {
        try
        {
            var currentVersion = GetCurrentVersion();
            var latest = await GetLatestReleaseAsync(ct);

            if (latest == null || latest.Version <= currentVersion)
                return;

            StartUpdater();
        }
        catch
        {
            // Silent fail: update check must never break startup
        }
    }

    private async Task<GitHubRelease?> GetLatestReleaseAsync(CancellationToken ct)
    {
        var url = $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest";

        using var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
            return null;

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString();
        var htmlUrl = root.GetProperty("html_url").GetString();

        if (string.IsNullOrWhiteSpace(tag))
            return null;

        return new GitHubRelease
        {
            Version = ParseVersion(tag),
            HtmlUrl = htmlUrl!
        };
    }

    private static Version GetCurrentVersion()
    {
        return Assembly.GetEntryAssembly()?
                   .GetName()
                   .Version
               ?? new Version(0, 0, 0);
    }

    private static Version ParseVersion(string tag)
    {
        // supports: v1.2.3 or 1.2.3
        tag = tag.TrimStart('v', 'V');
        return Version.TryParse(tag, out var v)
            ? v
            : new Version(0, 0, 0);
    }

    private sealed class GitHubRelease
    {
        public Version Version { get; init; } = null!;
        public string HtmlUrl { get; init; } = null!;
    }

    private static void StartUpdater()
    {
        void Run()
        {
            var updaterPath = ResolveUpdaterPath();

            var owner = Application.Current?.MainWindow;
            if (owner != null)
                owner.IsEnabled = false;

            var shouldReenable = true;
            try
            {
                var decision = MessageBox.Show(
                    owner,
                    "A new update is available. Do you want to install it now?",
                    "Update available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (decision != MessageBoxResult.Yes)
                    return;

                if (string.IsNullOrWhiteSpace(updaterPath))
                {
                    MessageBox.Show(
                        owner,
                        "A new update is available, but the update component could not be found.\n\n" +
                        "Please reinstall the application or contact support.",
                        "Update Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    return;
                }

                var enginePath = Path.Combine(AppContext.BaseDirectory, "engine", "engine.exe");
                if (File.Exists(enginePath))
                    KillEngineProcessesByExactPathOnce(enginePath);

                Process.Start(new ProcessStartInfo
                {
                    FileName = updaterPath,
                    Arguments = "update",
                    UseShellExecute = true,
                    WorkingDirectory = AppContext.BaseDirectory
                });

                shouldReenable = false;
                Application.Current?.Shutdown();
            }
            finally
            {
                if (owner != null && shouldReenable)
                    owner.IsEnabled = true;
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            Run();
            return;
        }

        dispatcher.Invoke(Run);
    }

    private static string? ResolveUpdaterPath()
    {
        var baseDir = AppContext.BaseDirectory;

        var candidates = new[]
        {
            Path.Combine(baseDir, "Installer", "DataGateWin.Installer.exe"),
            Path.Combine(baseDir, "installer", "DataGateWin.Installer.exe"),
            Path.Combine(baseDir, "DataGateWin.Installer.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
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
                    if (!p.HasExited)
                    {
                        p.CloseMainWindow();
                        p.WaitForExit(500);
                    }
                }
                catch { }

                if (!p.HasExited)
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(1500);
                }
            }
            catch
            {
                // ignore single-process failures
            }
            finally
            {
                try { p.Dispose(); } catch { }
            }
        }
    }
}