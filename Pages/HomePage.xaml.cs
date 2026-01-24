using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using DataGateWin.Ipc;
using Newtonsoft.Json.Linq;

namespace DataGateWin.Pages;

public partial class HomePage : Page
{
    private enum UiState
    {
        Idle = 0,
        Connecting = 1,
        Connected = 2,
        Disconnecting = 3
    }

    private readonly object _gate = new();
    private readonly SemaphoreSlim _opLock = new(1, 1);

    private EngineIpcClient? _client;
    private CancellationTokenSource? _lifetimeCts;

    private UiState _state = UiState.Idle;
    private bool _desiredConnected;
    private int _reconnectAttempt;
    private bool _handlersAttached;

    private const string SessionId = "dev";

    public HomePage()
    {
        InitializeComponent();
        ApplyStateSafe(UiState.Idle, "Idle");
        AppendLogSafe("UI ready.");
    }

    private async void HomePage_OnLoaded(object sender, RoutedEventArgs e)
    {
        _lifetimeCts?.Cancel();
        _lifetimeCts = new CancellationTokenSource();

        _desiredConnected = false;

        try
        {
            ApplyStateSafe(UiState.Connecting, "Attaching...");
            await AttachOrReportAsync(_lifetimeCts.Token);
        }
        catch (Exception ex)
        {
            AppendLogSafe($"ERROR: {ex}");
            ApplyStateSafe(UiState.Idle, $"Idle (attach failed: {ex.Message})");
        }
    }

    private void HomePage_OnUnloaded(object sender, RoutedEventArgs e)
    {
        try { _lifetimeCts?.Cancel(); } catch { }
        _lifetimeCts = null;

        _client = null;
        _handlersAttached = false;
    }

    private async void ConnectButton_OnClick(object sender, RoutedEventArgs e)
    {
        _desiredConnected = true;
        await EnsureConnectedAsync();
    }

    private async void DisconnectButton_OnClick(object sender, RoutedEventArgs e)
    {
        _desiredConnected = false;
        await EnsureDisconnectedAsync(userInitiated: true);
    }

    private async Task AttachOrReportAsync(CancellationToken ct)
    {
        var engineExePath = ResolveEngineExePath();
        if (!File.Exists(engineExePath))
            throw new FileNotFoundException("Engine executable not found.", engineExePath);

        _client ??= new EngineIpcClient(engineExePath, SessionId);
        AttachHandlersOnce(_client);

        if (_client.IsConnected)
        {
            AppendLogSafe("Engine already attached.");
            await RefreshStatusFromEngineAsync(ct).ConfigureAwait(false);
            return;
        }

        using var attachCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        attachCts.CancelAfter(TimeSpan.FromSeconds(8));

        var attached = await _client.TryConnectExistingAsync(totalTimeoutMs: 8000, attachCts.Token).ConfigureAwait(false);
        if (!attached)
        {
            var reason = _client.LastAttachError ?? "unknown";
            AppendLogSafe($"Attach failed: {reason}");
            ApplyStateSafe(UiState.Idle, "Idle");
            return;
        }

        AppendLogSafe("Engine attached.");
        await RefreshStatusFromEngineAsync(ct).ConfigureAwait(false);
    }

    private async Task EnsureConnectedAsync()
    {
        var ct = _lifetimeCts?.Token ?? CancellationToken.None;

        await _opLock.WaitAsync(ct);
        try
        {
            UiState current;
            lock (_gate) current = _state;

            if (current is UiState.Connecting or UiState.Connected)
                return;

            ApplyStateSafe(UiState.Connecting, "Connecting...");

            var engineExePath = ResolveEngineExePath();
            if (!File.Exists(engineExePath))
                throw new FileNotFoundException("Engine executable not found.", engineExePath);

            _client ??= new EngineIpcClient(engineExePath, SessionId);
            AttachHandlersOnce(_client);

            // IMPORTANT: do NOT re-attach if we already have pipes connected
            if (!_client.IsConnected)
            {
                using var attachCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attachCts.CancelAfter(TimeSpan.FromSeconds(10));

                var attached = await _client.TryConnectExistingAsync(totalTimeoutMs: 10000, attachCts.Token).ConfigureAwait(false);
                if (!attached)
                {
                    AppendLogSafe($"Attach failed: {_client.LastAttachError ?? "unknown"}. Starting/attaching engine process...");

                    // IMPORTANT: StartOrAttachAsync throws if client was already started before.
                    // Reset the client connection/lifetime before trying to start.
                    _client.ResetConnection();

                    using var startCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    startCts.CancelAfter(TimeSpan.FromSeconds(12));
                    await _client.StartOrAttachAsync(startCts.Token).ConfigureAwait(false);

                    AppendLogSafe("Engine connected.");
                }
                else
                {
                    AppendLogSafe("Engine attached (existing).");
                }
            }
            else
            {
                AppendLogSafe("Engine already attached.");
            }

            _reconnectAttempt = 0;

            var st0 = await GetEngineStateAsync(_client, ct).ConfigureAwait(false);
            if (!IsIdleState(st0))
            {
                ApplyStateSafe(UiState.Connected, $"Connected ({st0 ?? "unknown"})");
                return;
            }

            var startPayload = await BuildStartSessionPayloadAsync(ct).ConfigureAwait(false);
            if (startPayload == null)
            {
                ApplyStateSafe(UiState.Idle, "Idle (missing StartSession config)");
                return;
            }

            using (var startCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                startCts.CancelAfter(TimeSpan.FromSeconds(20));

                var startReply = await _client.SendCommandAsync(
                    type: "StartSession",
                    payloadJson: startPayload.ToString(Newtonsoft.Json.Formatting.None),
                    startCts.Token).ConfigureAwait(false);

                if (!startReply.Ok)
                {
                    ApplyStateSafe(UiState.Idle, $"Idle (start failed: {startReply.Code ?? "?"})");
                    AppendLogSafe($"StartSession failed: {startReply.Code ?? "?"} - {startReply.Message ?? "?"}");

                    if (_desiredConnected)
                        _ = ScheduleReconnectAsync();

                    return;
                }
            }

            ApplyStateSafe(UiState.Connecting, "Connecting (waiting for events)...");
        }
        catch (Exception ex)
        {
            ApplyStateSafe(UiState.Idle, $"Idle (error: {ex.Message})");
            AppendLogSafe($"ERROR: {ex}");

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
            UiState current;
            lock (_gate) current = _state;

            if (current is UiState.Idle or UiState.Disconnecting)
            {
                ApplyStateSafe(UiState.Idle, "Idle");
                return;
            }

            ApplyStateSafe(UiState.Disconnecting, "Disconnecting...");

            if (_client != null)
            {
                try
                {
                    using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    stopCts.CancelAfter(TimeSpan.FromSeconds(20));

                    var reply = await _client.SendCommandAsync(
                        type: "StopSession",
                        payloadJson: "{}",
                        stopCts.Token).ConfigureAwait(false);

                    if (!reply.Ok)
                        AppendLogSafe($"StopSession failed: {reply.Code ?? "?"} - {reply.Message ?? "?"}");
                }
                catch (Exception ex)
                {
                    AppendLogSafe($"Disconnect error: {ex}");
                }
            }

            ApplyStateSafe(UiState.Idle, userInitiated ? "Idle" : "Idle (disconnected)");
        }
        finally
        {
            _opLock.Release();
        }
    }

    private async Task RefreshStatusFromEngineAsync(CancellationToken ct)
    {
        if (_client == null)
        {
            ApplyStateSafe(UiState.Idle, "Idle");
            return;
        }

        if (!_client.IsConnected)
        {
            ApplyStateSafe(UiState.Idle, "Idle (not attached)");
            return;
        }

        var state = await GetEngineStateAsync(_client, ct).ConfigureAwait(false);
        if (IsIdleState(state))
            ApplyStateSafe(UiState.Idle, "Idle");
        else
            ApplyStateSafe(UiState.Connected, $"Connected ({state ?? "unknown"})");
    }

    private static bool IsIdleState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return true;

        return state.Trim().Equals("idle", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> GetEngineStateAsync(EngineIpcClient client, CancellationToken ct)
    {
        using var statusCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        statusCts.CancelAfter(TimeSpan.FromSeconds(5));

        var reply = await client.SendCommandAsync(
            type: "GetStatus",
            payloadJson: "{}",
            statusCts.Token).ConfigureAwait(false);

        if (!reply.Ok)
            return null;

        return reply.Payload?["state"]?.ToString();
    }

    private async Task ScheduleReconnectAsync()
    {
        var ct = _lifetimeCts?.Token ?? CancellationToken.None;

        UiState current;
        lock (_gate) current = _state;

        if (!_desiredConnected)
            return;

        if (current == UiState.Connecting)
            return;

        _reconnectAttempt++;

        var delay = GetReconnectDelay(_reconnectAttempt);
        ApplyStateSafe(UiState.Connecting, $"Reconnecting in {delay.TotalSeconds:0}s...");
        AppendLogSafe($"Reconnect scheduled. Attempt={_reconnectAttempt}, Delay={delay.TotalSeconds:0}s");

        try
        {
            await Task.Delay(delay, ct);
        }
        catch
        {
            ApplyStateSafe(UiState.Idle, "Idle");
            return;
        }

        if (_desiredConnected)
            await EnsureConnectedAsync();
    }

    private static TimeSpan GetReconnectDelay(int attempt)
    {
        var seconds = attempt switch
        {
            <= 1 => 2,
            2 => 4,
            3 => 8,
            _ => 15
        };
        return TimeSpan.FromSeconds(seconds);
    }

    private void AttachHandlersOnce(EngineIpcClient client)
    {
        if (_handlersAttached)
            return;

        _handlersAttached = true;

        client.EngineLogReceived += (_, line) =>
        {
            AppendLogSafe(line);
        };

        client.EngineExited += (_, code) =>
        {
            Dispatcher.InvokeAsync(async () =>
            {
                AppendLogSafe($"Engine exited with code: {code}");
                ApplyStateSafe(UiState.Idle, $"Idle (engine exited: {code})");

                if (_desiredConnected)
                    await ScheduleReconnectAsync();
            });
        };

        client.EventReceived += (_, ev) =>
        {
            Dispatcher.InvokeAsync(() => HandleEngineEvent(ev));
        };
    }

    private void HandleEngineEvent(Models.Ipc.IpcEvent ev)
    {
        if (string.IsNullOrWhiteSpace(ev.Type))
            return;

        if (ev.Type.Equals("Log", StringComparison.OrdinalIgnoreCase))
        {
            var line = ev.Payload?["line"]?.ToString();
            if (!string.IsNullOrWhiteSpace(line))
                AppendLogSafe(line);
            return;
        }

        if (ev.Type.Equals("StateChanged", StringComparison.OrdinalIgnoreCase))
        {
            var state = ev.Payload?["state"]?.ToString();
            if (!string.IsNullOrWhiteSpace(state))
            {
                StatusText.Text = $"State: {state}";
                if (IsIdleState(state))
                    ApplyStateSafe(UiState.Idle, "Idle");
                else
                    ApplyStateSafe(UiState.Connected, $"Connected ({state})");
            }
            return;
        }

        if (ev.Type.Equals("Error", StringComparison.OrdinalIgnoreCase))
        {
            var code = ev.Payload?["code"]?.ToString() ?? "?";
            var message = ev.Payload?["message"]?.ToString() ?? "?";
            AppendLogSafe($"ENGINE ERROR: {code} - {message}");
            return;
        }

        if (ev.Type.Equals("Connected", StringComparison.OrdinalIgnoreCase))
        {
            _reconnectAttempt = 0;

            var ip = ev.Payload?["vpnIpv4"]?.ToString();
            if (!string.IsNullOrWhiteSpace(ip))
                ApplyStateSafe(UiState.Connected, $"Connected ({ip})");
            else
                ApplyStateSafe(UiState.Connected, "Connected");

            return;
        }

        if (ev.Type.Equals("Disconnected", StringComparison.OrdinalIgnoreCase))
        {
            var reason = ev.Payload?["reason"]?.ToString() ?? "Unknown";
            AppendLogSafe($"Disconnected: {reason}");
            ApplyStateSafe(UiState.Idle, $"Idle (disconnected: {reason})");

            if (_desiredConnected)
                _ = ScheduleReconnectAsync();

            return;
        }

        StatusText.Text = $"Event: {ev.Type}";
    }

    private void ApplyStateSafe(UiState newState, string statusText)
    {
        lock (_gate) _state = newState;

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => ApplyStateSafe(newState, statusText));
            return;
        }

        StatusText.Text = statusText;

        var isBusy = newState is UiState.Connecting or UiState.Disconnecting;

        ConnectButton.IsEnabled = !isBusy && newState == UiState.Idle;
        DisconnectButton.IsEnabled = !isBusy && newState == UiState.Connected;
    }

    private void AppendLogSafe(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => AppendLogSafe(line));
            return;
        }

        var sb = new StringBuilder();
        sb.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append("] ");
        sb.Append(line);

        LogTextBox.AppendText(sb.ToString() + Environment.NewLine);
        LogTextBox.ScrollToEnd();
    }

    private static string ResolveEngineExePath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(baseDir, "engine", "engine.exe");
    }

    private static async Task<JObject?> BuildStartSessionPayloadAsync(CancellationToken ct)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        var ovpnPath = Path.Combine(baseDir, "ovpnfiles", "test-win-wss.ovpn");
        if (!File.Exists(ovpnPath))
            return null;

        var ovpnContent = await File.ReadAllTextAsync(ovpnPath, ct);

        var payload = new JObject
        {
            ["ovpnContent"] = ovpnContent,
            ["host"] = "dev-s1.datagateapp.com",
            ["port"] = "443",
            ["path"] = "/api/proxy",
            ["sni"] = "dev-s1.datagateapp.com",
            ["listenIp"] = "127.0.0.1",
            ["listenPort"] = 18080,
            ["verifyServerCert"] = false
        };

        return payload;
    }
}
