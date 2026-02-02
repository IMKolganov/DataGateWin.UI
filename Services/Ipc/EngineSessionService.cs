using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using DataGateWin.Ipc;
using DataGateWin.Models.Ipc;
using Newtonsoft.Json.Linq;

namespace DataGateWin.Services.Ipc;

public sealed class EngineSessionService(
    EnginePathResolver enginePathResolver,
    StartSessionPayloadBuilder payloadBuilder,
    Action<string> log,
    Action<EngineEvent> onEngineEvent)
    : IDisposable
{
    private EngineIpcClient? _client;
    private bool _handlersAttached;

    private const string SessionId = "dev";

    // Guard so we don't kill repeatedly if service is used multiple times
    private bool _startupKillDone;

    public void Dispose()
    {
        try { _client?.Dispose(); } catch { }
        _client = null;
        _handlersAttached = false;
    }

    public async Task AttachAsync(CancellationToken ct)
    {
        EnsureClientCreated();
        AttachHandlersOnce();

        if (_client!.IsConnected)
            return;

        using var attachCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        attachCts.CancelAfter(TimeSpan.FromSeconds(8));

        var attached = await _client.TryConnectExistingAsync(8000, attachCts.Token).ConfigureAwait(false);
        if (!attached)
            throw new InvalidOperationException($"Attach failed: {_client.LastAttachError ?? "unknown"}");
    }

    public async Task AttachOrStartAsync(CancellationToken ct)
    {
        EnsureClientCreated();
        AttachHandlersOnce();

        if (_client!.IsConnected)
            return;

        using var attachCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        attachCts.CancelAfter(TimeSpan.FromSeconds(10));

        var attached = await _client.TryConnectExistingAsync(1000, attachCts.Token).ConfigureAwait(false);
        if (attached)
        {
            log("Engine attached (existing).");
            return;
        }

        log($"Attach failed: {_client.LastAttachError ?? "unknown"}. Starting/attaching engine process...");

        _client.ResetConnection();

        using var startCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        startCts.CancelAfter(TimeSpan.FromSeconds(12));
        await _client.StartOrAttachAsync(startCts.Token).ConfigureAwait(false);

        log("Engine connected.");
    }

    public async Task<bool> IsAttachedAsync(CancellationToken ct)
    {
        EnsureClientCreated();
        return _client!.IsConnected;
    }

    public async Task<string?> GetEngineStateAsync(CancellationToken ct)
    {
        EnsureClientCreated();

        using var statusCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        statusCts.CancelAfter(TimeSpan.FromSeconds(5));

        var reply = await _client!.SendCommandAsync("GetStatus", "{}", statusCts.Token).ConfigureAwait(false);
        if (!reply.Ok)
            return null;

        return reply.Payload?["state"]?.ToString();
    }

    public async Task<bool> StartSessionAsync(CancellationToken ct)
    {
        EnsureClientCreated();

        JObject? payload;
        try
        {
            payload = await payloadBuilder.BuildAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            log("StartSession skipped: canceled.");
            return false;
        }
        catch (Exception ex)
        {
            log($"BuildAsync failed: {ex.Message}");
            return false;
        }

        if (payload == null)
            return false;

        using var startCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        startCts.CancelAfter(TimeSpan.FromSeconds(20));

        var reply = await _client!.SendCommandAsync(
            "StartSession",
            payload.ToString(Newtonsoft.Json.Formatting.None),
            startCts.Token).ConfigureAwait(false);

        if (!reply.Ok)
        {
            log($"StartSession failed: {reply.Code ?? "?"} - {reply.Message ?? "?"}");
            return false;
        }

        return true;
    }


    public async Task StopSessionSafeAsync(CancellationToken ct)
    {
        if (_client == null)
        {
            log("[ui][disconnect] StopSession skipped: client is null");
            return;
        }

        log($"[ui][disconnect] StopSession START isConnected={_client.IsConnected}");

        try
        {
            using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stopCts.CancelAfter(TimeSpan.FromSeconds(20));

            var startedAt = DateTime.UtcNow;
            log("[ui][disconnect] StopSession sending command... timeout=20s");

            var reply = await _client.SendCommandAsync("StopSession", "{}", stopCts.Token).ConfigureAwait(false);

            var ms = (DateTime.UtcNow - startedAt).TotalMilliseconds;
            log($"[ui][disconnect] StopSession reply received in {ms:0}ms ok={reply.Ok} code={reply.Code ?? "<null>"} message={reply.Message ?? "<null>"}");

            if (!reply.Ok)
                log($"[ui][disconnect] StopSession FAILED code={reply.Code ?? "?"} message={reply.Message ?? "?"}");
            else
                log("[ui][disconnect] StopSession OK");
        }
        catch (OperationCanceledException oce) when (ct.IsCancellationRequested)
        {
            log($"[ui][disconnect] StopSession CANCELED by outer token: {oce.Message}");
        }
        catch (OperationCanceledException oce)
        {
            log($"[ui][disconnect] StopSession TIMEOUT/CANCELED by internal token: {oce.Message}");
        }
        catch (Exception ex)
        {
            log($"[ui][disconnect] StopSession ERROR: {ex}");
        }
        finally
        {
            log("[ui][disconnect] StopSession END");
        }
    }

    private void EnsureClientCreated()
    {
        if (_client != null)
            return;

        var engineExePath = enginePathResolver.ResolveEngineExePath();
        if (!File.Exists(engineExePath))
            throw new FileNotFoundException("Engine executable not found.", engineExePath);

        KillEngineProcessesByExactPathOnce(engineExePath);

        _client = new EngineIpcClient(engineExePath, SessionId);
    }

    private void KillEngineProcessesByExactPathOnce(string engineExePath)
    {
        if (_startupKillDone)
            return;

        _startupKillDone = true;

        try
        {
            var fullTarget = Path.GetFullPath(engineExePath);

            foreach (var p in Process.GetProcessesByName("engine"))
            {
                try
                {
                    string? procPath = null;

                    try
                    {
                        procPath = p.MainModule?.FileName;
                    }
                    catch (Win32Exception)
                    {
                        // Access denied for some processes; ignore
                    }
                    catch (InvalidOperationException)
                    {
                        // Process exited; ignore
                    }

                    if (procPath == null)
                        continue;

                    if (!Path.GetFullPath(procPath).Equals(fullTarget, StringComparison.OrdinalIgnoreCase))
                        continue;

                    log($"[ui][startup] Killing stale engine process pid={p.Id} path={procPath}");

                    try
                    {
                        p.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // Fallback
                        p.Kill();
                    }

                    try
                    {
                        p.WaitForExit(2000);
                    }
                    catch
                    {
                        // Ignore
                    }
                }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            log($"[ui][startup] KillEngineProcesses failed (ignored): {ex.Message}");
        }
    }

    private void AttachHandlersOnce()
    {
        if (_client == null || _handlersAttached)
            return;

        _handlersAttached = true;

        _client.EngineLogReceived += (_, line) =>
        {
            if (!string.IsNullOrWhiteSpace(line))
                log(line);
        };

        _client.EngineExited += (_, code) =>
        {
            log($"Engine exited with code: {code}");
            onEngineEvent(new EngineEvent
            {
                Kind = EngineEventKind.EngineExited,
                ExitCode = code
            });
        };

        _client.EventReceived += (_, ev) =>
        {
            try
            {
                var mapped = EngineEventMapper.Map(ev);
                if (mapped == null)
                    return;

                if (mapped.Kind == EngineEventKind.Log && !string.IsNullOrWhiteSpace(mapped.Message))
                    log(mapped.Message);

                onEngineEvent(mapped);
            }
            catch (Exception ex)
            {
                log($"Event handler error: {ex}");
            }
        };
    }
}
