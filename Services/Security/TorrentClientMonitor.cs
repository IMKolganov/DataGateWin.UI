using System.Diagnostics;
using System.Windows;
using DataGateWin.CrashReporting;
using DataGateWin.Localization;

namespace DataGateWin.Services.Security;

public static class TorrentProcessDetector
{
    private static readonly HashSet<string> KnownTorrentProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "qbittorrent",
        "utorrent",
        "utorrentportable",
        "bittorrent",
        "deluge",
        "transmission-qt",
        "transmission-cli",
        "transmission-gtk",
        "transmission-daemon",
        "vuze",
        "azureus",
        "biglybt",
        "picotorrent",
        "webtorrent",
        "tribler",
        "tixati",
        "bitcomet",
        "frostwire"
    };

    public static bool IsTorrentProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        var normalized = processName.Trim();
        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^4];

        return KnownTorrentProcessNames.Contains(normalized);
    }

    public static IReadOnlyList<string> DetectFromProcessNames(IEnumerable<string?> processNames)
    {
        return processNames
            .Where(IsTorrentProcessName)
            .Select(p => p!.Trim().EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p.Trim()[..^4] : p!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class TorrentClientMonitor : IDisposable
{
    private readonly Window _owner;
    private readonly HashSet<string> _activeAlertedProcessNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private bool _isRunning;

    public TorrentClientMonitor(Window owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public void Start()
    {
        if (_isRunning)
            return;

        _isRunning = true;
        _cts = new CancellationTokenSource();
        _monitorTask = RunMonitorLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        if (!_isRunning)
            return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _monitorTask = null;
        _isRunning = false;
    }

    private async Task RunMonitorLoopAsync(CancellationToken ct)
    {
        await ScanAndNotifyAsync(ct).ConfigureAwait(false);

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                await ScanAndNotifyAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // monitor was stopped
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "TorrentClientMonitor.Loop");
        }
    }

    private async Task ScanAndNotifyAsync(CancellationToken ct)
    {
        if (!await _scanLock.WaitAsync(0, ct).ConfigureAwait(false))
            return;

        IReadOnlyList<string> detected;
        try
        {
            detected = await Task.Run(DetectRunningTorrentProcesses, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "TorrentClientMonitor.Scan");
            return;
        }
        finally
        {
            _scanLock.Release();
        }

        var detectedSet = new HashSet<string>(detected, StringComparer.OrdinalIgnoreCase);
        var newlyDetected = detected.Where(p => !_activeAlertedProcessNames.Contains(p)).ToList();

        _activeAlertedProcessNames.RemoveWhere(p => !detectedSet.Contains(p));
        foreach (var p in newlyDetected)
            _activeAlertedProcessNames.Add(p);

        if (newlyDetected.Count == 0)
            return;

        var processList = string.Join(", ", newlyDetected);
        var warningBody = Loc.T("Torrent_WarningBodyFmt", processList);
        var warningTitle = Loc.T("Torrent_WarningTitle");

        await _owner.Dispatcher.InvokeAsync(() =>
            MessageBox.Show(
                _owner,
                warningBody,
                warningTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning));

        CrashReporter.ReportNonFatal(
            new InvalidOperationException($"Torrent client detected on user machine. Processes: {processList}."),
            "TorrentClientMonitor.Detected");
    }

    public void Dispose()
    {
        Stop();
        _scanLock.Dispose();
    }

    private static IReadOnlyList<string> DetectRunningTorrentProcesses()
    {
        var names = new List<string?>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                names.Add(process.ProcessName);
            }
            catch
            {
                names.Add(null);
            }
            finally
            {
                try { process.Dispose(); } catch { }
            }
        }

        return TorrentProcessDetector.DetectFromProcessNames(names);
    }
}
