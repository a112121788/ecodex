using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ECode.Core.Config;

namespace ECode.Core.IPC;

/// <summary>
/// 连接并控制 ecode-daemon 守护进程的客户端，负责会话生命周期管理和双向通信。
/// </summary>
public sealed class DaemonClient : IDisposable
{
    private static string PipeName => CompatibilityOptions.NewDaemonPipe;
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private volatile bool _connected; // 在管道和读取器就绪后设置
    private CancellationTokenSource? _listenCts;
    private volatile bool _disposed;

    // 同步：同一时间只允许一个请求，监听循环通过 TCS 回传响应
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private TaskCompletionSource<DaemonResponse?>? _pendingResponse;

    public bool IsConnected => _pipe?.IsConnected == true && _connected;

    public event Action<string, byte[]>? RawOutputReceived;  // paneId，VT 字节
    public event Action<string, int>? SessionExited;          // paneId，退出码
    public event Action<string, string>? TitleChanged;        // paneId，标题
    public event Action<string, string>? CwdChanged;          // paneId，目录
    public event Action<string>? BellReceived;                // paneId
    public event Action? Connected;
    public event Action? Disconnected;

    /// <summary>
    /// 同步尝试连接到守护进程管道。
    /// 必须从后台线程调用（非 UI 线程）。
    /// 连接成功返回 true。
    /// </summary>
    public bool TryConnect(int timeoutMs = 300)
    {
        if (IsConnected) return true;

        // 先尝试新管道 `ecode-daemon`；兼容期回退到旧管道 `cmux-daemon`。
        var candidates = new List<string> { PipeName };
        if (CompatibilityOptions.ShouldListenLegacyDaemonPipe())
        {
            candidates.Add(CompatibilityOptions.LegacyDaemonPipe);
        }

        foreach (var name in candidates)
        {
            try
            {
                // 使用 PipeOptions.Asynchronous（重叠 I/O），使读写可以在同一句柄上并发进行。
                var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
                LogDaemon($"[Connect] Calling pipe.Connect({timeoutMs}) on '{name}'...");
                pipe.Connect(timeoutMs);
                LogDaemon($"[Connect] pipe.Connect returned OK, IsConnected={pipe.IsConnected}");

                _pipe = pipe;
                _reader = new StreamReader(_pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
                _connected = true;
                StartListening();
                LogDaemon("[Connect] Pipe connected, reader ready.");
                return true;
            }
            catch (TimeoutException)
            {
                LogDaemon($"[Connect] Timeout after {timeoutMs}ms on '{name}'");
            }
            catch (Exception ex)
            {
                LogDaemon($"[Connect] Error on '{name}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        return false;
    }

    /// <summary>触发 Connected 事件。TryConnect 成功后调用。</summary>
    public void RaiseConnected() => Connected?.Invoke();

    /// <summary>
    /// 启动守护进程并等待其可用。
    /// 必须从后台线程调用。
    /// </summary>
    public bool StartDaemonAndConnect(int maxRetries = 20, int retryDelayMs = 500)
    {
        try
        {
            var exePath = FindDaemonExecutable();
            LogDaemon($"[TryStart] FindDaemonExecutable: {exePath ?? "(null)"}");
            if (exePath == null) return false;

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            var proc = Process.Start(psi);
            LogDaemon($"[TryStart] Process.Start: pid={proc?.Id}, exited={proc?.HasExited}");

            if (proc == null)
            {
                LogDaemon("[TryStart] Process.Start returned null");
                return false;
            }

            // 重试连接直到守护进程就绪
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                // 先尝试连接，再休眠（守护进程可能已就绪）
                if (TryConnect(1000))
                {
                    LogDaemon($"[TryStart] Connected on attempt {attempt + 1}");
                    return true;
                }

                // 检查守护进程是否崩溃
                if (proc.HasExited)
                {
                    LogDaemon($"[TryStart] Daemon process exited with code {proc.ExitCode}");
                    return false;
                }

                LogDaemon($"[TryStart] Attempt {attempt + 1}/{maxRetries} — not yet connectable, waiting {retryDelayMs}ms...");
                Thread.Sleep(retryDelayMs);
            }

            LogDaemon("[TryStart] All attempts failed");
            return false;
        }
        catch (Exception ex)
        {
            LogDaemon($"[TryStart] Exception: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static string? FindDaemonExecutable()
    {
        var appDir = AppContext.BaseDirectory;
        LogDaemon($"[FindDaemon] AppContext.BaseDirectory: {appDir}");

        // 1. 在当前可执行文件旁查找（部署/发布场景）
        var candidate = Path.Combine(appDir, "ecode-daemon.exe");
        if (File.Exists(candidate)) return candidate;

        // 2. 在兄弟项目构建输出中查找（开发构建场景）
        //    appDir 例如 .../src/ECode/bin/Debug/net10.0-windows10.0.17763.0/
        //    守护进程位于 .../src/ECode.Daemon/bin/Debug/net10.0-windows/ecode-daemon.exe
        try
        {
            var dir = new DirectoryInfo(appDir);
            while (dir != null && !string.Equals(dir.Name, "src", StringComparison.OrdinalIgnoreCase))
                dir = dir.Parent;

            LogDaemon($"[FindDaemon] Traversed to src dir: {dir?.FullName ?? "(null)"}");

            if (dir != null)
            {
                var daemonBin = Path.Combine(dir.FullName, "ECode.Daemon", "bin");
                if (Directory.Exists(daemonBin))
                {
                    var config = appDir.Contains("Release", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";
                    var configDir = Path.Combine(daemonBin, config);
                    if (Directory.Exists(configDir))
                    {
                        foreach (var tfmDir in Directory.GetDirectories(configDir))
                        {
                            candidate = Path.Combine(tfmDir, "ecode-daemon.exe");
                            if (File.Exists(candidate)) return candidate;
                        }
                    }

                    foreach (var exe in Directory.GetFiles(daemonBin, "ecode-daemon.exe", SearchOption.AllDirectories))
                        return exe;
                }
            }
        }
        catch
        {
            // 搜索期间的文件系统错误 — 不关键
        }

        return null;
    }

    private void StartListening()
    {
        _listenCts = new CancellationTokenSource();
        // 使用专用后台线程读取 — 不用 Task.Run —
        // 因为 ReadLine 会阻塞调用线程。
        var thread = new Thread(() => ListenLoop(_listenCts.Token))
        {
            IsBackground = true,
            Name = "DaemonClient-Listen",
        };
        thread.Start();
    }

    private void ListenLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _reader != null)
            {
                var line = _reader.ReadLine();
                if (line == null) break;

                // 先尝试解析为 DaemonResponse（若有待处理请求）
                if (_pendingResponse != null)
                {
                    try
                    {
                        var response = JsonSerializer.Deserialize<DaemonResponse>(line);
                        if (response != null && _pendingResponse != null)
                        {
                            // 响应有 Success 属性；事件有 Type 属性
                            // 检查是否像响应（包含 Success 字段）
                            if (line.Contains("\"Success\"", StringComparison.OrdinalIgnoreCase))
                            {
                                var tcs = _pendingResponse;
                                _pendingResponse = null;
                                tcs.TrySetResult(response);
                                continue;
                            }
                        }
                    }
                    catch { }
                }

                // 尝试作为事件解析
                try
                {
                    var evt = JsonSerializer.Deserialize<DaemonEvent>(line);
                    if (evt != null && !string.IsNullOrEmpty(evt.Type))
                    {
                        DispatchEvent(evt);
                    }
                }
                catch { }
            }
        }
        catch (IOException) { }
        catch (OperationCanceledException) { }
        finally
        {
            _connected = false; // 阻止新请求进入 SendRequestAsync
            _pendingResponse?.TrySetResult(null);
            Disconnected?.Invoke();
        }
    }

    private void DispatchEvent(DaemonEvent evt)
    {
        switch (evt.Type)
        {
            case DaemonMessageTypes.EventOutput:
                if (evt.PaneId != null && evt.Data != null)
                {
                    try
                    {
                        var bytes = Convert.FromBase64String(evt.Data);
                        RawOutputReceived?.Invoke(evt.PaneId, bytes);
                    }
                    catch { }
                }
                break;
            case DaemonMessageTypes.EventExited:
                if (evt.PaneId != null)
                    SessionExited?.Invoke(evt.PaneId, int.TryParse(evt.Data, out var code) ? code : -1);
                break;
            case DaemonMessageTypes.EventTitleChanged:
                if (evt.PaneId != null && evt.Data != null)
                    TitleChanged?.Invoke(evt.PaneId, evt.Data);
                break;
            case DaemonMessageTypes.EventCwdChanged:
                if (evt.PaneId != null && evt.Data != null)
                    CwdChanged?.Invoke(evt.PaneId, evt.Data);
                break;
            case DaemonMessageTypes.EventBell:
                if (evt.PaneId != null)
                    BellReceived?.Invoke(evt.PaneId);
                break;
        }
    }

    /// <summary>
    /// 将 JSON 行作为原始字节直接写入管道。
    /// 避免 StreamWriter.Flush() → FlushFileBuffers 在同步管道上阻塞。
    /// Stream.Write() 立即将数据放入管道缓冲区而不阻塞。
    /// </summary>
    private void WriteToPipe(string line)
    {
        if (_pipe == null) return;
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        _pipe.Write(bytes, 0, bytes.Length);
    }

    private async Task<DaemonResponse?> SendRequestAsync(DaemonRequest request)
    {
        if (!IsConnected || _pipe == null)
        {
            LogDaemon($"[SendRequest] Bail: IsConnected={IsConnected}, pipe={(_pipe != null ? "set" : "null")}");
            return null;
        }

        await _requestLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var tcs = new TaskCompletionSource<DaemonResponse?>();
            _pendingResponse = tcs;

            var json = JsonSerializer.Serialize(request);
            if (request.Type != DaemonMessageTypes.SessionWrite)
                LogDaemon($"[SendRequest] Writing: {json[..Math.Min(json.Length, 200)]}");

            try
            {
                WriteToPipe(json);
            }
            catch (IOException)
            {
                _connected = false;
                _pendingResponse = null;
                return null;
            }

            // 等待监听循环传递响应（3 秒超时）
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            timeoutCts.Token.Register(() => tcs.TrySetResult(null));

            var response = await tcs.Task.ConfigureAwait(false);
            LogDaemon($"[SendRequest] Response: Success={response?.Success}, Error={response?.Error}, DataLen={response?.Data?.Length}");
            return response;
        }
        catch (Exception ex)
        {
            LogDaemon($"[SendRequest] Exception: {ex.GetType().Name}: {ex.Message}");
            _pendingResponse = null;
            return null;
        }
        finally
        {
            _requestLock.Release();
        }
    }

    public static void LogDaemon(string message)
    {
        try
        {
            var logDir = CompatibilityOptions.GetAppDataDir();
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "daemon-debug.log");
            // 使用 FileShare.ReadWrite 使守护进程和客户端可以并发写入
            // 而不互相阻塞（File.AppendAllText 使用 FileShare.Read 会阻塞）。
            using var fs = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var sw = new StreamWriter(fs);
            sw.Write($"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }

    public async Task<DaemonSessionInfo?> CreateSessionAsync(string paneId, int cols, int rows, string? workingDirectory = null, string? command = null)
    {
        var response = await SendRequestAsync(new DaemonRequest
        {
            Type = DaemonMessageTypes.SessionCreate,
            PaneId = paneId,
            Cols = cols,
            Rows = rows,
            WorkingDirectory = workingDirectory,
            Command = command,
        });

        if (response?.Success == true && response.Data != null)
            return JsonSerializer.Deserialize<DaemonSessionInfo>(response.Data);
        return null;
    }

    public async Task WriteAsync(string paneId, byte[] data)
    {
        await SendRequestAsync(new DaemonRequest
        {
            Type = DaemonMessageTypes.SessionWrite,
            PaneId = paneId,
            Data = Convert.ToBase64String(data),
        });
    }

    public async Task WriteAsync(string paneId, string text)
    {
        await WriteAsync(paneId, Encoding.UTF8.GetBytes(text));
    }

    public async Task ResizeAsync(string paneId, int cols, int rows)
    {
        await SendRequestAsync(new DaemonRequest
        {
            Type = DaemonMessageTypes.SessionResize,
            PaneId = paneId,
            Cols = cols,
            Rows = rows,
        });
    }

    public async Task CloseSessionAsync(string paneId)
    {
        await SendRequestAsync(new DaemonRequest
        {
            Type = DaemonMessageTypes.SessionClose,
            PaneId = paneId,
        });
    }

    public async Task<List<DaemonSessionInfo>> ListSessionsAsync()
    {
        var response = await SendRequestAsync(new DaemonRequest
        {
            Type = DaemonMessageTypes.SessionList,
        });

        if (response?.Success == true && response.Data != null)
            return JsonSerializer.Deserialize<List<DaemonSessionInfo>>(response.Data) ?? [];
        return [];
    }

    public async Task<string?> GetSnapshotAsync(string paneId)
    {
        var response = await SendRequestAsync(new DaemonRequest
        {
            Type = DaemonMessageTypes.SessionSnapshot,
            PaneId = paneId,
        });

        return response?.Success == true ? response.Data : null;
    }

    public async Task<bool> PingAsync()
    {
        var response = await SendRequestAsync(new DaemonRequest
        {
            Type = DaemonMessageTypes.Ping,
        });
        return response?.Success == true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connected = false;

        _listenCts?.Cancel();
        _reader?.Dispose();
        _pipe?.Dispose();
        _listenCts?.Dispose();
        _requestLock.Dispose();
    }
}
