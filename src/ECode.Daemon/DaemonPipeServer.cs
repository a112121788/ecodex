using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ECode.Core.Config;
using ECode.Core.IPC;
using static ECode.Core.IPC.DaemonClient;

namespace ECode.Daemon;

/// <summary>
/// 命名管道服务器 —— 监听客户端连接并分发会话管理请求，支持新旧管道协议的兼容过渡
/// </summary>
public sealed class DaemonPipeServer
{
    private static string PipeName => CompatibilityOptions.NewDaemonPipe;
    private static string? LegacyPipeName => CompatibilityOptions.ShouldListenLegacyDaemonPipe()
        ? CompatibilityOptions.LegacyDaemonPipe
        : null;
    private readonly DaemonSessionManager _sessionManager;
    // 每个客户端拥有一个 channel，用于线程安全的事件投递
    private readonly ConcurrentDictionary<string, Channel<string>> _clientChannels = new();
    private int _connectedClients;

    public int ConnectedClients => _connectedClients;

    public event Action? ClientConnected;
    public event Action? ClientDisconnected;

    public DaemonPipeServer(DaemonSessionManager sessionManager)
    {
        _sessionManager = sessionManager;

        _sessionManager.RawOutput += (paneId, data) =>
            BroadcastEvent(new DaemonEvent
            {
                Type = DaemonMessageTypes.EventOutput,
                PaneId = paneId,
                Data = Convert.ToBase64String(data),
            });

        _sessionManager.SessionExited += (paneId, exitCode) =>
            BroadcastEvent(new DaemonEvent { Type = DaemonMessageTypes.EventExited, PaneId = paneId, Data = exitCode.ToString() });

        _sessionManager.TitleChanged += (paneId, title) =>
            BroadcastEvent(new DaemonEvent { Type = DaemonMessageTypes.EventTitleChanged, PaneId = paneId, Data = title });

        _sessionManager.CwdChanged += (paneId, dir) =>
            BroadcastEvent(new DaemonEvent { Type = DaemonMessageTypes.EventCwdChanged, PaneId = paneId, Data = dir });

        _sessionManager.BellReceived += paneId =>
            BroadcastEvent(new DaemonEvent { Type = DaemonMessageTypes.EventBell, PaneId = paneId });
    }

    public void Run(CancellationToken ct)
    {
        var legacy = LegacyPipeName;
        LogDaemon($"[PipeServer] Listening on \\\\.\\pipe\\{PipeName}{(legacy != null ? $" and \\\\.\\pipe\\{legacy}" : string.Empty)}");

        // 同时启动两个 Accept 循环：旧管道放后台线程，新管道在主循环。
        if (legacy != null)
        {
            var legacyThread = new Thread(() => AcceptLoop(legacy, ct))
            {
                IsBackground = true,
                Name = "PipeServer-Accept-Legacy",
            };
            legacyThread.Start();
        }
        AcceptLoop(PipeName, ct);
    }

    private void AcceptLoop(string name, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 使用 PipeOptions.Asynchronous（重叠 I/O），使读写可以并发进行。
                // 若使用 PipeOptions.None，Windows 会将同一句柄上的所有 I/O 串行化，
                // 导致读线程阻塞写线程（或相反）时发生死锁。
                var pipe = new NamedPipeServerStream(
                    name,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                LogDaemon($"[PipeServer] Waiting for client connection on '{name}'...");
                pipe.WaitForConnection();
                LogDaemon($"[PipeServer] Client connected on '{name}', spawning handler...");

                var thread = new Thread(() => HandleConnection(pipe, ct))
                {
                    IsBackground = true,
                    Name = $"PipeServer-Client-{name}",
                };
                thread.Start();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException ex)
            {
                LogDaemon($"[PipeServer] Pipe error on '{name}': {ex.Message}");
                try { Thread.Sleep(100); } catch (ThreadInterruptedException) { break; }
            }
        }
    }

    private void HandleConnection(NamedPipeServerStream pipe, CancellationToken ct)
    {
        var clientId = Guid.NewGuid().ToString("N")[..8];
        // 所有写入（事件 + 响应）共用单个 channel —— 保证对管道的串行写入
        var writeChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        Interlocked.Increment(ref _connectedClients);
        _clientChannels[clientId] = writeChannel;
        ClientConnected?.Invoke();
        LogDaemon($"[PipeServer] Client {clientId} connected (total: {_connectedClients}).");

        try
        {
            using (pipe)
            {
                // 写线程：从 channel 取出数据并将原始字节写入管道。
                var writerThread = new Thread(() =>
                {
                    try
                    {
                        foreach (var json in writeChannel.Reader.ReadAllAsync(ct).ToBlockingEnumerable(ct))
                        {
                            var bytes = Encoding.UTF8.GetBytes(json + "\n");
                            pipe.Write(bytes, 0, bytes.Length);
                        }
                    }
                    catch (IOException) { }
                    catch (OperationCanceledException) { }
                    catch (ObjectDisposedException) { }
                })
                {
                    IsBackground = true,
                    Name = $"PipeServer-Writer-{clientId}",
                };
                writerThread.Start();

                LogDaemon($"[PipeServer:{clientId}] Entering read loop (IsConnected={pipe.IsConnected})...");

                // 从管道读取原始字节并手动解析行。
                // 绕开 StreamReader，因为它在读取命名管道时存在问题。
                var readBuffer = new byte[65536];
                var lineBuffer = new StringBuilder();

                while (!ct.IsCancellationRequested && pipe.IsConnected)
                {
                    int bytesRead;
                    try
                    {
                        bytesRead = pipe.Read(readBuffer, 0, readBuffer.Length);
                    }
                    catch (IOException)
                    {
                        break;
                    }

                    if (bytesRead == 0)
                    {
                        LogDaemon($"[PipeServer:{clientId}] Read returned 0 (EOF)");
                        break;
                    }

                    lineBuffer.Append(Encoding.UTF8.GetString(readBuffer, 0, bytesRead));

                    // 提取完整的行
                    var accumulated = lineBuffer.ToString();
                    int newlineIndex;
                    while ((newlineIndex = accumulated.IndexOf('\n')) >= 0)
                    {
                        var line = accumulated[..newlineIndex];
                        accumulated = accumulated[(newlineIndex + 1)..];

                        if (line.Length > 0)
                        {
                            var response = ProcessRequest(line);
                            writeChannel.Writer.TryWrite(response);
                        }
                    }
                    lineBuffer.Clear();
                    lineBuffer.Append(accumulated);
                }

                writeChannel.Writer.TryComplete();
                writerThread.Join(TimeSpan.FromSeconds(5));
            }
        }
        catch (IOException) { }
        catch (OperationCanceledException) { }
        finally
        {
            writeChannel.Writer.TryComplete();
            _clientChannels.TryRemove(clientId, out _);
            Interlocked.Decrement(ref _connectedClients);
            ClientDisconnected?.Invoke();
            LogDaemon($"[PipeServer] Client {clientId} disconnected (remaining: {_connectedClients}, sessions: {_sessionManager.ActiveSessionCount}).");
        }
    }

    private string ProcessRequest(string requestJson)
    {
        try
        {
            var request = JsonSerializer.Deserialize<DaemonRequest>(requestJson);
            if (request == null)
                return JsonSerializer.Serialize(new DaemonResponse { Success = false, Error = "Invalid request" });

            // 记录非写入类请求（写入请求过于频繁，不记录）
            if (request.Type != DaemonMessageTypes.SessionWrite)
                LogDaemon($"[PipeServer] Request: {request.Type} pane={request.PaneId}");

            return request.Type switch
            {
                DaemonMessageTypes.SessionCreate => HandleSessionCreate(request),
                DaemonMessageTypes.SessionWrite => HandleSessionWrite(request),
                DaemonMessageTypes.SessionResize => HandleSessionResize(request),
                DaemonMessageTypes.SessionClose => HandleSessionClose(request),
                DaemonMessageTypes.SessionList => HandleSessionList(),
                DaemonMessageTypes.SessionSnapshot => HandleSessionSnapshot(request),
                DaemonMessageTypes.Ping => JsonSerializer.Serialize(new DaemonResponse { Success = true, Data = "pong" }),
                _ => JsonSerializer.Serialize(new DaemonResponse { Success = false, Error = $"Unknown command: {request.Type}" }),
            };
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new DaemonResponse { Success = false, Error = ex.Message });
        }
    }

    private string HandleSessionCreate(DaemonRequest request)
    {
        var paneId = request.PaneId ?? throw new ArgumentException("PaneId required");

        var info = _sessionManager.CreateSession(
            paneId,
            request.Cols ?? 120,
            request.Rows ?? 30,
            request.WorkingDirectory,
            request.Command);

        return JsonSerializer.Serialize(new DaemonResponse { Success = true, Data = JsonSerializer.Serialize(info) });
    }

    private string HandleSessionWrite(DaemonRequest request)
    {
        if (request.PaneId == null) throw new ArgumentException("PaneId required");
        var data = request.Data != null ? Convert.FromBase64String(request.Data) : [];
        _sessionManager.WriteToSession(request.PaneId, data);
        return JsonSerializer.Serialize(new DaemonResponse { Success = true });
    }

    private string HandleSessionResize(DaemonRequest request)
    {
        if (request.PaneId == null) throw new ArgumentException("PaneId required");
        _sessionManager.ResizeSession(request.PaneId, request.Cols ?? 120, request.Rows ?? 30);
        return JsonSerializer.Serialize(new DaemonResponse { Success = true });
    }

    private string HandleSessionClose(DaemonRequest request)
    {
        if (request.PaneId == null) throw new ArgumentException("PaneId required");
        _sessionManager.CloseSession(request.PaneId);
        return JsonSerializer.Serialize(new DaemonResponse { Success = true });
    }

    private string HandleSessionList()
    {
        var sessions = _sessionManager.ListSessions();
        return JsonSerializer.Serialize(new DaemonResponse { Success = true, Data = JsonSerializer.Serialize(sessions) });
    }

    private string HandleSessionSnapshot(DaemonRequest request)
    {
        if (request.PaneId == null) throw new ArgumentException("PaneId required");
        var snapshot = _sessionManager.GetSnapshot(request.PaneId);
        return JsonSerializer.Serialize(new DaemonResponse { Success = true, Data = snapshot });
    }

    private void BroadcastEvent(DaemonEvent evt)
    {
        var json = JsonSerializer.Serialize(evt);
        foreach (var kvp in _clientChannels)
        {
            kvp.Value.Writer.TryWrite(json);
        }
    }
}
