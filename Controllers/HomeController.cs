using DataGateWin.Models.Ipc;
using DataGateWin.Services.Ipc;

namespace DataGateWin.Controllers;

public sealed class HomeController : IDisposable
{
    private readonly SemaphoreSlim _opLock = new(1, 1);

    private CancellationTokenSource? _lifetimeCts;
    private bool _desiredConnected;
    private int _reconnectAttempt;

    private readonly EngineSessionService _engine;

    private readonly object _uiLock = new();

    private Action<string>? _setStatusText;
    private Action<UiState, string>? _applyUiState;
    private Action<string>? _log;

    private UiState _lastUiState = UiState.Idle;
    private string _lastStatusText = "Idle";

    public HomeController()
    {
        _engine = new EngineSessionService(
            enginePathResolver: new EnginePathResolver(),
            payloadBuilder: new StartSessionPayloadBuilder(),
            log: Log,
            onEngineEvent: HandleEngineEvent
        );
    }

    public void AttachUi(
        Action<string> statusTextSetter,
        Action<UiState, string> uiStateApplier,
        Action<string> logAppender)
    {
        lock (_uiLock)
        {
            _setStatusText = statusTextSetter;
            _applyUiState = uiStateApplier;
            _log = logAppender;
        }

        ApplyUiState(_lastUiState, _lastStatusText);
        Log("UI attached.");
    }

    public void DetachUi()
    {
        lock (_uiLock)
        {
            _setStatusText = null;
            _applyUiState = null;
            _log = null;
        }
    }

    public async Task OnLoadedAsync()
    {
        _lifetimeCts?.Cancel();
        _lifetimeCts = new CancellationTokenSource();

        _desiredConnected = false;

        try
        {
            ApplyUiState(UiState.Connecting, "Attaching...");
            await _engine.AttachAsync(_lifetimeCts.Token);
            await RefreshStatusAsync(_lifetimeCts.Token);
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex}");
            ApplyUiState(UiState.Idle, $"Idle (attach failed: {ex.Message})");
        }
    }

    public void OnUnloaded()
    {
        // IMPORTANT: Do not dispose engine here, otherwise navigation kills your session.
        // Just detach UI and cancel operations if you want to stop ongoing UI updates.
        try { _lifetimeCts?.Cancel(); } catch { }
        _lifetimeCts = null;

        DetachUi();
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
            ApplyUiState(UiState.Connecting, "Connecting...");

            await _engine.AttachOrStartAsync(ct);

            var state = await _engine.GetEngineStateAsync(ct);
            if (!EngineState.IsIdle(state))
            {
                ApplyUiState(UiState.Connected, $"Connected ({state ?? "unknown"})");
                return;
            }

            var started = await _engine.StartSessionAsync(ct);
            if (!started)
            {
                ApplyUiState(UiState.Idle, "Idle (start failed)");
                if (_desiredConnected)
                    _ = ScheduleReconnectAsync();
                return;
            }

            ApplyUiState(UiState.Connecting, "Connecting (waiting for events)...");
            _reconnectAttempt = 0;
        }
        catch (Exception ex)
        {
            ApplyUiState(UiState.Idle, $"Idle (error: {ex.Message})");
            Log($"ERROR: {ex}");

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
            ApplyUiState(UiState.Disconnecting, "Disconnecting...");

            await _engine.StopSessionSafeAsync(ct);

            ApplyUiState(UiState.Idle, userInitiated ? "Idle" : "Idle (disconnected)");
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
            ApplyUiState(UiState.Idle, "Idle (not attached)");
            return;
        }

        var state = await _engine.GetEngineStateAsync(ct);
        ApplyUiState(
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

        ApplyUiState(UiState.Connecting, $"Reconnecting in {delay.TotalSeconds:0}s...");
        Log($"Reconnect scheduled. Attempt={_reconnectAttempt}, Delay={delay.TotalSeconds:0}s");

        try { await Task.Delay(delay, ct); }
        catch { ApplyUiState(UiState.Idle, "Idle"); return; }

        if (_desiredConnected)
            await EnsureConnectedAsync();
    }

    private void HandleEngineEvent(EngineEvent ev)
    {
        if (ev.Kind == EngineEventKind.StateChanged)
        {
            SetStatusText($"State: {ev.State ?? "?"}");
            ApplyUiState(
                EngineState.IsIdle(ev.State) ? UiState.Idle : UiState.Connected,
                EngineState.IsIdle(ev.State) ? "Idle" : $"Connected ({ev.State})"
            );
            return;
        }

        if (ev.Kind == EngineEventKind.Connected)
        {
            _reconnectAttempt = 0;
            ApplyUiState(
                UiState.Connected,
                string.IsNullOrWhiteSpace(ev.Ip) ? "Connected" : $"Connected ({ev.Ip})"
            );
            return;
        }

        if (ev.Kind == EngineEventKind.Disconnected)
        {
            Log($"Disconnected: {ev.Reason ?? "Unknown"}");
            ApplyUiState(UiState.Idle, $"Idle (disconnected: {ev.Reason ?? "Unknown"})");

            if (_desiredConnected)
                _ = ScheduleReconnectAsync();

            return;
        }
    }

    private void SetStatusText(string text)
    {
        lock (_uiLock)
        {
            _setStatusText?.Invoke(text);
        }
    }

    private void ApplyUiState(UiState state, string statusText)
    {
        _lastUiState = state;
        _lastStatusText = statusText;

        lock (_uiLock)
        {
            _applyUiState?.Invoke(state, statusText);
        }
    }

    private void Log(string line)
    {
        lock (_uiLock)
        {
            _log?.Invoke(line);
        }
    }

    public void Dispose()
    {
        try { _lifetimeCts?.Cancel(); } catch { }
        _lifetimeCts = null;

        DetachUi();
        _engine.Dispose();
        _opLock.Dispose();
    }
}
