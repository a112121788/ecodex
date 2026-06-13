namespace ECode.Core.IPC;

/// <summary>
/// 守护进程通信协议的消息类型常量和数据传输对象。
/// </summary>
public static class DaemonMessageTypes
{
    public const string SessionCreate = "SESSION_CREATE";
    public const string SessionWrite = "SESSION_WRITE";
    public const string SessionResize = "SESSION_RESIZE";
    public const string SessionClose = "SESSION_CLOSE";
    public const string SessionList = "SESSION_LIST";
    public const string SessionSnapshot = "SESSION_SNAPSHOT";
    public const string Ping = "PING";

    public const string EventOutput = "OUTPUT";
    public const string EventExited = "EXITED";
    public const string EventTitleChanged = "TITLE_CHANGED";
    public const string EventCwdChanged = "CWD_CHANGED";
    public const string EventBell = "BELL";
}

public class DaemonRequest
{
    public string Type { get; set; } = "";
    public string? PaneId { get; set; }
    public int? Cols { get; set; }
    public int? Rows { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? Command { get; set; }
    public string? Data { get; set; }
}

public class DaemonResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Data { get; set; }
}

public class DaemonSessionInfo
{
    public string PaneId { get; set; } = "";
    public int Cols { get; set; }
    public int Rows { get; set; }
    public string WorkingDirectory { get; set; } = "";
    public string? Title { get; set; }
    public bool IsRunning { get; set; }
    /// <summary>当会话在守护进程上已存在时为 true（重连/附加）。</summary>
    public bool IsExisting { get; set; }
}

public class DaemonEvent
{
    public string Type { get; set; } = "";
    public string? PaneId { get; set; }
    public string? Data { get; set; }
}
