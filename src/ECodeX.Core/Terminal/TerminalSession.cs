using System.IO.Pipes;
using System.Text;
using System.Diagnostics;
using ECodeX.Core.IPC;
using Microsoft.Win32.SafeHandles;

namespace ECodeX.Core.Terminal;

/// <summary>
/// 完整的终端会话：ConPTY 进程 + VT 解析器 + 缓冲区 + OSC 处理。
/// 这是 WPF 控件交互的主类。
/// </summary>
public sealed class TerminalSession : IDisposable
{
    private PseudoConsole? _console;
    private TerminalProcess? _process;
    private readonly VtParser _parser;
    private readonly OscHandler _oscHandler;
    private FileStream? _readStream;
    private FileStream? _writeStream;
    private Thread? _readThread;
    private volatile bool _disposed;
    private volatile bool _daemonWriteLogged;
    private volatile bool _localWriteNullLogged;
    private readonly object _lock = new();

    public TerminalBuffer Buffer { get; }
    public string PaneId { get; }
    public string? Title { get; private set; }
    public string? WorkingDirectory { get; set; }
    public bool IsRunning => _process != null && !_process.HasExited;
    public int? ProcessId => _process?.ProcessId;

    // 守护进程模式委托：设置后，Write/Resize 通过这些委托转发，而非本地 ConPTY
    public Func<byte[], Task>? DaemonWrite { get; set; }
    public Func<int, int, Task>? DaemonResize { get; set; }

    // 事件
    public event Action? OutputReceived;
    public event Action? ProcessExited;
    public event Action<string>? TitleChanged;
    public event Action<string>? WorkingDirectoryChanged;
    public event Action<string, string?, string>? NotificationReceived;
    public event Action<char, string?>? ShellPromptMarker;
    public event Action? Redraw;
    public event Action? BellReceived;
    public event Action<byte[]>? RawOutputReceived;

    public TerminalSession(string paneId, int cols = 120, int rows = 30)
    {
        PaneId = paneId;
        Buffer = new TerminalBuffer(cols, rows);
        _parser = new VtParser();
        _oscHandler = new OscHandler();
        WireParser();
    }

    private void WireParser()
    {
        _parser.OnPrint = c => Buffer.WriteChar(c);

        _parser.OnExecute = b =>
        {
            switch (b)
            {
                case 0x07: // BEL（响铃）
                    BellReceived?.Invoke();
                    break;
                case 0x08: // BS（退格）
                    Buffer.Backspace();
                    break;
                case 0x09: // HT（水平制表符）
                    Buffer.Tab();
                    break;
                case 0x0A: // LF（换行）
                case 0x0B: // VT（垂直制表符）
                case 0x0C: // FF（换页）
                    Buffer.LineFeed();
                    break;
                case 0x0D: // CR（回车）
                    Buffer.CarriageReturn();
                    break;
            }
        };

        _parser.OnCsiDispatch = (parameters, final, qualifier) =>
        {
            HandleCsi(parameters, final, qualifier);
        };

        _parser.OnEscDispatch = b =>
        {
            switch ((char)b)
            {
                case '7': // DECSC — 保存光标
                    Buffer.SaveCursor();
                    break;
                case '8': // DECRC — 恢复光标
                    Buffer.RestoreCursor();
                    break;
                case 'M': // RI — 反向索引
                    Buffer.ReverseLineFeed();
                    break;
                case 'D': // IND — 索引（换行）
                    Buffer.LineFeed();
                    break;
                case 'E': // NEL — 下一行
                    Buffer.NewLine();
                    break;
                case 'c': // RIS — 全部重置
                    Buffer.Clear();
                    Buffer.CurrentAttribute = TerminalAttribute.Default;
                    Buffer.ResetScrollRegion();
                    Buffer.MoveCursorTo(0, 0);
                    break;
            }
        };

        _parser.OnOscDispatch = osc =>
        {
            _oscHandler.Handle(osc);
        };

        _oscHandler.TitleChanged += title =>
        {
            Title = title;
            TitleChanged?.Invoke(title);
        };

        _oscHandler.WorkingDirectoryChanged += dir =>
        {
            WorkingDirectory = dir;
            WorkingDirectoryChanged?.Invoke(dir);
        };

        _oscHandler.NotificationReceived += (title, subtitle, body) =>
        {
            NotificationReceived?.Invoke(title, subtitle, body);
        };

        _oscHandler.ShellPromptMarker += (marker, payload) =>
        {
            ShellPromptMarker?.Invoke(marker, payload);
        };
    }

    /// <summary>
    /// 启动终端进程。
    /// </summary>
    public void Start(
        string? command = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var fallbackDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(fallbackDirectory))
            fallbackDirectory = Environment.CurrentDirectory;

        var effectiveWorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
            ? fallbackDirectory
            : workingDirectory;

        WorkingDirectory = effectiveWorkingDirectory;

        lock (_lock)
        {
            _console = PseudoConsole.Create((short)Buffer.Cols, (short)Buffer.Rows);
            _process = new TerminalProcess(_console, command, effectiveWorkingDirectory, environment);

            _readStream = new FileStream(_console.ReadPipe, FileAccess.Read);
            _writeStream = new FileStream(_console.WritePipe, FileAccess.Write);

            _process.Exited += () =>
            {
                ProcessExited?.Invoke();
            };

            _readThread = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = $"Terminal-Read-{PaneId}",
            };
            _readThread.Start();
        }

        if (!string.IsNullOrWhiteSpace(WorkingDirectory))
            WorkingDirectoryChanged?.Invoke(WorkingDirectory);
    }

    private void ReadLoop()
    {
        var buffer = new byte[4096];
        try
        {
            while (!_disposed && _readStream != null)
            {
                int bytesRead = _readStream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                var chunk = buffer.AsSpan(0, bytesRead).ToArray();

                lock (_lock)
                {
                    try
                    {
                        _parser.Feed(chunk);
                    }
                    catch (Exception ex)
                    {
                        // 不允许畸形/边界 VT 序列导致应用崩溃
                        Debug.WriteLine($"[TerminalSession:{PaneId}] VT parse error: {ex}");
                    }
                }

                RawOutputReceived?.Invoke(chunk);
                OutputReceived?.Invoke();
                Redraw?.Invoke();
            }
        }
        catch (IOException) when (_disposed)
        {
            // 关闭时预期会抛出
        }
        catch (ObjectDisposedException)
        {
            // 关闭时预期会抛出
        }
    }

    /// <summary>
    /// 向终端进程写入原始输入（键盘数据）。
    /// </summary>
    public void Write(string text)
    {
        if (_disposed) return;
        var bytes = Encoding.UTF8.GetBytes(text);
        Write(bytes);
    }

    /// <summary>
    /// 向终端进程写入原始字节。
    /// </summary>
    public void Write(byte[] data)
    {
        if (_disposed) return;

        // 守护进程模式：转发到守护进程而非本地 ConPTY
        if (DaemonWrite != null)
        {
            if (!_daemonWriteLogged)
            {
                _daemonWriteLogged = true;
                DaemonClient.LogDaemon("terminal-session", "daemon-write.first", PaneId, "First daemon write", new Dictionary<string, object?>
                {
                    ["bytes"] = data.Length,
                });
            }
            _ = DaemonWrite(data);
            return;
        }

        if (_writeStream == null)
        {
            if (!_localWriteNullLogged)
            {
                _localWriteNullLogged = true;
                DaemonClient.LogDaemon("terminal-session", "local-write.no-stream", PaneId, "Write called without a local ConPTY stream");
            }
            return;
        }
        try
        {
            _writeStream.Write(data, 0, data.Length);
            _writeStream.Flush();
        }
        catch (IOException) when (_disposed)
        {
            // 关闭时预期会抛出
        }
    }

    /// <summary>
    /// 调整终端尺寸。
    /// </summary>
    public void Resize(int cols, int rows)
    {
        if (_disposed) return;

        cols = Math.Max(1, cols);
        rows = Math.Max(1, rows);

        lock (_lock)
        {
            Buffer.Resize(cols, rows);

            // 守护进程模式：将调整尺寸请求转发到守护进程
            if (DaemonResize != null)
                _ = DaemonResize(cols, rows);
            else
                _console?.Resize((short)cols, (short)rows);
        }

        Redraw?.Invoke();
    }

    /// <summary>
    /// 将原始 VT 输出字节送入解析器并写入缓冲区。
    /// 用于守护进程会话从守护进程接收输出。
    /// </summary>
    public void FeedOutput(byte[] data)
    {
        if (_disposed) return;

        lock (_lock)
        {
            try
            {
                _parser.Feed(data);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TerminalSession:{PaneId}] VT parse error (feed): {ex}");
            }
        }

        OutputReceived?.Invoke();
        Redraw?.Invoke();
    }

    public TerminalBufferSnapshot CreateBufferSnapshot(int maxScrollbackLines = 3000)
    {
        lock (_lock)
        {
            return Buffer.CreateSnapshot(maxScrollbackLines);
        }
    }

    public void RestoreBufferSnapshot(TerminalBufferSnapshot snapshot)
    {
        if (_disposed) return;

        lock (_lock)
        {
            Buffer.RestoreSnapshot(snapshot);
        }

        Redraw?.Invoke();
    }

    private void HandleCsi(List<int> parameters, char final, string qualifier)
    {
        int Param(int index, int defaultValue = 0) =>
            index < parameters.Count && parameters[index] != 0 ? parameters[index] : defaultValue;

        bool isPrivate = qualifier.Contains('?');

        switch (final)
        {
            // 光标移动
            case 'A': // CUU — 光标上移
                Buffer.MoveCursorUp(Param(0, 1));
                break;
            case 'B': // CUD — 光标下移
                Buffer.MoveCursorDown(Param(0, 1));
                break;
            case 'C': // CUF — 光标前移
                Buffer.MoveCursorForward(Param(0, 1));
                break;
            case 'D': // CUB — 光标后移
                Buffer.MoveCursorBackward(Param(0, 1));
                break;
            case 'E': // CNL — 光标下一行
                Buffer.CarriageReturn();
                Buffer.MoveCursorDown(Param(0, 1));
                break;
            case 'F': // CPL — 光标上一行
                Buffer.CarriageReturn();
                Buffer.MoveCursorUp(Param(0, 1));
                break;
            case 'G': // CHA — 光标水平绝对定位
                Buffer.MoveCursorTo(Buffer.CursorRow, Param(0, 1) - 1);
                break;
            case 'H': // CUP — 光标位置
            case 'f': // HVP — 水平垂直位置
                Buffer.MoveCursorTo(Param(0, 1) - 1, Param(1, 1) - 1);
                break;
            case 'd': // VPA — 垂直位置绝对定位
                Buffer.MoveCursorTo(Param(0, 1) - 1, Buffer.CursorCol);
                break;

            // 擦除
            case 'J': // ED — 擦除显示
                Buffer.EraseInDisplay(Param(0));
                break;
            case 'K': // EL — 擦除行
                Buffer.EraseInLine(Param(0));
                break;
            case 'X': // ECH — 擦除字符
                Buffer.EraseChars(Param(0, 1));
                break;

            // 插入/删除
            case 'L': // IL — 插入行
                Buffer.InsertLines(Param(0, 1));
                break;
            case 'M': // DL — 删除行
                Buffer.DeleteLines(Param(0, 1));
                break;
            case '@': // ICH — 插入字符
                Buffer.InsertChars(Param(0, 1));
                break;
            case 'P': // DCH — 删除字符
                Buffer.DeleteChars(Param(0, 1));
                break;

            // 滚动
            case 'S': // SU — 向上滚动
                Buffer.ScrollUp(Param(0, 1));
                break;
            case 'T': // SD — 向下滚动
                Buffer.ScrollDown(Param(0, 1));
                break;

            // 滚动区域
            case 'r': // DECSTBM — 设置上下边界
                if (parameters.Count == 0)
                {
                    Buffer.ResetScrollRegion();
                }
                else
                {
                    Buffer.SetScrollRegion(Param(0, 1) - 1, Param(1, Buffer.Rows) - 1);
                }
                Buffer.MoveCursorTo(0, 0);
                break;

            // SGR — 选择图形渲染
            case 'm':
                HandleSgr(parameters);
                break;

            // 模式设置/重置
            case 'h': // SM / DECSET
                HandleMode(parameters, true, isPrivate);
                break;
            case 'l': // RM / DECRST
                HandleMode(parameters, false, isPrivate);
                break;

            // 光标保存/恢复
            case 's': // SCOSC — 保存光标位置
                if (!isPrivate)
                    Buffer.SaveCursor();
                break;
            case 'u': // SCORC — 恢复光标位置
                if (!isPrivate)
                    Buffer.RestoreCursor();
                break;

            // 设备状态
            case 'n': // DSR — 设备状态报告
                if (Param(0) == 6)
                {
                    // CPR — 光标位置报告
                    Write($"\x1b[{Buffer.CursorRow + 1};{Buffer.CursorCol + 1}R");
                }
                break;

            case 'c': // DA — 设备属性
                if (!isPrivate)
                    Write("\x1b[?1;0c");
                break;
        }
    }

    private void HandleSgr(List<int> parameters)
    {
        if (parameters.Count == 0)
        {
            Buffer.CurrentAttribute = TerminalAttribute.Default;
            return;
        }

        var attr = Buffer.CurrentAttribute;
        int i = 0;
        while (i < parameters.Count)
        {
            int code = parameters[i];
            switch (code)
            {
                case 0: attr = TerminalAttribute.Default; break;
                case 1: attr.Flags |= CellFlags.Bold; break;
                case 2: attr.Flags |= CellFlags.Dim; break;
                case 3: attr.Flags |= CellFlags.Italic; break;
                case 4: attr.Flags |= CellFlags.Underline; break;
                case 5: attr.Flags |= CellFlags.Blink; break;
                case 7: attr.Flags |= CellFlags.Inverse; break;
                case 8: attr.Flags |= CellFlags.Hidden; break;
                case 9: attr.Flags |= CellFlags.Strikethrough; break;
                case 21: attr.Flags &= ~CellFlags.Bold; break;
                case 22: attr.Flags &= ~(CellFlags.Bold | CellFlags.Dim); break;
                case 23: attr.Flags &= ~CellFlags.Italic; break;
                case 24: attr.Flags &= ~CellFlags.Underline; break;
                case 25: attr.Flags &= ~CellFlags.Blink; break;
                case 27: attr.Flags &= ~CellFlags.Inverse; break;
                case 28: attr.Flags &= ~CellFlags.Hidden; break;
                case 29: attr.Flags &= ~CellFlags.Strikethrough; break;

                // 前景色
                case >= 30 and <= 37:
                    attr.Foreground = TerminalColor.FromIndex(code - 30);
                    break;
                case 38: // 扩展前景色
                    i = ParseExtendedColor(parameters, i, out var fg);
                    attr.Foreground = fg;
                    continue;
                case 39: attr.Foreground = TerminalColor.Default; break;

                // 背景色
                case >= 40 and <= 47:
                    attr.Background = TerminalColor.FromIndex(code - 40);
                    break;
                case 48: // 扩展背景色
                    i = ParseExtendedColor(parameters, i, out var bg);
                    attr.Background = bg;
                    continue;
                case 49: attr.Background = TerminalColor.Default; break;

                // 高亮前景色
                case >= 90 and <= 97:
                    attr.Foreground = TerminalColor.FromIndex(code - 90 + 8);
                    break;

                // 高亮背景色
                case >= 100 and <= 107:
                    attr.Background = TerminalColor.FromIndex(code - 100 + 8);
                    break;
            }
            i++;
        }
        Buffer.CurrentAttribute = attr;
    }

    private static int ParseExtendedColor(List<int> parameters, int index, out TerminalColor color)
    {
        color = TerminalColor.Default;
        if (index + 1 >= parameters.Count)
            return index + 1;

        int type = parameters[index + 1];
        switch (type)
        {
            case 5: // 256 色：38;5;N
                if (index + 2 < parameters.Count)
                {
                    color = TerminalColor.FromIndex(parameters[index + 2]);
                    return index + 3;
                }
                return index + 2;

            case 2: // 真彩色：38;2;R;G;B
                if (index + 4 < parameters.Count)
                {
                    color = TerminalColor.FromRgb(
                        (byte)Math.Clamp(parameters[index + 2], 0, 255),
                        (byte)Math.Clamp(parameters[index + 3], 0, 255),
                        (byte)Math.Clamp(parameters[index + 4], 0, 255));
                    return index + 5;
                }
                return index + 2;

            default:
                return index + 1;
        }
    }

    private void HandleMode(List<int> parameters, bool set, bool isPrivate)
    {
        foreach (int param in parameters)
        {
            if (isPrivate)
            {
                switch (param)
                {
                    case 1: // DECCKM — 光标键模式
                        Buffer.ApplicationCursorKeys = set;
                        break;
                    case 6: // DECOM — 原点模式
                        Buffer.OriginMode = set;
                        break;
                    case 7: // DECAWM — 自动换行模式
                        Buffer.AutoWrapMode = set;
                        break;
                    case 25: // DECTCEM — 文本光标启用模式
                        Buffer.CursorVisible = set;
                        break;
                    case 1049: // 备用屏幕缓冲区
                        if (set)
                        {
                            Buffer.SwitchToAlternateScreen();
                        }
                        else
                        {
                            Buffer.SwitchToMainScreen();
                        }
                        break;
                    case 47: // 备用屏幕（旧版）
                    case 1047: // 备用屏幕（xterm）
                        if (set)
                            Buffer.SwitchToAlternateScreen();
                        else
                            Buffer.SwitchToMainScreen();
                        break;
                    case 2004: // 括号粘贴模式
                        Buffer.BracketedPasteMode = set;
                        break;
                    case 1000: // 普通鼠标追踪
                        Buffer.MouseTrackingNormal = set;
                        break;
                    case 1002: // 按键事件鼠标追踪
                        Buffer.MouseTrackingButton = set;
                        break;
                    case 1003: // 任意事件鼠标追踪
                        Buffer.MouseTrackingAny = set;
                        break;
                    case 1006: // SGR 扩展鼠标报告
                        Buffer.MouseSgrExtended = set;
                        break;
                }
            }
            else
            {
                switch (param)
                {
                    case 4: // IRM — 插入/替换模式
                        Buffer.InsertMode = set;
                        break;
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _readStream?.Dispose();
        _writeStream?.Dispose();
        _process?.Dispose();
        _console?.Dispose();
    }
}
