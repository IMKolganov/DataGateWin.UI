using System.IO;
using System.Windows.Threading;
using DataGateWin.Ipc;
using DataGateWin.Models.Ipc;

namespace DataGateWin.Services.Ipc;

public sealed class EngineSessionService : IDisposable
{
    private readonly EnginePathResolver _pathResolver;
    private readonly StartSessionPayloadBuilder _payloadBuilder;
    private readonly Action<string> _log;
    private readonly Action<EngineEvent> _onEngineEvent;

    private EngineIpcClient? _client;
    private bool _handlersAttached;

    private const string SessionId = "dev";

    public EngineSessionService(
        EnginePathResolver enginePathResolver,
        StartSessionPayloadBuilder payloadBuilder,
        Action<string> log,
        Action<EngineEvent> onEngineEvent)
    {
        _pathResolver = enginePathResolver;
        _payloadBuilder = payloadBuilder;
        _log = log;
        _onEngineEvent = onEngineEvent;
    }

    public void Dispose()
    {
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

        var attached = await _client.TryConnectExistingAsync(10000, attachCts.Token).ConfigureAwait(false);
        if (attached)
        {
            _log("Engine attached (existing).");
            return;
        }

        _log($"Attach failed: {_client.LastAttachError ?? "unknown"}. Starting/attaching engine process...");

        _client.ResetConnection();

        using var startCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        startCts.CancelAfter(TimeSpan.FromSeconds(12));
        await _client.StartOrAttachAsync(startCts.Token).ConfigureAwait(false);

        _log("Engine connected.");
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

        var payload = await _payloadBuilder.BuildAsync(ct).ConfigureAwait(false);
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
            _log($"StartSession failed: {reply.Code ?? "?"} - {reply.Message ?? "?"}");
            return false;
        }

        return true;
    }

    public async Task StopSessionSafeAsync(CancellationToken ct)
    {
        if (_client == null)
            return;

        try
        {
            using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stopCts.CancelAfter(TimeSpan.FromSeconds(20));

            var reply = await _client.SendCommandAsync("StopSession", "{}", stopCts.Token).ConfigureAwait(false);
            if (!reply.Ok)
                _log($"StopSession failed: {reply.Code ?? "?"} - {reply.Message ?? "?"}");
        }
        catch (Exception ex)
        {
            _log($"Disconnect error: {ex}");
        }
    }

    private void EnsureClientCreated()
    {
        if (_client != null)
            return;

        var engineExePath = _pathResolver.ResolveEngineExePath();
        if (!File.Exists(engineExePath))
            throw new FileNotFoundException("Engine executable not found.", engineExePath);

        _client = new EngineIpcClient(engineExePath, SessionId);
    }

    private void AttachHandlersOnce()
    {
        if (_client == null || _handlersAttached)
            return;

        _handlersAttached = true;

        _client.EngineLogReceived += (_, line) =>
        {
            if (!string.IsNullOrWhiteSpace(line))
                _log(line);
        };

        _client.EngineExited += (_, code) =>
        {
            _log($"Engine exited with code: {code}");
            _onEngineEvent(new EngineEvent
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

                // If it's a log event, we can also log it here (optional)
                if (mapped.Kind == EngineEventKind.Log && !string.IsNullOrWhiteSpace(mapped.Message))
                    _log(mapped.Message);

                _onEngineEvent(mapped);
            }
            catch (Exception ex)
            {
                _log($"Event handler error: {ex}");
            }
        };
    }
}