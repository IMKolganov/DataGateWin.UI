using System.Globalization;
using System.Windows;
using DataGateWin.CrashReporting;
using DataGateWin.Localization;
using DataGateWin.Models.Ipc;
using DataGateWin.Services.Installation;
using DataGateWin.Services.Ipc;
using DataGateWin.Services.IpList;
using DataGateWin.Services.OpenVpnFiles;
using DataGateWin.Services.VpnServers;
using DataGateWin.Services.Xray;

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
    private Action<UiState, string, VpnConnectionSessionInfo?>? _applyUiState;
    private Action<string>? _log;

    private UiState _lastUiState = UiState.Idle;
    private string _lastStatusText = Loc.T("Home_Status_Idle");
    private VpnConnectionSessionInfo? _sessionInfo;

    public HomeController()
    {
        var serversApi = new OpenVpnServersApiClient(App.AuthedApiHttp);
        var selector = new WssServerSelector(serversApi);

        var installation = new InstallationIdService();
        var filesApi = new OpenVpnFilesApiClient(App.AuthedApiHttp);
        var xrayFilesApi = new XrayClientLinksApiClient(App.AuthedApiHttp);

        var payloadBuilder = new StartSessionPayloadBuilder(
            wssServerSelector: selector,
            installationIdService: installation,
            filesApi: filesApi,
            xrayFilesApi: xrayFilesApi,
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
        Action<UiState, string, VpnConnectionSessionInfo?> uiStateApplier,
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
        // Page navigation must not reset VPN intent / session identity.
        // Only ensure a live CTS for future connect/disconnect/reconnect work.
        if (_lifetimeCts == null || _lifetimeCts.IsCancellationRequested)
            _lifetimeCts = new CancellationTokenSource();

        var ct = _lifetimeCts.Token;
        var returningToPage =
            _lastUiState is UiState.Connected or UiState.Connecting or UiState.Disconnecting
            || _sessionInfo is { HasIdentity: true }
            || _desiredConnected;

        try
        {
            if (!returningToPage)
                ApplyUiState(UiState.Connecting, Loc.T("Home_Status_Attaching"));
            else
                ReapplyUiToLastState();

            await _engine.AttachOrStartAsync(ct);
            RememberSelectionFromEngine();
            await RefreshStatusAsync(ct);
        }
        catch (Exception ex)
        {
            if (TryHandleEngineMissing(ex))
                return;

            CrashReporter.ReportNonFatal(ex, "HomeController.OnLoaded");
            Log(Loc.T("Home_Log_ErrorFmt", ex));
            // Always surface attach failure (including return-to-Home), so we never leave a
            // stale Connected UI when the engine IPC is dead.
            ApplyUiState(UiState.Idle, Loc.T("Home_Status_AttachFailedFmt", ex.Message));
        }
    }

    public void OnUnloaded()
    {
        // Keep controller CTS + session info alive across menu navigation.
        // Cancelling here used to kill reconnect and force a cold "Attaching" reset on return.
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
            if (EngineState.IsUnknown(state))
            {
                Log(Loc.T("Home_Log_ErrorFmt", "GetStatus failed"));
                ApplyUiState(UiState.Idle, Loc.T("Home_Status_IdleStartFailed"));
                if (_desiredConnected)
                    _ = ScheduleReconnectAsync();
                return;
            }

            if (EngineState.IsLiveSession(state) && !EngineState.NeedsCleanup(state))
            {
                RememberSelectionFromEngine();
                if (EngineState.IsConnected(state))
                {
                    ApplyUiState(UiState.Connected, ConnectedStatusText(state));
                    return;
                }

                ApplyUiState(UiState.Connecting, Loc.T("Home_Status_ConnectingWaiting"));
                return;
            }

            if (!EngineState.IsIdle(state))
            {
                ApplyUiState(UiState.Disconnecting, Loc.T("Home_Status_Disconnecting"));
                var stopped = await _engine.StopSessionSafeAsync(ct);
                ClearSessionInfo();
                var afterStop = await _engine.GetEngineStateAsync(ct);
                if (!stopped || (!EngineState.IsUnknown(afterStop) && !EngineState.IsIdle(afterStop)))
                {
                    Log(Loc.T("Home_Log_ErrorFmt", $"StopSession incomplete (ok={stopped}, state={afterStop ?? "null"})"));
                    ApplyUiState(UiState.Idle, Loc.T("Home_Status_IdleStartFailed"));
                    if (_desiredConnected)
                        _ = ScheduleReconnectAsync();
                    return;
                }

                ApplyUiState(UiState.Connecting, Loc.T("Home_Status_Connecting"));
            }

            var started = await _engine.StartSessionAsync(_connectAutoPick, _connectManualId, ct);
            if (!started)
            {
                ClearSessionInfo();
                if (_engine.LastStartFailedNoEligibleServers)
                {
                    Log(Loc.T("Home_Log_NoWss"));
                    _desiredConnected = false;
                }

                ApplyUiState(UiState.Idle, Loc.T("Home_Status_IdleStartFailed"));
                if (_desiredConnected)
                    _ = ScheduleReconnectAsync();
                return;
            }

            RememberSelectionFromEngine();
            ApplyUiState(UiState.Connecting, Loc.T("Home_Status_ConnectingWaiting"));
            _reconnectAttempt = 0;
        }
        catch (Exception ex)
        {
            if (TryHandleEngineMissing(ex))
                return;

            CrashReporter.ReportNonFatal(ex, "HomeController.EnsureConnected");
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
            ClearSessionInfo();

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
            if (!_desiredConnected)
                ClearSessionInfo();
            ApplyUiState(UiState.Idle, Loc.T("Home_Status_IdleNotAttached"));
            return;
        }

        var state = await _engine.GetEngineStateAsync(ct);
        if (EngineState.IsUnknown(state))
        {
            ApplyUiState(UiState.Idle, Loc.T("Home_Status_IdleNotAttached"));
            return;
        }

        if (EngineState.IsIdle(state) || EngineState.NeedsCleanup(state))
        {
            if (!_desiredConnected)
                ClearSessionInfo();
            ApplyUiState(UiState.Idle, Loc.T("Home_Status_Idle"));
            return;
        }

        if (EngineState.IsConnected(state))
        {
            RememberSelectionFromEngine();
            ApplyUiState(UiState.Connected, ConnectedStatusText(state));
            return;
        }

        RememberSelectionFromEngine();
        ApplyUiState(UiState.Connecting, Loc.T("Home_Status_ConnectingWaiting"));
    }

    private bool TryHandleEngineMissing(Exception ex)
    {
        if (!EngineMissingUi.IsEngineMissingException(ex))
            return false;

        EngineMissingUi.ShowDialog(Application.Current?.MainWindow);
        ClearSessionInfo();
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
        catch (OperationCanceledException) { ApplyUiState(UiState.Idle, Loc.T("Home_Status_Idle")); return; }

        if (_desiredConnected)
            await EnsureConnectedAsync();
    }

    private void HandleEngineEvent(EngineEvent ev)
    {
        if (ev.Kind == EngineEventKind.StateChanged)
        {
            if (EngineState.IsIdle(ev.State) || EngineState.NeedsCleanup(ev.State))
            {
                if (HomeSessionUiPolicy.ShouldClearSessionIdentity(_desiredConnected))
                    ClearSessionInfo();

                if (_desiredConnected)
                {
                    ApplyUiState(UiState.Connecting, Loc.T("Home_Status_ConnectingWaiting"));
                    return;
                }

                ApplyUiState(UiState.Idle, Loc.T("Home_Status_Idle"));
                return;
            }

            if (EngineState.IsConnected(ev.State))
            {
                if (_lastUiState == UiState.Connected || _sessionInfo is { HasIdentity: true })
                {
                    ApplyUiState(UiState.Connected, ConnectedStatusText(ev.State));
                    return;
                }

                SetStatusText(Loc.T("Home_Status_StateFmt", ev.State ?? "?"));
                ApplyUiState(UiState.Connected, ConnectedStatusText(ev.State));
                return;
            }

            ApplyUiState(UiState.Connecting, Loc.T("Home_Status_StateFmt", ev.State ?? "?"));
            return;
        }

        if (ev.Kind == EngineEventKind.Connected)
        {
            _reconnectAttempt = 0;
            RememberSelectionFromEngine();
            if (_sessionInfo != null && !string.IsNullOrWhiteSpace(ev.Ip))
                _sessionInfo.VpnIp = ev.Ip.Trim();

            ApplyUiState(UiState.Connected, ConnectedStatusText(null));
            return;
        }

        if (ev.Kind == EngineEventKind.Disconnected)
        {
            var reason = string.IsNullOrWhiteSpace(ev.Reason) ? Loc.T("Common_Unknown") : ev.Reason;
            Log(Loc.T("Home_Log_DisconnectedLineFmt", reason));

            if (_desiredConnected)
            {
                // Keep footer identity while ScheduleReconnect rebuilds the session.
                ApplyUiState(UiState.Connecting, Loc.T("Home_Status_IdleDisconnectedReasonFmt", reason));
                _ = ScheduleReconnectAsync();
                return;
            }

            ClearSessionInfo();
            ApplyUiState(UiState.Idle, Loc.T("Home_Status_IdleDisconnectedReasonFmt", reason));
            return;
        }

        if (ev.Kind == EngineEventKind.Error)
        {
            var msg = string.IsNullOrWhiteSpace(ev.Message) ? Loc.T("Common_Unknown") : ev.Message!;
            Log(Loc.T("Home_Log_ErrorFmt", msg));
            ApplyUiState(UiState.Idle, Loc.T("Home_Status_IdleErrorFmt", msg));
            if (_desiredConnected)
                _ = ScheduleReconnectAsync();
            return;
        }

        if (ev.Kind == EngineEventKind.EngineExited)
        {
            Log(Loc.T("Home_Log_ErrorFmt", $"engine exit code={ev.ExitCode}"));
            ClearSessionInfo();
            ApplyUiState(UiState.Idle, Loc.T("Home_Status_IdleErrorFmt", $"engine exit {ev.ExitCode}"));
            if (_desiredConnected)
                _ = ScheduleReconnectAsync();
        }
    }

    private void RememberSelectionFromEngine()
    {
        var sel = _engine.LastSelection;
        if (sel == null)
            return;

        // Preserve tunnel IP across payload rebuilds during reconnect.
        var previousVpnIp = _sessionInfo?.VpnIp;
        _sessionInfo = new VpnConnectionSessionInfo
        {
            ServerId = sel.ServerId,
            ServerName = sel.ServerName,
            ExternalIp = sel.ExternalIp,
            VpnIp = !string.IsNullOrWhiteSpace(sel.VpnIp) ? sel.VpnIp : previousVpnIp,
        };
    }

    private void ClearSessionInfo()
    {
        _sessionInfo = null;
        _engine.ClearLastSelection();
    }

    private string ConnectedStatusText(string? engineState) =>
        HomeSessionUiPolicy.ComposeConnectedStatus(
            serverName: _sessionInfo?.ServerName,
            vpnIp: _sessionInfo?.VpnIp,
            engineState: engineState,
            lastStatusText: _lastStatusText,
            lastWasConnected: _lastUiState == UiState.Connected,
            connectedPlain: Loc.T("Home_Status_Connected"),
            connectedServerFmt: Loc.T("Home_Status_ConnectedServerFmt"),
            connectedIpFmt: Loc.T("Home_Status_ConnectedIpFmt"),
            connectedFmt: Loc.T("Home_Status_ConnectedFmt"));

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

        var network = state is UiState.Connected or UiState.Connecting or UiState.Disconnecting
            ? _sessionInfo
            : null;

        lock (_uiLock)
        {
            _applyUiState?.Invoke(state, statusText, network);
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
        try { _lifetimeCts?.Cancel(); } catch (Exception ex) { CrashReporter.ReportNonFatal(ex, "HomeController.DisposeCancel"); }
        _lifetimeCts = null;

        DetachUi();
        _engine.Dispose();
        _opLock.Dispose();
    }
}
