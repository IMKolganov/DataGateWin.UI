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

    public bool IsConnected => _controlPipe?.IsConnected == true && _eventsPipe?.IsConnected == true;

    public event EventHandler<IpcEvent>? EventReceived;
    public event EventHandler<int>? EngineExited;
    public event EventHandler<string>? EngineLogReceived;

    public string? LastAttachError { get; private set; }

    private void EnsureInternalLifetime()
    {
        _internalCts ??= new CancellationTokenSource();
    }

    public async Task StartOrAttachAsync(CancellationToken ct)
    {
        if (_internalCts != null)
            throw new InvalidOperationException("Already started/connected.");

        EnsureInternalLifetime();

        if (await TryConnectExistingAsync(totalTimeoutMs: 1000, ct).ConfigureAwait(false))
        {
            _ownsProcess = false;
            return;
        }

        await StartEngineProcessAsync(ct).ConfigureAwait(false);

        await Task.Delay(150, ct).ConfigureAwait(false);
        if (_process!.HasExited)
        {
            throw new InvalidOperationException(
                $"Engine exited early. ExitCode={_process.ExitCode}. LastOut={_lastOut ?? "<null>"}. LastErr={_lastErr ?? "<null>"}");
        }

        await ConnectAsync(timeoutMs: 1000, ct).ConfigureAwait(false);
        _ownsProcess = true;
    }

    public async Task<bool> TryConnectExistingAsync(int totalTimeoutMs, CancellationToken ct)
    {
        EnsureInternalLifetime();

        LastAttachError = null;

        var deadline = Environment.TickCount64 + totalTimeoutMs;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            CleanupPipesOnly();

            var controlName = $"datagate.engine.{sessionId}.control";
            var eventsName = $"datagate.engine.{sessionId}.events";

            _controlPipe = new NamedPipeClientStream(".", controlName, PipeDirection.InOut, PipeOptions.Asynchronous);
            _eventsPipe = new NamedPipeClientStream(".", eventsName, PipeDirection.In, PipeOptions.Asynchronous);

            var remaining = (int)Math.Max(0, deadline - Environment.TickCount64);
            if (remaining <= 0)
            {
                LastAttachError ??= "timeout";
                return false;
            }

            try
            {
                EngineLogReceived?.Invoke(this, $"[ipc][ui] connect TRY control timeoutMs={remaining}");
                await _controlPipe.ConnectAsync(remaining, ct).ConfigureAwait(false);
                EngineLogReceived?.Invoke(this, "[ipc][ui] connect OK control");

                remaining = (int)Math.Max(0, deadline - Environment.TickCount64);
                if (remaining <= 0)
                {
                    LastAttachError = "timeout_after_control";
                    CleanupPipesOnly();
                    return false;
                }

                EngineLogReceived?.Invoke(this, $"[ipc][ui] connect TRY events timeoutMs={remaining}");
                await _eventsPipe.ConnectAsync(remaining, ct).ConfigureAwait(false);
                EngineLogReceived?.Invoke(this, "[ipc][ui] connect OK events");

                InitStreamsAndLoops();
                return true;
            }
            catch (OperationCanceledException)
            {
                LastAttachError = "canceled";
                CleanupPipesOnly();
                throw;
            }
            catch (UnauthorizedAccessException ex)
            {
                LastAttachError = $"access_denied: {ex.Message}";
                CleanupPipesOnly();
                return false;
            }
            catch (TimeoutException)
            {
                LastAttachError = "timeout";
                CleanupPipesOnly();
                return false;
            }
            catch (IOException ex)
            {
                LastAttachError = $"io: {ex.Message}";
                CleanupPipesOnly();

                await Task.Delay(120, ct).ConfigureAwait(false);

                remaining = (int)Math.Max(0, deadline - Environment.TickCount64);
                if (remaining <= 0)
                    return false;
            }
        }
    }

    private void InitStreamsAndLoops()
    {
        if (_internalCts == null)
            throw new InvalidOperationException("Internal lifetime is not initialized.");

        _controlReader = new StreamReader(_controlPipe!, Encoding.UTF8, false, 4096, leaveOpen: true);
        _controlWriter = new StreamWriter(_controlPipe!, new UTF8Encoding(false), 4096, leaveOpen: true)
        {
            AutoFlush = true
        };
        _eventsReader = new StreamReader(_eventsPipe!, Encoding.UTF8, false, 4096, leaveOpen: true);

        EngineLogReceived?.Invoke(this, "[ipc][ui] loops starting");

        _controlReadLoop = Task.Run(() => ControlReadLoopAsync(_internalCts.Token));
        _eventsReadLoop = Task.Run(() => EventsReadLoopAsync(_internalCts.Token));
    }

    private async Task ConnectAsync(int timeoutMs, CancellationToken ct)
    {
        EnsureInternalLifetime();

        var controlName = $"datagate.engine.{sessionId}.control";
        var eventsName = $"datagate.engine.{sessionId}.events";

        _controlPipe = new NamedPipeClientStream(".", controlName, PipeDirection.InOut, PipeOptions.Asynchronous);
        _eventsPipe = new NamedPipeClientStream(".", eventsName, PipeDirection.In, PipeOptions.Asynchronous);

        await ConnectOrFailFastAsync(_controlPipe, "control", timeoutMs, ct).ConfigureAwait(false);
        await ConnectOrFailFastAsync(_eventsPipe, "events", timeoutMs, ct).ConfigureAwait(false);

        InitStreamsAndLoops();
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

            FailAllPending(new IOException("Engine process exited."));
            try { _internalCts?.Cancel(); } catch { }
        };

        if (!_process.Start())
            throw new InvalidOperationException("Failed to start engine process.");

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task ConnectOrFailFastAsync(
        NamedPipeClientStream pipe,
        string label,
        int timeoutMs,
        CancellationToken ct)
    {
        var start = Environment.TickCount64;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (_process != null && _process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Engine exited while connecting pipes. ExitCode={_process.ExitCode}. LastOut={_lastOut ?? "<null>"}. LastErr={_lastErr ?? "<null>"}");
            }

            var elapsed = (int)Math.Max(0, Environment.TickCount64 - start);
            if (elapsed >= timeoutMs)
                throw new TimeoutException($"Pipe connect timed out: {label}");

            var slice = Math.Min(800, timeoutMs - elapsed);

            try
            {
                EngineLogReceived?.Invoke(this, $"[ipc][ui] connect TRY {label} timeoutMs={timeoutMs}");
                await pipe.ConnectAsync(slice, ct).ConfigureAwait(false);
                EngineLogReceived?.Invoke(this, $"[ipc][ui] connect OK {label}");
                return;
            }
            catch (TimeoutException)
            {
            }
            catch (IOException)
            {
                await Task.Delay(120, ct).ConfigureAwait(false);
            }
        }
    }

    public async Task<IpcReply> SendCommandAsync(string type, string payloadJson, CancellationToken ct)
    {
        if (_controlWriter == null)
            throw new InvalidOperationException("Not connected.");

        if (_internalCts?.IsCancellationRequested == true)
            throw new InvalidOperationException("Client lifetime is canceled.");

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

        EngineLogReceived?.Invoke(this, $"[ipc][ui] control send id={id} type={type} bytes={line.Length}");

        try
        {
            await _controlWriter.WriteLineAsync(line).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (_pending.TryRemove(id, out var pendingTcs))
                pendingTcs.TrySetException(ex);
            throw;
        }

        using var reg = ct.Register(() =>
        {
            if (_pending.TryRemove(id, out var pendingTcs))
            {
                EngineLogReceived?.Invoke(this, $"[ipc][ui] pending canceled id={id} type={type}");
                pendingTcs.TrySetCanceled(ct);
            }
        });

        return await tcs.Task.ConfigureAwait(false);
    }

    private async Task ControlReadLoopAsync(CancellationToken ct)
    {
        if (_controlReader == null) return;

        EngineLogReceived?.Invoke(this, "[ipc][ui] control loop started");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line;

                try
                {
                    line = await _controlReader.ReadLineAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    EngineLogReceived?.Invoke(this, $"[ipc][ui] control read failed: {ex.GetType().Name}: {ex.Message}");
                    break;
                }

                if (line == null)
                {
                    EngineLogReceived?.Invoke(this, "[ipc][ui] control loop EOF");
                    break;
                }

                var preview = line.Length <= 200 ? line : line[..200];
                EngineLogReceived?.Invoke(this, $"[ipc][ui] control recv bytes={line.Length} preview={preview}");

                // 1) Try typed deserialize
                IpcReply? reply = null;
                try
                {
                    reply = JsonConvert.DeserializeObject<IpcReply>(line, JsonSettings);
                }
                catch (Exception ex)
                {
                    EngineLogReceived?.Invoke(this, $"[ipc][ui] control recv typed parse failed: {ex.GetType().Name}: {ex.Message}");
                }

                // 2) Fallback: parse as JObject and extract id
                if (reply?.Id == null)
                {
                    try
                    {
                        var jo = JObject.Parse(line);
                        var id = jo["id"]?.Value<string>();

                        EngineLogReceived?.Invoke(this, $"[ipc][ui] control recv fallback id={(id ?? "<null>")}");

                        if (id != null && _pending.TryRemove(id, out var tcs))
                        {
                            // Build minimal IpcReply from raw json so caller gets data.
                            reply ??= new IpcReply();
                            reply.Id = id;

                            // If your IpcReply has these properties, fill them from JSON as well
                            // (safe even if some are absent)
                            reply.Ok = jo["ok"]?.Value<bool>() ?? false;
                            reply.Payload = jo["payload"];

                            var errObj = jo["error"] as JObject;
                            if (errObj == null || errObj.Type == JTokenType.Null)
                            {
                                reply.Error = null;
                            }
                            else
                            {
                                reply.Error = new IpcError
                                {
                                    Code = errObj["code"]?.Value<string>(),
                                    Message = errObj["message"]?.Value<string>()
                                };
                            }

                            EngineLogReceived?.Invoke(this, $"[ipc][ui] pending HIT id={id} (fallback)");
                            tcs.TrySetResult(reply);
                        }
                        else
                        {
                            EngineLogReceived?.Invoke(this, $"[ipc][ui] pending MISS id={(id ?? "<null>")}");
                        }
                    }
                    catch (Exception ex)
                    {
                        EngineLogReceived?.Invoke(this, $"[ipc][ui] control recv fallback parse failed: {ex.GetType().Name}: {ex.Message}");
                    }

                    continue;
                }

                var rid = reply.Id;
                EngineLogReceived?.Invoke(this, $"[ipc][ui] control recv parsed id={rid} ok={reply.Ok}");

                if (_pending.TryRemove(rid, out var tcs2))
                {
                    EngineLogReceived?.Invoke(this, $"[ipc][ui] pending HIT id={rid}");
                    tcs2.TrySetResult(reply);
                }
                else
                {
                    EngineLogReceived?.Invoke(this, $"[ipc][ui] pending MISS id={rid}");
                }
            }
        }
        finally
        {
            EngineLogReceived?.Invoke(this, "[ipc][ui] control loop stopped");
            FailAllPending(new IOException("Control pipe read loop stopped."));
        }
    }

    private async Task EventsReadLoopAsync(CancellationToken ct)
    {
        if (_eventsReader == null) return;

        EngineLogReceived?.Invoke(this, "[ipc][ui] events loop started");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line;

                try
                {
                    line = await _eventsReader.ReadLineAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    EngineLogReceived?.Invoke(this, $"[ipc][ui] events read failed: {ex.GetType().Name}: {ex.Message}");
                    break;
                }

                if (line == null)
                {
                    EngineLogReceived?.Invoke(this, "[ipc][ui] events loop EOF");
                    break;
                }

                IpcEvent? ev;
                try { ev = JsonConvert.DeserializeObject<IpcEvent>(line, JsonSettings); }
                catch
                {
                    EngineLogReceived?.Invoke(this, "[ipc][ui] events recv parse failed");
                    continue;
                }

                if (ev?.Type == null) continue;
                EventReceived?.Invoke(this, ev);
            }
        }
        finally
        {
            EngineLogReceived?.Invoke(this, "[ipc][ui] events loop stopped");
        }
    }

    private void FailAllPending(Exception ex)
    {
        foreach (var kv in _pending.ToArray())
        {
            if (_pending.TryRemove(kv.Key, out var tcs))
                tcs.TrySetException(ex);
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

    public void ResetConnection()
    {
        try { _internalCts?.Cancel(); } catch { }

        FailAllPending(new IOException("Connection reset."));

        CleanupPipesOnly();

        try { _internalCts?.Dispose(); } catch { }
        _internalCts = null;

        _controlReadLoop = null;
        _eventsReadLoop = null;
    }

    public void Dispose()
    {
        try { _internalCts?.Cancel(); } catch { }

        FailAllPending(new ObjectDisposedException(nameof(EngineIpcClient)));

        CleanupPipesOnly();

        try
        {
            if (_ownsProcess && _process != null && !_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch { }

        try { _process?.Dispose(); } catch { }

        try { _internalCts?.Dispose(); } catch { }
        _internalCts = null;
    }

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        StringEscapeHandling = StringEscapeHandling.Default
    };
}
