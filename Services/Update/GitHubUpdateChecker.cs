using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using DataGateWin.CrashReporting;
using DataGateWin.Localization;

namespace DataGateWin.Services.Update;

public sealed class GitHubUpdateChecker
{
    private const string EngineExeRelativePath = "engine";

    private static int _checkInFlight;
    private static bool _updatePromptCompletedThisSession;

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
        if (_updatePromptCompletedThisSession)
            return;

        if (Interlocked.CompareExchange(ref _checkInFlight, 1, 0) != 0)
            return;

        try
        {
            var currentVersion = GetCurrentVersion();
            var latest = await GetLatestReleaseAsync(ct).ConfigureAwait(false);

            if (latest == null || !ReleaseVersionParser.IsUpgradeAvailable(latest.Version, currentVersion))
                return;

            StartUpdater();
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "GitHubUpdateChecker.CheckForUpdate");
            // Silent fail: update check must never break startup
        }
        finally
        {
            Interlocked.Exchange(ref _checkInFlight, 0);
        }
    }

    /// <summary>Latest release version from GitHub, formatted for display, or null if unavailable.</summary>
    public async Task<string?> TryGetLatestReleaseVersionForDisplayAsync(CancellationToken ct)
    {
        try
        {
            var latest = await GetLatestReleaseAsync(ct);
            return latest == null ? null : ReleaseVersionParser.FormatForDisplay(latest.Version);
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "GitHubUpdateChecker.GetLatestReleaseVersion");
            return null;
        }
    }

    private static string FormatVersionForDisplay(Version v) => ReleaseVersionParser.FormatForDisplay(v);

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
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        return new GitHubRelease
        {
            Version = ReleaseVersionParser.ParseTag(tag)
        };
    }

    internal static void ResetSessionStateForTests()
    {
        _updatePromptCompletedThisSession = false;
        Interlocked.Exchange(ref _checkInFlight, 0);
    }

    private static Version GetCurrentVersion()
    {
        return Assembly.GetEntryAssembly()?
                   .GetName()
                   .Version
               ?? new Version(0, 0, 0);
    }

    private sealed class GitHubRelease
    {
        public Version Version { get; init; } = null!;
    }

    private static void StartUpdater()
    {
        if (_updatePromptCompletedThisSession)
            return;

        RunOnUiThread(() =>
        {
            if (_updatePromptCompletedThisSession)
                return;

            var owner = Application.Current?.MainWindow;
            if (owner != null)
                owner.IsEnabled = false;

            var shouldReenable = true;
            try
            {
                if (!ConfirmUpdate(owner))
                {
                    _updatePromptCompletedThisSession = true;
                    return;
                }

                var updaterPath = AppInstallerLocator.TryFindInstallerExe();
                if (string.IsNullOrWhiteSpace(updaterPath))
                {
                    _updatePromptCompletedThisSession = true;
                    ShowUpdaterMissing(owner);
                    return;
                }

                _updatePromptCompletedThisSession = true;
                StopEngineIfRunning();
                LaunchUpdater(updaterPath);

                shouldReenable = false;
                Application.Current?.Shutdown();
            }
            finally
            {
                if (owner != null && shouldReenable)
                    owner.IsEnabled = true;
            }
        });
    }

    private static bool ConfirmUpdate(Window? owner)
    {
        var decision = MessageBox.Show(
            owner,
            Loc.T("Msg_UpdateAvailableBody"),
            Loc.T("Msg_UpdateAvailableTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        return decision == MessageBoxResult.Yes;
    }

    private static void ShowUpdaterMissing(Window? owner)
    {
        MessageBox.Show(
            owner,
            Loc.T("Msg_UpdateErrorBody"),
            Loc.T("Msg_UpdateErrorTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static void StopEngineIfRunning()
    {
        var enginePath = Path.Combine(AppContext.BaseDirectory, EngineExeRelativePath, "engine.exe");
        if (File.Exists(enginePath))
            KillEngineProcessesByExactPathOnce(enginePath);
    }

    private static void LaunchUpdater(string updaterPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = updaterPath,
            Arguments = AppInstallerLocator.InstallerUpdateArgument,
            UseShellExecute = true,
            WorkingDirectory = AppContext.BaseDirectory
        });
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
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
                catch (Exception ex)
                {
                    CrashReporter.ReportNonFatal(ex, "GitHubUpdateChecker.KillEngine.CloseMainWindow");
                }

                if (!p.HasExited)
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(1500);
                }
            }
            catch (Exception ex)
            {
                CrashReporter.ReportNonFatal(ex, "GitHubUpdateChecker.KillEngine");
                // ignore single-process failures
            }
            finally
            {
                try { p.Dispose(); } catch (Exception ex) { CrashReporter.ReportNonFatal(ex, "GitHubUpdateChecker.KillEngine.Dispose"); }
            }
        }
    }
}