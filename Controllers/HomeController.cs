using DataGateWin.Models.Ipc;
using DataGateWin.Services.Ipc;

namespace DataGateWin.Controllers;

public sealed class HomeController
{
    private readonly Action<string> _setStatusText;
    private readonly Action<UiState, string> _applyUiState;
    private readonly Action<string> _log;

    private readonly SemaphoreSlim _opLock = new(1, 1);

    private CancellationTokenSource? _lifetimeCts;
    private bool _desiredConnected;
    private int _reconnectAttempt;

    private readonly EngineSessionService _engine;

    public HomeController(
        Action<string> statusTextSetter,
        Action<UiState, string> uiStateApplier,
        Action<string> logAppender)
    {
        _setStatusText = statusTextSetter;
        _applyUiState = uiStateApplier;
        _log = logAppender;

        _engine = new EngineSessionService(
            enginePathResolver: new EnginePathResolver(),
            payloadBuilder: new StartSessionPayloadBuilder(),
            log: _log,
            onEngineEvent: HandleEngineEvent
        );

        _applyUiState(UiState.Idle, "Idle");
        _log("UI ready.");
    }

    public async Task OnLoadedAsync()
    {
        _lifetimeCts?.Cancel();
        _lifetimeCts = new CancellationTokenSource();

        _desiredConnected = false;

        try
        {
            _applyUiState(UiState.Connecting, "Attaching...");
            await _engine.AttachAsync(_lifetimeCts.Token);
            await RefreshStatusAsync(_lifetimeCts.Token);
        }
        catch (Exception ex)
        {
            _log($"ERROR: {ex}");
            _applyUiState(UiState.Idle, $"Idle (attach failed: {ex.Message})");
        }
    }

    public void OnUnloaded()
    {
        try { _lifetimeCts?.Cancel(); } catch { }
        _lifetimeCts = null;

        _engine.Dispose();
    }

    public async Task ConnectAsync()
    {
        _desiredConnected = true;
        await EnsureConnectedAsync();
    }

    public async Task DisconnectAsync()
    {
        _desiredConnected = false;
        await EnsureDisconnectedAsync(userInitiated: true);
    }

    private async Task EnsureConnectedAsync()
    {
        var ct = _lifetimeCts?.Token ?? CancellationToken.None;

        await _opLock.WaitAsync(ct);
        try
        {
            _applyUiState(UiState.Connecting, "Connecting...");

            await _engine.AttachOrStartAsync(ct);

            var state = await _engine.GetEngineStateAsync(ct);
            if (!EngineState.IsIdle(state))
            {
                _applyUiState(UiState.Connected, $"Connected ({state ?? "unknown"})");
                return;
            }

            var started = await _engine.StartSessionAsync(ct);
            if (!started)
            {
                _applyUiState(UiState.Idle, "Idle (start failed)");
                if (_desiredConnected)
                    _ = ScheduleReconnectAsync();
                return;
            }

            _applyUiState(UiState.Connecting, "Connecting (waiting for events)...");
            _reconnectAttempt = 0;
        }
        catch (Exception ex)
        {
            _applyUiState(UiState.Idle, $"Idle (error: {ex.Message})");
            _log($"ERROR: {ex}");

            if (_desiredConnected)
                _ = ScheduleReconnectAsync();
        }
        finally
        {
            _opLock.Release();
        }
    }

    private async Task EnsureDisconnectedAsync(bool userInitiated)
    {
        var ct = _lifetimeCts?.Token ?? CancellationToken.None;

        await _opLock.WaitAsync(ct);
        try
        {
            _applyUiState(UiState.Disconnecting, "Disconnecting...");

            await _engine.StopSessionSafeAsync(ct);

            _applyUiState(UiState.Idle, userInitiated ? "Idle" : "Idle (disconnected)");
        }
        finally
        {
            _opLock.Release();
        }
    }

    private async Task RefreshStatusAsync(CancellationToken ct)
    {
        if (!await _engine.IsAttachedAsync(ct))
        {
            _applyUiState(UiState.Idle, "Idle (not attached)");
            return;
        }

        var state = await _engine.GetEngineStateAsync(ct);
        _applyUiState(
            EngineState.IsIdle(state) ? UiState.Idle : UiState.Connected,
            EngineState.IsIdle(state) ? "Idle" : $"Connected ({state ?? "unknown"})"
        );
    }

    private async Task ScheduleReconnectAsync()
    {
        var ct = _lifetimeCts?.Token ?? CancellationToken.None;

        if (!_desiredConnected)
            return;

        _reconnectAttempt++;
        var delay = ReconnectPolicy.GetDelay(_reconnectAttempt);

        _applyUiState(UiState.Connecting, $"Reconnecting in {delay.TotalSeconds:0}s...");
        _log($"Reconnect scheduled. Attempt={_reconnectAttempt}, Delay={delay.TotalSeconds:0}s");

        try { await Task.Delay(delay, ct); }
        catch { _applyUiState(UiState.Idle, "Idle"); return; }

        if (_desiredConnected)
            await EnsureConnectedAsync();
    }

    private void HandleEngineEvent(EngineEvent ev)
    {
        // 1) обновление статуса
        if (ev.Kind == EngineEventKind.StateChanged)
        {
            _setStatusText($"State: {ev.State ?? "?"}");
            _applyUiState(EngineState.IsIdle(ev.State) ? UiState.Idle : UiState.Connected,
                EngineState.IsIdle(ev.State) ? "Idle" : $"Connected ({ev.State})");
            return;
        }

        // 2) connected/disconnected
        if (ev.Kind == EngineEventKind.Connected)
        {
            _reconnectAttempt = 0;
            _applyUiState(UiState.Connected, string.IsNullOrWhiteSpace(ev.Ip) ? "Connected" : $"Connected ({ev.Ip})");
            return;
        }

        if (ev.Kind == EngineEventKind.Disconnected)
        {
            _log($"Disconnected: {ev.Reason ?? "Unknown"}");
            _applyUiState(UiState.Idle, $"Idle (disconnected: {ev.Reason ?? "Unknown"})");

            if (_desiredConnected)
                _ = ScheduleReconnectAsync();

            return;
        }

        // 3) ошибки уже логируются ниже
    }
}