using ECode.Core.Config;
using ECode.Core.IPC;
using ECode.Daemon;

/// <summary>
/// ecode-daemon 后台服务入口 —— 管理所有终端会话的生命周期，通过命名管道接收 CLI 和 WPF 应用的控制指令
/// </summary>

// 通过命名互斥体进行单实例检查。
// 兼容期：若用户保留 CompatListenLegacyDaemonPipe=true，且旧 mutex 已被旧版
// cmux-daemon 占用，我们主动让位，由旧 daemon 继续处理遗留连接。
var newMutex = new Mutex(true, CompatibilityOptions.NewMutexName, out bool createdNew);
if (!createdNew)
{
    Log("ecode-daemon is already running (mutex exists). Exiting.");
    return 1;
}

if (CompatibilityOptions.ShouldHonorLegacyMutex())
{
    try
    {
        if (Mutex.TryOpenExisting(CompatibilityOptions.LegacyMutexName, out _))
        {
            Log("[ecode-daemon] Legacy cmux-daemon is running; deferring to it.");
            newMutex.Dispose();
            return 0;
        }
    }
    catch
    {
        // ignore: 旧 mutex 不存在可继续启动
    }
}

Log($"[ecode-daemon] Starting (PID {Environment.ProcessId})...");

var sessionManager = new DaemonSessionManager();
var pipeServer = new DaemonPipeServer(sessionManager);

using var cts = new CancellationTokenSource();

// 空闲超时：在没有客户端连接且没有活动会话的情况下，24 小时后退出
var idleTimeout = TimeSpan.FromHours(24);
DateTime lastActivity = DateTime.UtcNow;

pipeServer.ClientConnected += () =>
{
    lastActivity = DateTime.UtcNow;
    Log($"[ecode-daemon] Client connected (total: {pipeServer.ConnectedClients})");
};

pipeServer.ClientDisconnected += () =>
{
    lastActivity = DateTime.UtcNow;
    Log($"[ecode-daemon] Client disconnected (total: {pipeServer.ConnectedClients}, sessions: {sessionManager.ActiveSessionCount})");
};

sessionManager.SessionCreated += paneId =>
{
    lastActivity = DateTime.UtcNow;
    Log($"[ecode-daemon] Session created: {paneId} (total: {sessionManager.ActiveSessionCount})");
};

sessionManager.SessionExited += (paneId, exitCode) =>
{
    Log($"[ecode-daemon] Session exited: {paneId} code={exitCode} (total: {sessionManager.ActiveSessionCount})");
};

Log("[ecode-daemon] Starting pipe server...");
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
Log("[ecode-daemon] Pipe server started, waiting for connections...");

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
        Log("[ecode-daemon] Idle timeout reached. Shutting down.");
        cts.Cancel();
    }
}

newMutex.Dispose();


Log($"[ecode-daemon] Shutting down (sessions: {sessionManager.ActiveSessionCount})...");
sessionManager.Dispose();
Log("[ecode-daemon] Stopped.");
return 0;

// 写入与 WPF 客户端共用的 daemon-debug.log
static void Log(string message) => DaemonClient.LogDaemon(message);
