using System.Globalization;
using System.Windows;
using DataGateWin.Localization;
using DataGateWin.Models.Ipc;
using DataGateWin.Services.Installation;
using DataGateWin.Services.Ipc;
using DataGateWin.Services.IpList;
using DataGateWin.Services.OpenVpnFiles;
using DataGateWin.Services.VpnServers;

namespace DataGateWin.Controllers;

public sealed class HomeController : IDisposable
{
    private readonly SemaphoreSlim _opLock = new(1, 1);

    private CancellationTokenSource? _lifetimeCts;
    private bool _desiredConnected;
    private int _reconnectAttempt;
    private bool _connectAutoPick = true;
    private int? _connectManualId;

    private readonly EngineSessionService _engine;

    private readonly object _uiLock = new();

    private Action<string>? _setStatusText;
    private Action<UiState, string>? _applyUiState;
    private Action<string>? _log;

    private UiState _lastUiState = UiState.Idle;
    private string _lastStatusText = Loc.T("Home_Status_Idle");

    public HomeController()
    {
        var serversApi = new OpenVpnServersApiClient(App.AuthedApiHttp);
        var selector = new WssServerSelector(serversApi);

        var installation = new InstallationIdService();
        var filesApi = new OpenVpnFilesApiClient(App.AuthedApiHttp);

        var payloadBuilder = new StartSessionPayloadBuilder(
            wssServerSelector: selector,
            installationIdService: installation,
            filesApi: filesApi,
            session: App.Session,
            ipListRoutes: new IpListRoutesRepository());

        _engine = new EngineSessionService(
            enginePathResolver: new EnginePathResolver(),
            payloadBuilder: payloadBuilder,
            log: Log,
            onEngineEvent: HandleEngineEvent
        );
    }

    public void AppendLogLine(string line) => Log(line);

    public void ReapplyUiToLastState() => ApplyUiState(_lastUiState, _lastStatusText);

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
        Log(Loc.T("Home_Log_UiAttached"));
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
            ApplyUiState(UiState.Connecting, Loc.T("Home_Status_Attaching"));

            await _engine.AttachOrStartAsync(_lifetimeCts.Token);

            await RefreshStatusAsync(_lifetimeCts.Token);
        }
        catch (Exception ex)
        {
            if (TryHandleEngineMissing(ex))
                return;

            Log(Loc.T("Home_Log_ErrorFmt", ex));
            ApplyUiState(UiState.Idle, Loc.T("Home_Status_AttachFailedFmt", ex.Message));
        }
    }

    public void OnUnloaded()
    {
        try { _lifetimeCts?.Cancel(); } catch { }
        _lifetimeCts = null;

        DetachUi();
    }

    public async Task ConnectAsync(bool autoPickServer, int? manualVpnServerId)
    {
        _desiredConnected = true;
        _connectAutoPick = autoPickServer;
        _connectManualId = manualVpnServerId;
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
            ApplyUiState(UiState.Connecting, Loc.T("Home_Status_Connecting"));

            await _engine.AttachOrStartAsync(ct);

            var state = await _engine.GetEngineStateAsync(ct);
            if (!EngineState.IsIdle(state))
            {
                var label = string.IsNullOrWhiteSpace(state) ? Loc.T("Common_Unknown") : state;
                ApplyUiState(UiState.Connected, Loc.T("Home_Status_ConnectedFmt", label));
                return;
            }

            var started = await _engine.StartSessionAsync(_connectAutoPick, _connectManualId, ct);
            if (!started)
            {
                ApplyUiState(UiState.Idle, Loc.T("Home_Status_IdleStartFailed"));
                if (_desiredConnected)
                    _ = ScheduleReconnectAsync();
                return;
            }

            ApplyUiState(UiState.Connecting, Loc.T("Home_Status_ConnectingWaiting"));
            _reconnectAttempt = 0;
        }
        catch (Exception ex)
        {
            if (TryHandleEngineMissing(ex))
                return;

            ApplyUiState(UiState.Idle, Loc.T("Home_Status_IdleErrorFmt", ex.Message));
            Log(Loc.T("Home_Log_ErrorFmt", ex));

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
            ApplyUiState(UiState.Disconnecting, Loc.T("Home_Status_Disconnecting"));

            await _engine.StopSessionSafeAsync(ct);

            ApplyUiState(
                UiState.Idle,
                userInitiated ? Loc.T("Home_Status_Idle") : Loc.T("Home_Status_IdleDisconnected"));
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
            ApplyUiState(UiState.Idle, Loc.T("Home_Status_IdleNotAttached"));
            return;
        }

        var state = await _engine.GetEngineStateAsync(ct);
        var label = string.IsNullOrWhiteSpace(state) ? Loc.T("Common_Unknown") : state!;
        ApplyUiState(
            EngineState.IsIdle(state) ? UiState.Idle : UiState.Connected,
            EngineState.IsIdle(state) ? Loc.T("Home_Status_Idle") : Loc.T("Home_Status_ConnectedFmt", label)
        );
    }

    private bool TryHandleEngineMissing(Exception ex)
    {
        if (!EngineMissingUi.IsEngineMissingException(ex))
            return false;

        EngineMissingUi.ShowDialog(Application.Current?.MainWindow);
        ApplyUiState(UiState.Idle, Loc.T("Home_Status_EngineMissing"));
        Log(Loc.T("Home_Log_EngineMissing"));
        _desiredConnected = false;
        _reconnectAttempt = 0;
        return true;
    }

    private async Task ScheduleReconnectAsync()
    {
        var ct = _lifetimeCts?.Token ?? CancellationToken.None;

        if (!_desiredConnected)
            return;

        _reconnectAttempt++;
        var delay = ReconnectPolicy.GetDelay(_reconnectAttempt);

        ApplyUiState(
            UiState.Connecting,
            Loc.T("Home_Status_ReconnectingFmt", delay.TotalSeconds.ToString("0", CultureInfo.CurrentCulture)));
        Log(Loc.T(
            "Home_Log_ReconnectScheduledFmt",
            _reconnectAttempt.ToString(CultureInfo.InvariantCulture),
            delay.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)));

        try { await Task.Delay(delay, ct); }
        catch { ApplyUiState(UiState.Idle, Loc.T("Home_Status_Idle")); return; }

        if (_desiredConnected)
            await EnsureConnectedAsync();
    }

    private void HandleEngineEvent(EngineEvent ev)
    {
        if (ev.Kind == EngineEventKind.StateChanged)
        {
            SetStatusText(Loc.T("Home_Status_StateFmt", ev.State ?? "?"));
            var label = string.IsNullOrWhiteSpace(ev.State) ? Loc.T("Common_Unknown") : ev.State;
            ApplyUiState(
                EngineState.IsIdle(ev.State) ? UiState.Idle : UiState.Connected,
                EngineState.IsIdle(ev.State) ? Loc.T("Home_Status_Idle") : Loc.T("Home_Status_ConnectedFmt", label)
            );
            return;
        }

        if (ev.Kind == EngineEventKind.Connected)
        {
            _reconnectAttempt = 0;
            ApplyUiState(
                UiState.Connected,
                string.IsNullOrWhiteSpace(ev.Ip)
                    ? Loc.T("Home_Status_Connected")
                    : Loc.T("Home_Status_ConnectedIpFmt", ev.Ip)
            );
            return;
        }

        if (ev.Kind == EngineEventKind.Disconnected)
        {
            var reason = string.IsNullOrWhiteSpace(ev.Reason) ? Loc.T("Common_Unknown") : ev.Reason;
            Log(Loc.T("Home_Log_DisconnectedLineFmt", reason));
            ApplyUiState(UiState.Idle, Loc.T("Home_Status_IdleDisconnectedReasonFmt", reason));

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
