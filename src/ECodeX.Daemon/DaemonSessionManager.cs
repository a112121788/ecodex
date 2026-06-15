using System.Collections.Concurrent;
using ECodeX.Core.IPC;
using ECodeX.Core.Terminal;
using static ECodeX.Core.IPC.DaemonClient;

namespace ECodeX.Daemon;

/// <summary>
/// 会话管理器 —— 负责终端会话的创建、复用、输入输出和生命周期管理
/// </summary>
public sealed class DaemonSessionManager : IDisposable
{
    private readonly ConcurrentDictionary<string, TerminalSession> _sessions = new();

    public event Action<string>? SessionCreated;
    public event Action<string, int>? SessionExited;
    public event Action<string, string>? TitleChanged;
    public event Action<string, string>? CwdChanged;
    public event Action<string>? BellReceived;
    public event Action<string, byte[]>? RawOutput;

    public int ActiveSessionCount => _sessions.Count;

    public DaemonSessionInfo CreateSession(
        string paneId,
        int cols,
        int rows,
        string? workingDirectory,
        string? command,
        string? workspaceId)
    {
        // 如果会话已存在，则返回其信息（attach/重连语义）
        if (_sessions.TryGetValue(paneId, out var existing))
        {
            LogDaemon("daemon-session-manager", "session.attach", paneId, "Reconnecting to existing session", new Dictionary<string, object?>
            {
                ["cwd"] = existing.WorkingDirectory,
                ["running"] = existing.IsRunning,
            });
            return new DaemonSessionInfo
            {
                PaneId = paneId,
                Cols = existing.Buffer.Cols,
                Rows = existing.Buffer.Rows,
                WorkingDirectory = existing.WorkingDirectory ?? "",
                Title = existing.Title,
                IsRunning = existing.IsRunning,
                IsExisting = true,
            };
        }

        LogDaemon("daemon-session-manager", "session.create", paneId, "Creating new session", new Dictionary<string, object?>
        {
            ["cols"] = cols,
            ["command"] = command,
            ["cwd"] = workingDirectory,
            ["rows"] = rows,
            ["workspaceId"] = workspaceId,
        });
        var session = new TerminalSession(paneId, cols, rows);

        session.RawOutputReceived += data =>
        {
            RawOutput?.Invoke(paneId, data);
        };

        session.ProcessExited += () =>
        {
            SessionExited?.Invoke(paneId, 0);
        };

        session.TitleChanged += title =>
        {
            TitleChanged?.Invoke(paneId, title);
        };

        session.WorkingDirectoryChanged += dir =>
        {
            CwdChanged?.Invoke(paneId, dir);
        };

        session.BellReceived += () =>
        {
            BellReceived?.Invoke(paneId);
        };

        _sessions[paneId] = session;
        session.Start(
            command: command,
            workingDirectory: workingDirectory,
            environment: TerminalEnvironmentVariables.ForWorkspace(workspaceId));

        SessionCreated?.Invoke(paneId);

        return new DaemonSessionInfo
        {
            PaneId = paneId,
            Cols = cols,
            Rows = rows,
            WorkingDirectory = session.WorkingDirectory ?? "",
            IsRunning = session.IsRunning,
        };
    }

    public void WriteToSession(string paneId, byte[] data)
    {
        if (_sessions.TryGetValue(paneId, out var session))
            session.Write(data);
    }

    public void ResizeSession(string paneId, int cols, int rows)
    {
        if (_sessions.TryGetValue(paneId, out var session))
            session.Resize(cols, rows);
    }

    public void CloseSession(string paneId)
    {
        if (_sessions.TryRemove(paneId, out var session))
            session.Dispose();
    }

    public List<DaemonSessionInfo> ListSessions()
    {
        return _sessions.Select(kvp => new DaemonSessionInfo
        {
            PaneId = kvp.Key,
            Cols = kvp.Value.Buffer.Cols,
            Rows = kvp.Value.Buffer.Rows,
            WorkingDirectory = kvp.Value.WorkingDirectory ?? "",
            Title = kvp.Value.Title,
            IsRunning = kvp.Value.IsRunning,
        }).ToList();
    }

    public string? GetSnapshot(string paneId)
    {
        if (!_sessions.TryGetValue(paneId, out var session))
            return null;

        var snapshot = session.CreateBufferSnapshot(maxScrollbackLines: 3000);
        return System.Text.Json.JsonSerializer.Serialize(snapshot);
    }

    public TerminalSession? GetSession(string paneId)
    {
        return _sessions.GetValueOrDefault(paneId);
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
            session.Dispose();
        _sessions.Clear();
    }
}
