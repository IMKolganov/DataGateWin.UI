using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
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
    private readonly DispatcherTimer _timer;
    private readonly Window _owner;
    private readonly HashSet<string> _activeAlertedProcessNames = new(StringComparer.OrdinalIgnoreCase);
    private bool _isRunning;

    public TorrentClientMonitor(Window owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(20)
        };
        _timer.Tick += (_, _) => ScanAndNotify();
    }

    public void Start()
    {
        if (_isRunning)
            return;

        _isRunning = true;
        ScanAndNotify();
        _timer.Start();
    }

    public void Stop()
    {
        if (!_isRunning)
            return;

        _timer.Stop();
        _isRunning = false;
    }

    private void ScanAndNotify()
    {
        IReadOnlyList<string> detected;
        try
        {
            detected = TorrentProcessDetector.DetectFromProcessNames(
                Process.GetProcesses().Select(p =>
                {
                    try
                    {
                        return p.ProcessName;
                    }
                    catch
                    {
                        return null;
                    }
                    finally
                    {
                        try { p.Dispose(); } catch { }
                    }
                }));
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "TorrentClientMonitor.Scan");
            return;
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

        MessageBox.Show(
            _owner,
            warningBody,
            warningTitle,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        CrashReporter.ReportNonFatal(
            new InvalidOperationException($"Torrent client detected on user machine. Processes: {processList}."),
            "TorrentClientMonitor.Detected");
    }

    public void Dispose()
    {
        Stop();
    }
}
