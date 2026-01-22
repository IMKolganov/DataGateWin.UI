using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using DataGateWin.Models.Ipc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace DataGateWin.Ipc;

public sealed class EngineIpcClient(string engineExePath, string sessionId) : IDisposable
{
    private Process? _process;
    private bool _ownsProcess;

    private NamedPipeClientStream? _controlPipe;
    private NamedPipeClientStream? _eventsPipe;

    private StreamReader? _controlReader;
    private StreamWriter? _controlWriter;
    private StreamReader? _eventsReader;

    private readonly ConcurrentDictionary<string, TaskCompletionSource<IpcReply>> _pending = new();

    private CancellationTokenSource? _internalCts;
    private Task? _controlReadLoop;
    private Task? _eventsReadLoop;

    private string? _lastOut;
    private string? _lastErr;

    public event EventHandler<IpcEvent>? EventReceived;
    public event EventHandler<int>? EngineExited;
    public event EventHandler<string>? EngineLogReceived;

    public async Task StartOrAttachAsync(CancellationToken ct)
    {
        if (_internalCts != null)
            throw new InvalidOperationException("Already started/connected.");

        _internalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Try attach for up to 3 seconds
        if (await TryConnectExistingAsync(totalTimeoutMs: 3000, ct).ConfigureAwait(false))
        {
            _ownsProcess = false;
            return;
        }


        // Start engine and connect
        await StartEngineProcessAsync(ct).ConfigureAwait(false);

        // Small delay to detect instant exit and avoid misleading pipe timeout.
        await Task.Delay(150, ct).ConfigureAwait(false);
        if (_process!.HasExited)
        {
            throw new InvalidOperationException(
                $"Engine exited early. ExitCode={_process.ExitCode}. LastOut={_lastOut ?? "<null>"}. LastErr={_lastErr ?? "<null>"}");
        }

        await ConnectAsync(timeoutMs: 5000, ct).ConfigureAwait(false);
        _ownsProcess = true;
    }

    public async Task<bool> TryConnectExistingAsync(int totalTimeoutMs, CancellationToken ct)
    {
        var deadline = Environment.TickCount + totalTimeoutMs;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            CleanupPipesOnly(); // important: new client objects for each attempt

            var controlName = $"datagate.engine.{sessionId}.control";
            var eventsName = $"datagate.engine.{sessionId}.events";

            _controlPipe = new NamedPipeClientStream(".", controlName, PipeDirection.InOut, PipeOptions.Asynchronous);
            _eventsPipe = new NamedPipeClientStream(".", eventsName, PipeDirection.In, PipeOptions.Asynchronous);

            var remaining = unchecked(deadline - Environment.TickCount);
            if (remaining <= 0)
                return false;

            // small slices to allow looping
            var slice = Math.Min(300, remaining);

            try
            {
                // Try control first
                await _controlPipe.ConnectAsync(slice, ct).ConfigureAwait(false);

                remaining = unchecked(deadline - Environment.TickCount);
                if (remaining <= 0) return false;

                slice = Math.Min(300, remaining);

                // Then events
                await _eventsPipe.ConnectAsync(slice, ct).ConfigureAwait(false);

                // Connected to both -> init streams and loops
                _controlReader = new StreamReader(_controlPipe, Encoding.UTF8, false, 4096, leaveOpen: true);
                _controlWriter = new StreamWriter(_controlPipe, new UTF8Encoding(false), 4096, leaveOpen: true)
                    { AutoFlush = true };
                _eventsReader = new StreamReader(_eventsPipe, Encoding.UTF8, false, 4096, leaveOpen: true);

                _controlReadLoop = Task.Run(() => ControlReadLoopAsync(_internalCts!.Token), _internalCts!.Token);
                _eventsReadLoop = Task.Run(() => EventsReadLoopAsync(_internalCts!.Token), _internalCts!.Token);

                return true;
            }
            catch (TimeoutException)
            {
                // retry until total timeout
            }
            catch (IOException)
            {
                // pipe not ready / server not created instance yet
            }
        }
    }

    private async Task ConnectAsync(int timeoutMs, CancellationToken ct)
    {
        var controlName = $"datagate.engine.{sessionId}.control";
        var eventsName = $"datagate.engine.{sessionId}.events";

        _controlPipe = new NamedPipeClientStream(".", controlName, PipeDirection.InOut, PipeOptions.Asynchronous);
        _eventsPipe = new NamedPipeClientStream(".", eventsName, PipeDirection.In, PipeOptions.Asynchronous);

        await ConnectOrFailFastAsync(_controlPipe, controlName, timeoutMs, ct).ConfigureAwait(false);
        await ConnectOrFailFastAsync(_eventsPipe, eventsName, timeoutMs, ct).ConfigureAwait(false);

        _controlReader = new StreamReader(_controlPipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        _controlWriter = new StreamWriter(_controlPipe, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
        _eventsReader = new StreamReader(_eventsPipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        _controlReadLoop = Task.Run(() => ControlReadLoopAsync(_internalCts!.Token), _internalCts!.Token);
        _eventsReadLoop = Task.Run(() => EventsReadLoopAsync(_internalCts!.Token), _internalCts!.Token);
    }

    private async Task StartEngineProcessAsync(CancellationToken ct)
    {
        if (_process != null)
            throw new InvalidOperationException("Engine already started.");

        var engineDir = Path.GetDirectoryName(engineExePath)
            ?? throw new InvalidOperationException("Engine directory not resolved.");

        _process = new Process
        {
            StartInfo =
            {
                FileName = engineExePath,
                WorkingDirectory = engineDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            },
            EnableRaisingEvents = true
        };

        _process.StartInfo.ArgumentList.Clear();
        _process.StartInfo.ArgumentList.Add("--session-id");
        _process.StartInfo.ArgumentList.Add(sessionId);

        _process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            _lastOut = e.Data;
            EngineLogReceived?.Invoke(this, e.Data);
        };

        _process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            _lastErr = e.Data;
            EngineLogReceived?.Invoke(this, e.Data);
        };

        _process.Exited += (_, _) =>
        {
            EngineExited?.Invoke(this, _process?.ExitCode ?? -1);
            _internalCts?.Cancel();
        };

        if (!_process.Start())
            throw new InvalidOperationException("Failed to start engine process.");

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task ConnectOrFailFastAsync(
        NamedPipeClientStream pipe,
        string pipeName,
        int timeoutMs,
        CancellationToken ct)
    {
        var start = Environment.TickCount;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (_process != null && _process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Engine exited while connecting pipes. ExitCode={_process.ExitCode}. LastOut={_lastOut ?? "<null>"}. LastErr={_lastErr ?? "<null>"}");
            }

            var elapsed = unchecked(Environment.TickCount - start);
            if (elapsed >= timeoutMs)
                throw new TimeoutException($"Pipe connect timed out: {pipeName}");

            var slice = Math.Min(250, timeoutMs - elapsed);
            try
            {
                await pipe.ConnectAsync(slice, ct).ConfigureAwait(false);
                return;
            }
            catch (TimeoutException)
            {
                // retry
            }
        }
    }

    public async Task<IpcReply> SendCommandAsync(string type, string payloadJson, CancellationToken ct)
    {
        if (_controlWriter == null)
            throw new InvalidOperationException("Not connected.");

        var id = Guid.NewGuid().ToString("N");

        var tcs = new TaskCompletionSource<IpcReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, tcs))
            throw new InvalidOperationException("Failed to register pending request.");

        var cmd = new IpcCommand
        {
            Id = id,
            Type = type,
            Payload = string.IsNullOrWhiteSpace(payloadJson) ? new JObject() : JToken.Parse(payloadJson)
        };

        var line = JsonConvert.SerializeObject(cmd, JsonSettings);
        await _controlWriter.WriteLineAsync(line).ConfigureAwait(false);

        using var reg = ct.Register(() =>
        {
            if (_pending.TryRemove(id, out var pendingTcs))
                pendingTcs.TrySetCanceled(ct);
        });

        return await tcs.Task.ConfigureAwait(false);
    }

    private async Task ControlReadLoopAsync(CancellationToken ct)
    {
        if (_controlReader == null) return;

        while (!ct.IsCancellationRequested)
        {
            var line = await _controlReader.ReadLineAsync().ConfigureAwait(false);
            if (line == null) break;

            IpcReply? reply;
            try { reply = JsonConvert.DeserializeObject<IpcReply>(line, JsonSettings); }
            catch { continue; }

            if (reply?.Id == null) continue;

            if (_pending.TryRemove(reply.Id, out var tcs))
                tcs.TrySetResult(reply);
        }
    }

    private async Task EventsReadLoopAsync(CancellationToken ct)
    {
        if (_eventsReader == null) return;

        while (!ct.IsCancellationRequested)
        {
            var line = await _eventsReader.ReadLineAsync().ConfigureAwait(false);
            if (line == null) break;

            IpcEvent? ev;
            try { ev = JsonConvert.DeserializeObject<IpcEvent>(line, JsonSettings); }
            catch { continue; }

            if (ev?.Type == null) continue;
            EventReceived?.Invoke(this, ev);
        }
    }

    private void CleanupPipesOnly()
    {
        try { _controlPipe?.Dispose(); } catch { }
        try { _eventsPipe?.Dispose(); } catch { }

        _controlPipe = null;
        _eventsPipe = null;
        _controlReader = null;
        _controlWriter = null;
        _eventsReader = null;
    }

    public void Dispose()
    {
        try { _internalCts?.Cancel(); } catch { }

        CleanupPipesOnly();

        try
        {
            if (_ownsProcess && _process != null && !_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch { }

        try { _process?.Dispose(); } catch { }
    }

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        StringEscapeHandling = StringEscapeHandling.Default
    };
}