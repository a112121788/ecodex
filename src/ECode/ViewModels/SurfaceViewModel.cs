using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ECode.Core.Config;
using ECode.Core.IPC;
using ECode.Core.Models;
using ECode.Core.Services;
using ECode.Core.Terminal;

namespace ECode.ViewModels;

/// <summary>管理单个 Surface 的分屏布局和终端会话生命周期</summary>
public partial class SurfaceViewModel : ObservableObject, IDisposable
{
    public Surface Surface { get; }
    private readonly string _workspaceId;
    private readonly NotificationService _notificationService;
    private readonly Dictionary<string, TerminalSession> _sessions = [];
    private readonly Dictionary<string, List<string>> _paneCommandHistory = [];
    private readonly Dictionary<string, string?> _paneShells = [];
    private readonly Dictionary<string, int> _paneUnreadCounts = [];
    private readonly HashSet<string> _daemonPanes = [];
    private readonly HashSet<string> _daemonOutputLogged = [];
    private static readonly object _daemonWaitLock = new();
    private static bool _daemonWaitDone;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private SplitNode _rootNode;

    [ObservableProperty]
    private string? _focusedPaneId;

    [ObservableProperty]
    private bool _isZoomed;

    [ObservableProperty]
    private int _unreadNotificationCount;

    [ObservableProperty]
    private bool _hasNotification;

    [ObservableProperty]
    private int _notificationVersion;

    [ObservableProperty]
    private string? _attentionPaneId;

    [ObservableProperty]
    private int _attentionVersion;

    [ObservableProperty]
    private DateTime _attentionRequestedAtUtc;

    public event Action<string>? WorkingDirectoryChanged;

    /// <summary>Gets the shell process PID from the focused pane session.</summary>
    public int? ShellPid
    {
        get
        {
            if (FocusedPaneId == null) return null;
            var session = GetSession(FocusedPaneId);
            return session?.ProcessId;
        }
    }

    public SurfaceViewModel(Surface surface, string workspaceId, NotificationService notificationService)
    {
        Surface = surface;
        _workspaceId = workspaceId;
        _notificationService = notificationService;
        _name = surface.Name;
        _rootNode = surface.RootSplitNode;
        _focusedPaneId = surface.FocusedPaneId;

        // 为会话持久化连接守护进程事件
        var daemon = App.DaemonClient;
        daemon.RawOutputReceived += OnDaemonRawOutput;
        daemon.CwdChanged += OnDaemonCwdChanged;
        daemon.TitleChanged += OnDaemonTitleChanged;
        daemon.SessionExited += OnDaemonSessionExited;
        daemon.BellReceived += OnDaemonBellReceived;
        daemon.Disconnected += OnDaemonDisconnected;

        // 为所有叶子节点启动终端会话
        foreach (var leaf in _rootNode.GetLeaves())
        {
            if (leaf.PaneId != null)
            {
                Surface.PaneSnapshots.TryGetValue(leaf.PaneId, out var snapshot);
                if (snapshot?.CommandHistory is { Count: > 0 })
                {
                    _paneCommandHistory[leaf.PaneId] = snapshot.CommandHistory
                        .Select(App.CommandLogService.SanitizeCommandForStorage)
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .Cast<string>()
                        .ToList();
                }

                StartSession(leaf.PaneId, snapshot?.WorkingDirectory, snapshot, snapshot?.Shell);
            }
        }

        var firstLeaf = _rootNode.GetLeaves().FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.PaneId));
        if (firstLeaf?.PaneId != null &&
            (string.IsNullOrWhiteSpace(_focusedPaneId) || _rootNode.FindNode(_focusedPaneId) == null))
        {
            FocusedPaneId = firstLeaf.PaneId;
        }
        else
        {
            Surface.FocusedPaneId = _focusedPaneId;
        }

        RefreshNotificationState();
    }

    partial void OnUnreadNotificationCountChanged(int value)
    {
        HasNotification = value > 0;
    }

    public bool HasUnreadNotification(string paneId)
    {
        return _paneUnreadCounts.TryGetValue(paneId, out var count) && count > 0;
    }

    public void RefreshNotificationState()
    {
        foreach (var leaf in RootNode.GetLeaves())
        {
            if (string.IsNullOrWhiteSpace(leaf.PaneId))
                continue;

            _paneUnreadCounts[leaf.PaneId] = _notificationService.GetUnreadCount(
                _workspaceId,
                Surface.Id,
                leaf.PaneId);
        }

        NotificationVersion++;
    }

    public void FlashPaneAttention(string paneId)
    {
        if (RootNode.FindNode(paneId) == null)
            return;

        AttentionPaneId = paneId;
        AttentionRequestedAtUtc = DateTime.UtcNow;
        AttentionVersion++;
    }

    private void OnDaemonRawOutput(string paneId, byte[] data)
    {
        if (!_daemonPanes.Contains(paneId)) return;
        if (_sessions.TryGetValue(paneId, out var session))
            session.FeedOutput(data);
    }

    private void OnDaemonCwdChanged(string paneId, string dir)
    {
        if (!_daemonPanes.Contains(paneId)) return;
        // 更新会话的 WorkingDirectory，以便写入快照
        if (_sessions.TryGetValue(paneId, out var session))
            session.WorkingDirectory = dir;
        if (paneId == FocusedPaneId)
            WorkingDirectoryChanged?.Invoke(dir);
    }

    private void OnDaemonTitleChanged(string paneId, string title)
    {
        if (!_daemonPanes.Contains(paneId)) return;
    }

    private void OnDaemonSessionExited(string paneId, int exitCode)
    {
        if (!_daemonPanes.Contains(paneId)) return;
        _daemonPanes.Remove(paneId);
    }

    private void OnDaemonBellReceived(string paneId)
    {
        if (!_daemonPanes.Contains(paneId)) return;
    }

    private void OnDaemonDisconnected()
    {
        // 守护进程已停止 —— 把所有守护进程上的会话回退到本地 ConPTY
        var paneIds = _daemonPanes.ToList();
        if (paneIds.Count == 0) return;

        DaemonLog($"[DaemonDisconnected] Falling back {paneIds.Count} sessions to local ConPTY");

        foreach (var paneId in paneIds)
        {
            if (!_sessions.TryGetValue(paneId, out var session)) continue;

            var cwd = session.WorkingDirectory;
            session.DaemonWrite = null;
            session.DaemonResize = null;

            try
            {
                session.Start(command: _paneShells.GetValueOrDefault(paneId) ?? GetConfiguredShell(), workingDirectory: cwd);
                DaemonLog($"[DaemonDisconnected] {paneId} → local session started");
            }
            catch (Exception ex)
            {
                DaemonLog($"[DaemonDisconnected] {paneId} → local start failed: {ex.Message}");
            }
        }

        _daemonPanes.Clear();
    }

    public TerminalSession? GetSession(string paneId)
    {
        return _sessions.GetValueOrDefault(paneId);
    }

    public string GetPaneTitle(string paneId, string? fallbackTitle)
    {
        if (Surface.PaneCustomNames.TryGetValue(paneId, out var custom) && !string.IsNullOrWhiteSpace(custom))
            return custom;

        return fallbackTitle ?? "Terminal";
    }

    public void SetPaneCustomName(string paneId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            Surface.PaneCustomNames.Remove(paneId);
        else
            Surface.PaneCustomNames[paneId] = name.Trim();

        OnPropertyChanged(nameof(RootNode));
    }

    public IReadOnlyList<string> GetCommandHistory(string paneId)
    {
        return _paneCommandHistory.TryGetValue(paneId, out var history)
            ? history.AsReadOnly()
            : [];
    }

    private static bool ShouldCaptureTranscript(string reason)
    {
        var settings = SettingsService.Current;

        if (string.Equals(reason, "clear-terminal", StringComparison.OrdinalIgnoreCase))
            return settings.CaptureTranscriptsOnClear;

        return settings.CaptureTranscriptsOnClose;
    }

    public string? CapturePaneTranscript(string paneId, string reason)
    {
        if (!ShouldCaptureTranscript(reason))
            return null;

        if (!_sessions.TryGetValue(paneId, out var session))
            return null;

        var text = session.Buffer.ExportPlainText(maxScrollbackLines: 20000);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return App.CommandLogService.SaveTerminalTranscript(
            _workspaceId,
            Surface.Id,
            paneId,
            session.WorkingDirectory,
            text,
            reason);
    }

    public int CaptureAllPaneTranscripts(string reason)
    {
        if (!ShouldCaptureTranscript(reason))
            return 0;

        int captured = 0;

        var paneIds = RootNode.GetLeaves()
            .Select(l => l.PaneId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var paneId in paneIds)
        {
            if (CapturePaneTranscript(paneId, reason) != null)
                captured++;
        }

        return captured;
    }

    public void CapturePaneSnapshotsForPersistence()
    {
        var activePaneIds = RootNode.GetLeaves()
            .Select(l => l.PaneId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToHashSet();

        foreach (var paneId in activePaneIds)
        {
            if (!_sessions.TryGetValue(paneId, out var session))
                continue;

            var state = Surface.PaneSnapshots.TryGetValue(paneId, out var existing)
                ? existing
                : new PaneStateSnapshot();

            state.CapturedAt = DateTime.UtcNow;
            state.WorkingDirectory = session.WorkingDirectory;
            state.Shell = _paneShells.GetValueOrDefault(paneId);
            state.BufferSnapshot = session.CreateBufferSnapshot(maxScrollbackLines: 3000);

            if (_paneCommandHistory.TryGetValue(paneId, out var history))
                state.CommandHistory = history.TakeLast(500).ToList();

            Surface.PaneSnapshots[paneId] = state;
        }

        var stalePaneIds = Surface.PaneSnapshots.Keys.Where(id => !activePaneIds.Contains(id)).ToList();
        foreach (var paneId in stalePaneIds)
            Surface.PaneSnapshots.Remove(paneId);
    }

    public void RegisterCommandSubmission(string paneId, string command)
    {
        var sanitized = App.CommandLogService.SanitizeCommandForStorage(command);
        if (string.IsNullOrWhiteSpace(sanitized))
            return;

        AppendToCommandHistory(paneId, sanitized);

        var cwd = _sessions.TryGetValue(paneId, out var session)
            ? session.WorkingDirectory
            : null;

        App.CommandLogService.RecordManualCommandSubmission(
            paneId,
            _workspaceId,
            Surface.Id,
            sanitized,
            cwd);
    }


    private void AppendToCommandHistory(string paneId, string command)
    {
        if (!_paneCommandHistory.TryGetValue(paneId, out var history))
        {
            history = [];
            _paneCommandHistory[paneId] = history;
        }

        var trimmed = command.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return;

        if (history.Count == 0 || !string.Equals(history[^1], trimmed, StringComparison.Ordinal))
            history.Add(trimmed);

        while (history.Count > 500)
            history.RemoveAt(0);
    }

    private TerminalSession StartSession(string paneId, string? workingDirectory = null, PaneStateSnapshot? restoredState = null, string? shell = null)
    {
        var effectiveShell = shell ?? GetConfiguredShell();
        // 存储显式覆盖（null 表示使用设置中的默认 Shell）
        _paneShells[paneId] = shell;

        // 等待守护进程连接任务完成（包含按需拉起守护进程的逻辑）。
        // 第一个面板最多阻塞 5 秒；后续面板直接复用已缓存的结果。
        lock (_daemonWaitLock)
        {
            if (!_daemonWaitDone)
            {
                DaemonLog($"[StartSession:{paneId}] Waiting for daemon connect task...");
                try { App.DaemonConnectTask.Wait(5000); }
                catch { /* timeout or connect failure — proceed with local */ }
                _daemonWaitDone = true;
            }
        }

        var daemonReady = App.DaemonConnectTask.IsCompletedSuccessfully
                          && App.DaemonConnectTask.Result;

        DaemonLog($"[StartSession:{paneId}] daemonReady={daemonReady}, IsConnected={App.DaemonClient.IsConnected}, TaskStatus={App.DaemonConnectTask.Status}");

        // 先尝试走守护进程托管的会话
        if (daemonReady)
        {
            try
            {
                return StartDaemonSession(paneId, workingDirectory, restoredState, effectiveShell);
            }
            catch (Exception ex)
            {
                DaemonLog($"[StartSession:{paneId}] Daemon session failed: {ex.Message}");
            }
        }

        DaemonLog($"[StartSession:{paneId}] Using LOCAL session");
        return StartLocalSession(paneId, workingDirectory, restoredState, effectiveShell);
    }

    private static void DaemonLog(string message) => App.DaemonLog(message);

    private TerminalSession StartDaemonSession(string paneId, string? workingDirectory, PaneStateSnapshot? restoredState, string? shell)
    {
        // 优先使用快照中保存的尺寸（避免重连后触发不必要的 resize）
        var initCols = restoredState?.BufferSnapshot?.Cols ?? 120;
        var initRows = restoredState?.BufferSnapshot?.Rows ?? 30;
        var session = new TerminalSession(paneId, initCols, initRows);
        WireSessionEvents(session, paneId);

        // 设置守护进程委托，让 Write/Resize 经由守护进程转发
        var daemon = App.DaemonClient;
        session.DaemonWrite = data => daemon.WriteAsync(paneId, data);
        session.DaemonResize = (cols, rows) => daemon.ResizeAsync(paneId, cols, rows);

        _sessions[paneId] = session;
        _daemonPanes.Add(paneId);

        var effectiveCwd = workingDirectory ?? restoredState?.WorkingDirectory;

        // 在守护进程上异步创建/挂载会话
        _ = Task.Run(async () =>
        {
            try
            {
                DaemonLog($"[DaemonSession:{paneId}] Calling CreateSessionAsync ({initCols}x{initRows})...");
                var result = await daemon.CreateSessionAsync(
                    paneId, initCols, initRows, effectiveCwd);

                if (result == null)
                {
                    DaemonLog($"[DaemonSession:{paneId}] CreateSessionAsync returned NULL — falling back to local");
                    _daemonPanes.Remove(paneId);
                    session.DaemonWrite = null;
                    session.DaemonResize = null;
                    session.Start(command: shell, workingDirectory: effectiveCwd);
                    return;
                }

                DaemonLog($"[DaemonSession:{paneId}] CreateSessionAsync OK: IsExisting={result.IsExisting}, IsRunning={result.IsRunning}, Cwd={result.WorkingDirectory}");

                // 从守护进程响应中设置工作目录
                if (!string.IsNullOrEmpty(result.WorkingDirectory))
                    session.WorkingDirectory = result.WorkingDirectory;

                // 若为重连已有守护进程会话，则获取其实时的缓冲区快照
                if (result.IsExisting && result.IsRunning)
                {
                    DaemonLog($"[DaemonSession:{paneId}] Reconnecting — fetching daemon snapshot...");
                    var snapshotJson = await daemon.GetSnapshotAsync(paneId);
                    if (snapshotJson != null)
                    {
                        try
                        {
                            var snapshot = System.Text.Json.JsonSerializer.Deserialize<TerminalBufferSnapshot>(snapshotJson);
                            if (snapshot != null)
                            {
                                session.RestoreBufferSnapshot(snapshot);
                                DaemonLog($"[DaemonSession:{paneId}] Snapshot restored ({snapshotJson.Length} chars)");
                            }
                        }
                        catch (Exception ex)
                        {
                            DaemonLog($"[DaemonSession:{paneId}] Snapshot restore error: {ex.Message}");
                        }
                    }
                    else
                    {
                        DaemonLog($"[DaemonSession:{paneId}] GetSnapshotAsync returned null");
                    }

                    // 短暂延时后发送回车，强制 Shell 重新打印一次提示符。
                    // 快照虽然会恢复滚动历史，但提示符所在行可能已经缺失，
                    // 因为 Shell 早在断连前就把提示符打印过了。
                    await Task.Delay(300);
                    await daemon.WriteAsync(paneId, [0x0d]); // CR = 回车
                }
            }
            catch (Exception ex)
            {
                DaemonLog($"[DaemonSession:{paneId}] Exception — falling back to local: {ex.Message}");
                _daemonPanes.Remove(paneId);
                session.DaemonWrite = null;
                session.DaemonResize = null;
                session.Start(command: shell, workingDirectory: effectiveCwd);
            }
        });

        if (restoredState?.BufferSnapshot != null)
            session.RestoreBufferSnapshot(restoredState.BufferSnapshot);

        return session;
    }

    private static string? GetConfiguredShell()
    {
        var shell = SettingsService.Current.DefaultShell;
        return string.IsNullOrWhiteSpace(shell) ? null : shell;
    }

    private TerminalSession StartLocalSession(string paneId, string? workingDirectory, PaneStateSnapshot? restoredState, string? shell)
    {
        var session = new TerminalSession(paneId);
        WireSessionEvents(session, paneId);

        _sessions[paneId] = session;
        session.Start(command: shell, workingDirectory: workingDirectory ?? restoredState?.WorkingDirectory);

        if (restoredState?.BufferSnapshot != null)
            session.RestoreBufferSnapshot(restoredState.BufferSnapshot);

        return session;
    }

    private void WireSessionEvents(TerminalSession session, string paneId)
    {
        session.WorkingDirectoryChanged += dir =>
        {
            if (paneId == FocusedPaneId)
                WorkingDirectoryChanged?.Invoke(dir);
        };

        session.NotificationReceived += (title, subtitle, body) =>
        {
            var source = NotificationSource.Osc9; // Default
            _notificationService.AddNotification(
                _workspaceId, Surface.Id, paneId,
                title, subtitle, body, source);
        };

        session.ShellPromptMarker += (marker, payload) =>
        {
            App.CommandLogService.HandlePromptMarker(
                paneId,
                _workspaceId,
                Surface.Id,
                marker,
                payload,
                session.WorkingDirectory);

            if (marker == 'B')
            {
                var sanitized = App.CommandLogService.SanitizeCommandForStorage(payload);
                if (!string.IsNullOrWhiteSpace(sanitized))
                    AppendToCommandHistory(paneId, sanitized);
            }
        };
    }

    [RelayCommand]
    public void SplitRight()
    {
        SplitFocused(SplitDirection.Vertical);
    }

    [RelayCommand]
    public void SplitDown()
    {
        SplitFocused(SplitDirection.Horizontal);
    }

    public void SplitFocused(SplitDirection direction, string? shell = null)
    {
        if (FocusedPaneId == null) return;

        var node = RootNode.FindNode(FocusedPaneId);
        if (node == null || !node.IsLeaf) return;

        var newChild = node.Split(direction);
        if (newChild.PaneId != null)
        {
            var currentSession = GetSession(FocusedPaneId);
            var cwd = currentSession?.WorkingDirectory;
            var effectiveShell = shell ?? _paneShells.GetValueOrDefault(FocusedPaneId);
            StartSession(newChild.PaneId, cwd, null, effectiveShell);
            FocusedPaneId = newChild.PaneId;
        }

        // 触发 UI 更新
        OnPropertyChanged(nameof(RootNode));
    }

    public void OpenPaneWithShell(string shellPath)
    {
        SplitFocused(SplitDirection.Vertical, shellPath);
    }

    [RelayCommand]
    public void ClosePane()
    {
        ClosePane(FocusedPaneId);
    }

    public void ClosePane(string? paneId)
    {
        if (paneId == null) return;

        CapturePaneTranscript(paneId, "pane-close");

        // 在移除前先取相邻面板
        var nextLeaf = RootNode.GetNextLeaf(paneId) ?? RootNode.GetPreviousLeaf(paneId);
        string? nextPaneId = nextLeaf?.PaneId;

        // 停止并移除该会话
        if (_sessions.TryGetValue(paneId, out var session))
        {
            if (_daemonPanes.Remove(paneId))
                _ = App.DaemonClient.CloseSessionAsync(paneId);
            session.Dispose();
            _sessions.Remove(paneId);
        }

        Surface.PaneCustomNames.Remove(paneId);
        Surface.PaneSnapshots.Remove(paneId);
        _paneCommandHistory.Remove(paneId);
        _paneShells.Remove(paneId);

        // 如果这是唯一的面板，则不移除
        var leaves = RootNode.GetLeaves().ToList();
        if (leaves.Count <= 1) return;

        RootNode.Remove(paneId);

        if (paneId == FocusedPaneId)
            FocusedPaneId = nextPaneId;

        OnPropertyChanged(nameof(RootNode));
    }

    public void FocusPane(string paneId)
    {
        FocusedPaneId = paneId;
        Surface.FocusedPaneId = paneId;
    }

    [RelayCommand]
    public void FocusNextPane()
    {
        if (FocusedPaneId == null) return;
        var next = RootNode.GetNextLeaf(FocusedPaneId);
        if (next?.PaneId != null)
            FocusPane(next.PaneId);
    }

    [RelayCommand]
    public void FocusPreviousPane()
    {
        if (FocusedPaneId == null) return;
        var prev = RootNode.GetPreviousLeaf(FocusedPaneId);
        if (prev?.PaneId != null)
            FocusPane(prev.PaneId);
    }


    [RelayCommand]
    public void ToggleZoom() => IsZoomed = !IsZoomed;

    public void EqualizePanes()
    {
        RootNode.Equalize();
        OnPropertyChanged(nameof(RootNode));
    }

    partial void OnFocusedPaneIdChanged(string? value)
    {
        Surface.FocusedPaneId = value;
    }

    partial void OnNameChanged(string value)
    {
        Surface.Name = value;
    }

    public void Dispose()
    {
        CapturePaneSnapshotsForPersistence();

        // 断开守护进程事件
        var daemon = App.DaemonClient;
        daemon.RawOutputReceived -= OnDaemonRawOutput;
        daemon.CwdChanged -= OnDaemonCwdChanged;
        daemon.TitleChanged -= OnDaemonTitleChanged;
        daemon.SessionExited -= OnDaemonSessionExited;
        daemon.BellReceived -= OnDaemonBellReceived;
        daemon.Disconnected -= OnDaemonDisconnected;

        foreach (var session in _sessions.Values)
            session.Dispose();
        _sessions.Clear();
        _daemonPanes.Clear();
        _paneShells.Clear();
    }
}
