using ECodeX.Core.Config;
using ECodeX.Core.IPC;
using ECodeX.Daemon;

/// <summary>
/// ecodex-daemon 后台服务入口 —— 管理所有终端会话的生命周期，通过命名管道接收 CLI 和 WPF 应用的控制指令
/// </summary>

// 通过命名互斥体进行单实例检查。
// 兼容期：若用户保留 CompatListenLegacyDaemonPipe=true，且旧 mutex 已被旧版
// cmux-daemon 占用，我们主动让位，由旧 daemon 继续处理遗留连接。
var newMutex = new Mutex(true, CompatibilityOptions.NewMutexName, out bool createdNew);
if (!createdNew)
{
    Log("startup.mutex-exists", "ecodex-daemon is already running (mutex exists). Exiting.");
    return 1;
}

if (CompatibilityOptions.ShouldHonorLegacyMutex())
{
    try
    {
        if (Mutex.TryOpenExisting(CompatibilityOptions.LegacyMutexName, out _))
        {
            Log("startup.legacy-mutex", "Legacy cmux-daemon is running; deferring to it.");
            newMutex.Dispose();
            return 0;
        }
    }
    catch
    {
        // ignore: 旧 mutex 不存在可继续启动
    }
}

Log("startup.begin", "Starting ecodex-daemon", fields: new Dictionary<string, object?>
{
    ["pid"] = Environment.ProcessId,
});

var sessionManager = new DaemonSessionManager();
var pipeServer = new DaemonPipeServer(sessionManager);

using var cts = new CancellationTokenSource();

// 空闲超时：在没有客户端连接且没有活动会话的情况下，24 小时后退出
var idleTimeout = TimeSpan.FromHours(24);
DateTime lastActivity = DateTime.UtcNow;

pipeServer.ClientConnected += () =>
{
    lastActivity = DateTime.UtcNow;
    Log("client.connected", "Client connected", fields: new Dictionary<string, object?>
    {
        ["connectedClients"] = pipeServer.ConnectedClients,
    });
};

pipeServer.ClientDisconnected += () =>
{
    lastActivity = DateTime.UtcNow;
    Log("client.disconnected", "Client disconnected", fields: new Dictionary<string, object?>
    {
        ["activeSessions"] = sessionManager.ActiveSessionCount,
        ["connectedClients"] = pipeServer.ConnectedClients,
    });
};

sessionManager.SessionCreated += paneId =>
{
    lastActivity = DateTime.UtcNow;
    Log("session.created", "Session created", paneId, new Dictionary<string, object?>
    {
        ["activeSessions"] = sessionManager.ActiveSessionCount,
    });
};

sessionManager.SessionExited += (paneId, exitCode) =>
{
    Log("session.exited", "Session exited", paneId, new Dictionary<string, object?>
    {
        ["activeSessions"] = sessionManager.ActiveSessionCount,
        ["exitCode"] = exitCode,
    });
};

Log("pipe-server.start", "Starting pipe server");
// 在专用后台线程上运行管道服务器（同步 I/O）
var serverThread = new Thread(() =>
{
    try { pipeServer.Run(cts.Token); }
    catch (OperationCanceledException) { }
})
{
    IsBackground = true,
    Name = "PipeServer-Accept",
};
serverThread.Start();
Log("pipe-server.started", "Pipe server started, waiting for connections");

// 空闲监控循环 —— 阻塞主线程直到关闭
while (!cts.Token.IsCancellationRequested)
{
    try
    {
        Thread.Sleep(TimeSpan.FromMinutes(5));
    }
    catch (ThreadInterruptedException) { break; }

    if (pipeServer.ConnectedClients == 0
        && sessionManager.ActiveSessionCount == 0
        && DateTime.UtcNow - lastActivity > idleTimeout)
    {
        Log("shutdown.idle-timeout", "Idle timeout reached. Shutting down.");
        cts.Cancel();
    }
}

newMutex.Dispose();


Log("shutdown.begin", "Shutting down ecodex-daemon", fields: new Dictionary<string, object?>
{
    ["activeSessions"] = sessionManager.ActiveSessionCount,
});
sessionManager.Dispose();
Log("shutdown.done", "ecodex-daemon stopped.");
return 0;

// 写入与 WPF 客户端共用的 daemon-debug.log
static void Log(
    string eventName,
    string message,
    string? paneId = null,
    IReadOnlyDictionary<string, object?>? fields = null) =>
    DaemonClient.LogDaemon("ecodex-daemon", eventName, paneId, message, fields);
