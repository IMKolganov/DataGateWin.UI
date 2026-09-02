using System.Diagnostics;
using System.IO;
using DataGateWin.CrashReporting;

namespace DataGateWin.Services.Ipc;

/// <summary>
/// Runs <c>engine.exe --recover-dns</c> to clear leftover OpenVPN NRPT / SearchList state.
/// Safe to call after killing a stale engine or before attach; failures are non-fatal.
/// </summary>
internal static class EngineDnsRecoveryRunner
{
    public static void TryRecover(string engineExePath, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(engineExePath) || !File.Exists(engineExePath))
        {
            log?.Invoke("[ui][dns] recover-dns skipped: engine.exe not found");
            return;
        }

        try
        {
            var engineDir = Path.GetDirectoryName(engineExePath) ?? ".";
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = engineExePath,
                Arguments = "--recover-dns",
                WorkingDirectory = engineDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process == null)
            {
                log?.Invoke("[ui][dns] recover-dns failed: process not started");
                return;
            }

            if (!process.WaitForExit(20000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                log?.Invoke("[ui][dns] recover-dns timed out");
                return;
            }

            var stderr = process.StandardError.ReadToEnd();
            if (process.ExitCode == 0)
                log?.Invoke("[ui][dns] recover-dns OK");
            else
                log?.Invoke($"[ui][dns] recover-dns exit={process.ExitCode} stderr={stderr}");
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "EngineDnsRecoveryRunner.TryRecover");
            log?.Invoke($"[ui][dns] recover-dns error: {ex.Message}");
        }
    }
}
