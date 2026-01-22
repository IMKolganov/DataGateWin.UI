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

    private EngineIpcClient? _client;
    private CancellationTokenSource? _cts;

    private UiState _state = UiState.Idle;

    private bool _desiredConnected;
    private int _reconnectAttempt;
    private readonly object _gate = new();

    public HomePage()
    {
        InitializeComponent();
        ApplyState(UiState.Idle, "Idle");
        AppendLog("UI ready.");
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

    private async Task EnsureConnectedAsync()
    {
        UiState current;
        lock (_gate) current = _state;

        if (current is UiState.Connecting or UiState.Connected)
            return;

        ApplyState(UiState.Connecting, "Connecting...");

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            // var sessionId = $"ui-{Guid.NewGuid():N}".Substring(0, 12);
            //for dev
            var sessionId = "dev";

            var engineExePath = ResolveEngineExePath();
            if (!File.Exists(engineExePath))
                throw new FileNotFoundException("Engine executable not found.", engineExePath);

            _client?.Dispose();
            _client = new EngineIpcClient(engineExePath, sessionId);

            _client.EngineLogReceived += (_, line) =>
            {
                Dispatcher.InvokeAsync(() => AppendLog(line));
            };

            _client.EngineExited += (_, code) =>
            {
                Dispatcher.InvokeAsync(async () =>
                {
                    AppendLog($"Engine exited with code: {code}");
                    ApplyState(UiState.Idle, $"Idle (engine exited: {code})");

                    if (_desiredConnected)
                        await ScheduleReconnectAsync();
                });
            };

            _client.EventReceived += (_, ev) =>
            {
                Dispatcher.InvokeAsync(() => HandleEngineEvent(ev));
            };

            await _client.StartOrAttachAsync(_cts.Token);

            AppendLog("Engine connected.");
            _reconnectAttempt = 0;

            // --- Start VPN session ---
            // You MUST provide correct payload for StartSession.
            // Example below reads ovpn content from a local file and sends StartSession.

            var startPayload = await BuildStartSessionPayloadAsync(_cts.Token);
            if (startPayload == null)
            {
                ApplyState(UiState.Idle, "Idle (missing StartSession config)");
                if (_desiredConnected)
                    await ScheduleReconnectAsync();
                return;
            }

            using (var startCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
            {
                startCts.CancelAfter(TimeSpan.FromSeconds(15));

                var startReply = await _client.SendCommandAsync(
                    type: "StartSession",
                    payloadJson: startPayload.ToString(Newtonsoft.Json.Formatting.None),
                    startCts.Token);

                if (!startReply.Ok)
                {
                    ApplyState(UiState.Idle, $"Idle (start failed: {startReply.Code ?? "?"})");
                    AppendLog($"StartSession failed: {startReply.Code ?? "?"} - {startReply.Message ?? "?"}");

                    if (_desiredConnected)
                        await ScheduleReconnectAsync();
                    return;
                }
            }

            ApplyState(UiState.Connected, "Connected");
        }
        catch (Exception ex)
        {
            ApplyState(UiState.Idle, $"Idle (error: {ex.Message})");
            AppendLog($"ERROR: {ex}");

            if (_desiredConnected)
                await ScheduleReconnectAsync();
        }
    }

    private async Task EnsureDisconnectedAsync(bool userInitiated)
    {
        UiState current;
        lock (_gate) current = _state;

        if (current is UiState.Idle or UiState.Disconnecting)
        {
            ApplyState(UiState.Idle, "Idle");
            return;
        }

        ApplyState(UiState.Disconnecting, "Disconnecting...");

        try
        {
            if (_client != null)
            {
                using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(_cts?.Token ?? CancellationToken.None);
                stopCts.CancelAfter(TimeSpan.FromSeconds(10));

                var reply = await _client.SendCommandAsync(
                    type: "StopSession",
                    payloadJson: "{}",
                    stopCts.Token);

                if (!reply.Ok)
                    AppendLog($"StopSession failed: {reply.Code ?? "?"} - {reply.Message ?? "?"}");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Disconnect error: {ex}");
        }
        finally
        {
            try { _cts?.Cancel(); } catch { }
            try { _client?.Dispose(); } catch { }
            _client = null;

            ApplyState(UiState.Idle, userInitiated ? "Idle" : "Idle (disconnected)");
        }
    }

    private async Task ScheduleReconnectAsync()
    {
        UiState current;
        lock (_gate) current = _state;

        if (!_desiredConnected)
            return;

        if (current == UiState.Connecting)
            return;

        _reconnectAttempt++;

        var delay = GetReconnectDelay(_reconnectAttempt);
        ApplyState(UiState.Connecting, $"Reconnecting in {delay.TotalSeconds:0}s...");
        AppendLog($"Reconnect scheduled. Attempt={_reconnectAttempt}, Delay={delay.TotalSeconds:0}s");

        try
        {
            await Task.Delay(delay);
        }
        catch
        {
            ApplyState(UiState.Idle, "Idle");
            return;
        }

        if (_desiredConnected)
            await EnsureConnectedAsync();
    }

    private static TimeSpan GetReconnectDelay(int attempt)
    {
        // Simple capped exponential backoff: 2, 4, 8, 15, 15, ...
        var seconds = attempt switch
        {
            <= 1 => 2,
            2 => 4,
            3 => 8,
            _ => 15
        };
        return TimeSpan.FromSeconds(seconds);
    }

    private void HandleEngineEvent(Models.Ipc.IpcEvent ev)
    {
        if (string.IsNullOrWhiteSpace(ev.Type))
            return;

        // Typical events from your engine:
        // EngineReady, StateChanged, Log, Error, Connected, Disconnected

        if (ev.Type.Equals("Log", StringComparison.OrdinalIgnoreCase))
        {
            var line = ev.Payload?["line"]?.ToString();
            if (!string.IsNullOrWhiteSpace(line))
                AppendLog(line);
            return;
        }

        if (ev.Type.Equals("StateChanged", StringComparison.OrdinalIgnoreCase))
        {
            var state = ev.Payload?["state"]?.ToString();
            if (!string.IsNullOrWhiteSpace(state))
                StatusText.Text = $"State: {state}";
            return;
        }

        if (ev.Type.Equals("Error", StringComparison.OrdinalIgnoreCase))
        {
            var code = ev.Payload?["code"]?.ToString() ?? "?";
            var message = ev.Payload?["message"]?.ToString() ?? "?";
            AppendLog($"ENGINE ERROR: {code} - {message}");
            return;
        }

        if (ev.Type.Equals("Connected", StringComparison.OrdinalIgnoreCase))
        {
            var ip = ev.Payload?["vpnIpv4"]?.ToString();
            if (!string.IsNullOrWhiteSpace(ip))
                ApplyState(UiState.Connected, $"Connected ({ip})");
            else
                ApplyState(UiState.Connected, "Connected");
            return;
        }

        if (ev.Type.Equals("Disconnected", StringComparison.OrdinalIgnoreCase))
        {
            var reason = ev.Payload?["reason"]?.ToString() ?? "Unknown";
            AppendLog($"Disconnected: {reason}");
            ApplyState(UiState.Idle, $"Idle (disconnected: {reason})");

            if (_desiredConnected)
                _ = ScheduleReconnectAsync();

            return;
        }

        StatusText.Text = $"Event: {ev.Type}";
    }

    private void ApplyState(UiState newState, string statusText)
    {
        lock (_gate) _state = newState;

        Dispatcher.InvokeAsync(() =>
        {
            StatusText.Text = statusText;

            var isBusy = newState is UiState.Connecting or UiState.Disconnecting;

            ConnectButton.IsEnabled = !isBusy && newState != UiState.Connected;
            DisconnectButton.IsEnabled = !isBusy && newState == UiState.Connected;
        });
    }

    private void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

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
        // TODO: Replace with your real config source (appsettings / UI inputs).
        // Minimal fields required by your engine:
        // - ovpnContent
        // - host, port, path, listenIp, listenPort
        //
        // Optional:
        // - sni, verifyServerCert, authorizationHeader

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        var ovpnPath = Path.Combine(baseDir, "ovpnfiles", "test-win-wss.ovpn");
        if (!File.Exists(ovpnPath))
            return null;

        var ovpnContent = await File.ReadAllTextAsync(ovpnPath, ct);

        var payload = new JObject
        {
            ["ovpnContent"] = ovpnContent,

            // Example bridge settings (replace with your real values):
            ["host"] = "dev-s1.datagateapp.com",
            ["port"] = "443",
            ["path"] = "/api/proxy",
            ["sni"] = "dev-s1.datagateapp.com",
            ["listenIp"] = "127.0.0.1",
            ["listenPort"] = 18080,
            ["verifyServerCert"] = false,

            // If you need auth for WSS:
            // ["authorizationHeader"] = "Bearer ..."
        };

        return payload;
    }
}
