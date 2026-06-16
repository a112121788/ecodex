using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ECodex.Core.IPC;
using ECodex.Core.IPC.V2;
using ECodex.Core.Models;
using ECodex.Core.Services;
using BrowserScriptingRuntime = ECodex.Services.BrowserScriptingRuntime;
using SurfaceV2ApiService = ECodex.Services.SurfaceApiService<ECodex.ViewModels.WorkspaceViewModel, ECodex.ViewModels.SurfaceViewModel>;
using SurfaceV2ApiSurface = ECodex.Services.SurfaceApiSurface<ECodex.ViewModels.SurfaceViewModel>;
using SurfaceV2ApiWorkspace = ECodex.Services.SurfaceApiWorkspace<ECodex.ViewModels.WorkspaceViewModel, ECodex.ViewModels.SurfaceViewModel>;
using WorkspaceV2ApiService = ECodex.Services.WorkspaceApiService<ECodex.ViewModels.WorkspaceViewModel>;
using WorkspaceV2ApiWorkspace = ECodex.Services.WorkspaceApiWorkspace<ECodex.ViewModels.WorkspaceViewModel>;
using PaneV2ApiPane = ECodex.Services.PaneApiPane<string>;
using PaneV2ApiReadResult = ECodex.Services.PaneApiReadResult;
using PaneV2ApiService = ECodex.Services.PaneApiService<ECodex.ViewModels.WorkspaceViewModel, ECodex.ViewModels.SurfaceViewModel, string>;
using PaneV2ApiSurface = ECodex.Services.PaneApiSurface<ECodex.ViewModels.SurfaceViewModel, string>;
using PaneV2ApiWorkspace = ECodex.Services.PaneApiWorkspace<ECodex.ViewModels.WorkspaceViewModel, ECodex.ViewModels.SurfaceViewModel, string>;
using NotificationV2ApiService = ECodex.Services.NotificationApiService;
using ConfigV2ApiService = ECodex.Services.ConfigApiService;
using StatusV2ApiService = ECodex.Services.StatusApiService;
using AppLifecycleV2ApiService = ECodex.Services.AppLifecycleApiService;
using WorkspaceCreateRequest = ECodex.Services.WorkspaceCreateRequest;
using Microsoft.Win32;

namespace ECodex.ViewModels;

public sealed record RestoreSessionResult(
    int ScannedSurfaces,
    int PendingBindings,
    int TrustedStarted,
    string? FirstWorkspaceId,
    string? FirstWorkspaceName,
    string? FirstSurfaceId,
    string? FirstSurfaceName,
    string? FirstPaneId);

/// <summary>管理多个 Workspace 和侧边栏状态的主窗口 ViewModel</summary>
public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<WorkspaceViewModel> _workspaces = [];

    [ObservableProperty]
    private WorkspaceViewModel? _selectedWorkspace;

    [ObservableProperty]
    private bool _sidebarVisible = true;

    [ObservableProperty]
    private double _sidebarWidth = 280;

    [ObservableProperty]
    private bool _compactSidebar;

    private double _sidebarWidthBeforeCompact = 280;
    private static readonly JsonSerializerOptions BrowserScriptingJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public bool IsSidebarExpanded => !CompactSidebar;

    [ObservableProperty]
    private bool _notificationPanelVisible;

    [ObservableProperty]
    private int _totalUnreadCount;

    private readonly NotificationService _notificationService;
    private readonly BrowserScriptingService _browserScriptingService;
    private readonly SurfaceV2ApiService _surfaceApiService;
    private readonly WorkspaceV2ApiService _workspaceApiService;
    private readonly PaneV2ApiService _paneApiService;
    private readonly NotificationV2ApiService _notificationApiService;
    private readonly ConfigV2ApiService _configApiService;
    private readonly StatusV2ApiService _statusApiService;
    private readonly AppLifecycleV2ApiService _appLifecycleApiService;

    public NotificationService NotificationService => _notificationService;

    public event Action? WorkspaceOrderChanged;
    public event Action? SessionCheckpointRequested;
    public event Func<string>? ConfigReloadRequested;

    public MainViewModel()
    {
        _notificationService = App.NotificationService;
        _notificationService.UnreadCountChanged += () =>
        {
            TotalUnreadCount = _notificationService.UnreadCount;
            UpdateWorkspaceNotificationCounts();
        };

        _browserScriptingService = new BrowserScriptingService(
            () => Workspaces.SelectMany(workspace => workspace.Surfaces.Select(surface =>
                new BrowserScriptingSurfaceDescriptor(
                    WorkspaceId: workspace.Workspace.Id,
                    WorkspaceName: workspace.Name,
                    SurfaceId: surface.Surface.Id,
                    SurfaceName: surface.Name,
                    Kind: surface.Surface.Kind,
                    Url: surface.Surface.BrowserUrl,
                    Title: surface.Surface.BrowserTitle))));
        _surfaceApiService = new SurfaceV2ApiService(
            CreateSurfaceApiWorkspaces,
            MoveSurfaceForV2Api,
            ReorderSurfacesForV2Api,
            SelectSurfaceForV2Api);
        _workspaceApiService = new WorkspaceV2ApiService(
            CreateWorkspaceApiWorkspaces,
            CreateWorkspaceForV2Api,
            SelectWorkspaceForV2Api,
            CloseWorkspaceForV2Api,
            RenameWorkspaceForV2Api,
            ReorderWorkspacesForV2Api);
        _paneApiService = new PaneV2ApiService(
            CreatePaneApiWorkspaces,
            SelectSurfaceForV2Api,
            FocusPaneForV2Api,
            WritePaneForV2Api,
            ReadPaneForV2Api,
            SplitPaneForV2Api,
            ClosePaneForV2Api,
            ResizePaneForV2Api,
            SwapPanesForV2Api,
            ZoomSurfaceForV2Api);
        _notificationApiService = new NotificationV2ApiService(
            _notificationService,
            JumpToNotification);
        _configApiService = new ConfigV2ApiService(HandleConfigReload);
        _statusApiService = new StatusV2ApiService(HandleStatus);
        _appLifecycleApiService = new AppLifecycleV2ApiService(
            () => DaemonSessionTerminator.TerminateAllAsync(App.DaemonClient),
            RequestApplicationShutdownAfterResponse);

        // 连接命名管道的命令处理程序
        if (App.PipeServer != null)
        {
            App.PipeServer.OnCommand = HandlePipeCommand;
            App.PipeServer.OnV2Request = HandleV2PipeRequest;
        }

        // 恢复会话；首次启动由主窗口加载后引导用户选择项目文件夹。
        var session = SessionPersistenceService.Load();
        if (session != null && session.Workspaces.Count > 0)
        {
            RestoreSession(session);
        }
    }

    [RelayCommand]
    public void CreateNewWorkspace()
    {
        var directory = SelectWorkspaceDirectory("选择项目文件夹");
        if (directory == null)
            return;

        if (!TryCreateWorkspace(null, directory, out _, out var error))
        {
            MessageBox.Show(error, "无法创建项目", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void EnsureInitialWorkspace()
    {
        if (Workspaces.Count == 0)
            CreateNewWorkspace();
    }

    private bool TryCreateWorkspace(
        string? name,
        string? workingDirectory,
        out WorkspaceViewModel? workspaceVm,
        out string error)
    {
        workspaceVm = null;
        error = "";

        var normalizedDirectory = WorkspaceDirectoryService.Normalize(workingDirectory);
        if (string.IsNullOrWhiteSpace(normalizedDirectory))
        {
            error = "创建项目必须选择项目文件夹。";
            return false;
        }

        if (WorkspaceDirectoryService.IsDuplicate(Workspaces.Select(workspace => workspace.WorkingDirectory), normalizedDirectory))
        {
            error = $"该文件夹已经绑定到其他项目：{normalizedDirectory}";
            return false;
        }

        var fallbackName = $"项目 {Workspaces.Count + 1}";
        var workspaceName = string.IsNullOrWhiteSpace(name)
            ? WorkspaceDirectoryService.GetDefaultWorkspaceName(normalizedDirectory, fallbackName)
            : name.Trim();

        var workspace = new Workspace
        {
            Name = workspaceName,
            WorkingDirectory = normalizedDirectory,
        };
        var surface = new Surface { Name = "Terminal 1" };
        workspace.Surfaces.Add(surface);
        workspace.SelectedSurface = surface;

        workspaceVm = CreateWorkspaceViewModel(workspace);
        Workspaces.Add(workspaceVm);
        SelectedWorkspace = workspaceVm;
        RequestSessionCheckpoint();
        return true;
    }

    private WorkspaceViewModel CreateWorkspaceOrThrow(WorkspaceCreateRequest request)
    {
        if (TryCreateWorkspace(request.Name, request.WorkingDirectory, out var workspace, out var error) && workspace != null)
            return workspace;

        throw new InvalidOperationException(error);
    }

    private string? SelectWorkspaceDirectory(string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private WorkspaceViewModel CreateWorkspaceViewModel(Workspace workspace)
    {
        var vm = new WorkspaceViewModel(workspace, _notificationService);
        vm.SessionCheckpointRequested += OnWorkspaceSessionCheckpointRequested;
        return vm;
    }

    private void DetachWorkspaceViewModel(WorkspaceViewModel workspace)
    {
        workspace.SessionCheckpointRequested -= OnWorkspaceSessionCheckpointRequested;
    }

    private void OnWorkspaceSessionCheckpointRequested() => RequestSessionCheckpoint();

    private void RequestSessionCheckpoint() => SessionCheckpointRequested?.Invoke();

    public void DuplicateWorkspace(WorkspaceViewModel source)
    {
        var duplicateDirectory = SelectWorkspaceDirectory("为复制项目选择新的项目文件夹");
        if (duplicateDirectory == null)
            return;

        var normalizedDirectory = WorkspaceDirectoryService.Normalize(duplicateDirectory);
        if (string.IsNullOrWhiteSpace(normalizedDirectory))
            return;

        if (WorkspaceDirectoryService.IsDuplicate(Workspaces.Select(workspace => workspace.WorkingDirectory), normalizedDirectory))
        {
            MessageBox.Show($"该文件夹已经绑定到其他项目：{normalizedDirectory}", "无法复制项目", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var clone = new Workspace
        {
            Name = source.Name + " (copy)",
            IconGlyph = source.IconGlyph,
            AccentColor = source.AccentColor,
            WorkingDirectory = normalizedDirectory,
        };

        var surfaceMap = new Dictionary<string, Surface>();

        foreach (var sourceSurfaceVm in source.Surfaces)
        {
            var sourceSurface = sourceSurfaceVm.Surface;
            var paneIdMap = new Dictionary<string, string>();
            var clonedRoot = CloneSplitNode(sourceSurface.RootSplitNode, paneIdMap);

            var clonedSurface = new Surface
            {
                Name = sourceSurface.Name,
                Kind = sourceSurface.Kind,
                BrowserUrl = sourceSurface.BrowserUrl,
                BrowserTitle = sourceSurface.BrowserTitle,
                BrowserHistory = sourceSurface.BrowserHistory.ToList(),
                RootSplitNode = clonedRoot,
                FocusedPaneId = sourceSurface.FocusedPaneId != null && paneIdMap.TryGetValue(sourceSurface.FocusedPaneId, out var mappedFocused)
                    ? mappedFocused
                    : clonedRoot.GetLeaves().Select(l => l.PaneId).FirstOrDefault(),
            };

            foreach (var (oldPaneId, customName) in sourceSurface.PaneCustomNames)
            {
                if (paneIdMap.TryGetValue(oldPaneId, out var newPaneId))
                    clonedSurface.PaneCustomNames[newPaneId] = customName;
            }

            foreach (var (oldPaneId, snapshot) in sourceSurface.PaneSnapshots)
            {
                if (!paneIdMap.TryGetValue(oldPaneId, out var newPaneId))
                    continue;

                clonedSurface.PaneSnapshots[newPaneId] = new PaneStateSnapshot
                {
                    CapturedAt = snapshot.CapturedAt,
                    WorkingDirectory = clone.WorkingDirectory,
                    Shell = snapshot.Shell,
                    CommandHistory = snapshot.CommandHistory.ToList(),
                    BufferSnapshot = snapshot.BufferSnapshot == null
                        ? null
                        : new ECodex.Core.Terminal.TerminalBufferSnapshot
                        {
                            Cols = snapshot.BufferSnapshot.Cols,
                            Rows = snapshot.BufferSnapshot.Rows,
                            CursorRow = snapshot.BufferSnapshot.CursorRow,
                            CursorCol = snapshot.BufferSnapshot.CursorCol,
                            ScrollbackLines = snapshot.BufferSnapshot.ScrollbackLines.ToList(),
                            ScreenLines = snapshot.BufferSnapshot.ScreenLines.ToList(),
                        },
                };
            }

            clone.Surfaces.Add(clonedSurface);
            surfaceMap[sourceSurface.Id] = clonedSurface;
        }

        clone.SelectedSurface = source.SelectedSurface != null && surfaceMap.TryGetValue(source.SelectedSurface.Surface.Id, out var selected)
            ? selected
            : clone.Surfaces.FirstOrDefault();

        if (clone.Surfaces.Count == 0)
        {
            var fallbackSurface = new Surface { Name = "Terminal 1" };
            clone.Surfaces.Add(fallbackSurface);
            clone.SelectedSurface = fallbackSurface;
        }

        var vm = CreateWorkspaceViewModel(clone);
        Workspaces.Add(vm);
        SelectedWorkspace = vm;
        RequestSessionCheckpoint();
    }

    [RelayCommand]
    public void CloseWorkspace(WorkspaceViewModel? workspace)
    {
        if (workspace == null) return;
        if (Workspaces.Count <= 1) return; // Keep at least one

        int index = Workspaces.IndexOf(workspace);
        workspace.CaptureAllSurfaceTranscripts("workspace-close");
        DetachWorkspaceViewModel(workspace);
        workspace.Dispose();
        Workspaces.Remove(workspace);

        if (SelectedWorkspace == workspace)
        {
            SelectedWorkspace = Workspaces[Math.Min(index, Workspaces.Count - 1)];
        }

        RequestSessionCheckpoint();
    }

    [RelayCommand]
    public void SelectWorkspace(int index)
    {
        if (index >= 0 && index < Workspaces.Count)
        {
            SelectedWorkspace = Workspaces[index];
        }
    }

    public bool MoveWorkspace(WorkspaceViewModel workspace, int targetIndex)
    {
        var sourceIndex = Workspaces.IndexOf(workspace);
        if (sourceIndex < 0)
            return false;

        targetIndex = Math.Clamp(targetIndex, 0, Workspaces.Count - 1);
        if (sourceIndex == targetIndex)
            return false;

        Workspaces.Move(sourceIndex, targetIndex);
        WorkspaceOrderChanged?.Invoke();
        RequestSessionCheckpoint();
        return true;
    }

    public bool MoveWorkspaceBefore(WorkspaceViewModel workspace, WorkspaceViewModel target)
    {
        var targetIndex = Workspaces.IndexOf(target);
        return targetIndex >= 0 && MoveWorkspace(workspace, targetIndex);
    }

    [RelayCommand]
    public void NextWorkspace()
    {
        if (Workspaces.Count == 0) return;
        int index = SelectedWorkspace != null ? Workspaces.IndexOf(SelectedWorkspace) : -1;
        SelectedWorkspace = Workspaces[(index + 1) % Workspaces.Count];
    }

    [RelayCommand]
    public void PreviousWorkspace()
    {
        if (Workspaces.Count == 0) return;
        int index = SelectedWorkspace != null ? Workspaces.IndexOf(SelectedWorkspace) : 0;
        SelectedWorkspace = Workspaces[(index - 1 + Workspaces.Count) % Workspaces.Count];
    }

    [RelayCommand]
    public void ToggleSidebar() => SidebarVisible = !SidebarVisible;

    [RelayCommand]
    public void ToggleCompactSidebar() => CompactSidebar = !CompactSidebar;

    [RelayCommand]
    public void ToggleNotificationPanel() => NotificationPanelVisible = !NotificationPanelVisible;

    partial void OnCompactSidebarChanged(bool value)
    {
        if (value)
        {
            if (SidebarWidth > 120)
                _sidebarWidthBeforeCompact = SidebarWidth;

            SidebarWidth = 92;
        }
        else
        {
            SidebarWidth = Math.Max(220, _sidebarWidthBeforeCompact);
        }

        OnPropertyChanged(nameof(IsSidebarExpanded));
    }

    [RelayCommand]
    public void JumpToLatestUnread()
    {
        var latest = _notificationService.GetLatestUnread();
        JumpToNotification(latest);
    }

    public bool JumpToNotification(TerminalNotification? notification)
    {
        if (notification == null) return false;

        // 找到对应的项目和 Surface
        var workspace = Workspaces.FirstOrDefault(w => w.Workspace.Id == notification.WorkspaceId);
        if (workspace != null)
        {
            SelectedWorkspace = workspace;
            var surface = workspace.Surfaces.FirstOrDefault(s => s.Surface.Id == notification.SurfaceId);
            if (surface != null)
            {
                workspace.SelectedSurface = surface;
                if (notification.PaneId != null)
                {
                    surface.FocusPane(notification.PaneId);
                    surface.FlashPaneAttention(notification.PaneId);
                }
            }
            _notificationService.MarkAsRead(notification.Id);
            NotificationPanelVisible = false;
            return true;
        }

        return false;
    }

    [RelayCommand]
    public void MarkAllNotificationsRead()
    {
        _notificationService.MarkAllAsRead();
    }

    private void UpdateWorkspaceNotificationCounts()
    {
        foreach (var ws in Workspaces)
        {
            ws.UnreadNotificationCount = _notificationService.GetUnreadCount(ws.Workspace.Id);
            ws.LatestNotificationText = _notificationService.GetLatestText(ws.Workspace.Id);

            foreach (var surface in ws.Surfaces)
            {
                surface.UnreadNotificationCount = _notificationService.GetUnreadCount(
                    ws.Workspace.Id,
                    surface.Surface.Id);
                surface.RefreshNotificationState();
            }
        }
    }

    public void SaveSession(double windowX, double windowY, double windowWidth, double windowHeight, bool isMaximized, bool captureTranscripts = true)
    {
        // 在序列化前，先捕获终端脚本和内存中的终端上下文。
        foreach (var workspace in Workspaces)
        {
            foreach (var surface in workspace.Surfaces)
            {
                if (captureTranscripts)
                    surface.CaptureAllPaneTranscripts("session-close");
                surface.CapturePaneSnapshotsForPersistence();
            }
        }

        var workspaces = Workspaces.Select(w => w.Workspace).ToList();
        var state = SessionPersistenceService.BuildState(
            workspaces,
            SelectedWorkspace != null ? Workspaces.IndexOf(SelectedWorkspace) : null,
            windowX, windowY, windowWidth, windowHeight,
            isMaximized, SidebarWidth, SidebarVisible, CompactSidebar);
        SessionPersistenceService.Save(state);
    }

    private void RestoreSession(SessionState session)
    {
        var seenDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedWorkspaceId = session.SelectedWorkspaceIndex is int selectedIndex &&
                                  selectedIndex >= 0 &&
                                  selectedIndex < session.Workspaces.Count
            ? session.Workspaces[selectedIndex].Id
            : null;

        foreach (var wsState in session.Workspaces)
        {
            var normalizedDirectory = WorkspaceDirectoryService.Normalize(wsState.WorkingDirectory);
            if (!string.IsNullOrWhiteSpace(normalizedDirectory) && !seenDirectories.Add(normalizedDirectory))
                continue;

            var workspace = new Workspace
            {
                Id = wsState.Id,
                Name = wsState.Name,
                IconGlyph = string.IsNullOrWhiteSpace(wsState.IconGlyph) ? "\uE8A5" : wsState.IconGlyph,
                AccentColor = string.IsNullOrWhiteSpace(wsState.AccentColor) ? "#FF818CF8" : wsState.AccentColor,
                WorkingDirectory = normalizedDirectory,
            };

            foreach (var surfState in wsState.Surfaces)
            {
                var surface = new Surface
                {
                    Id = surfState.Id,
                    Name = surfState.Name,
                    Kind = surfState.Kind,
                    BrowserUrl = surfState.BrowserUrl,
                    BrowserTitle = surfState.BrowserTitle,
                    BrowserHistory = surfState.BrowserHistory.ToList(),
                    FocusedPaneId = surfState.FocusedPaneId,
                    PaneCustomNames = new Dictionary<string, string>(surfState.PaneCustomNames),
                    PaneSnapshots = surfState.PaneSnapshots.ToDictionary(
                        kvp => kvp.Key,
                        kvp => new PaneStateSnapshot
                        {
                            CapturedAt = kvp.Value.CapturedAt,
                            WorkingDirectory = kvp.Value.WorkingDirectory,
                            Shell = kvp.Value.Shell,
                            CommandHistory = kvp.Value.CommandHistory.ToList(),
                            BufferSnapshot = kvp.Value.BufferSnapshot == null
                                ? null
                                : new ECodex.Core.Terminal.TerminalBufferSnapshot
                                {
                                    Cols = kvp.Value.BufferSnapshot.Cols,
                                    Rows = kvp.Value.BufferSnapshot.Rows,
                                    CursorRow = kvp.Value.BufferSnapshot.CursorRow,
                                    CursorCol = kvp.Value.BufferSnapshot.CursorCol,
                                    ScrollbackLines = kvp.Value.BufferSnapshot.ScrollbackLines.ToList(),
                                    ScreenLines = kvp.Value.BufferSnapshot.ScreenLines.ToList(),
                                },
                        }),
                };

                if (surfState.RootNode != null)
                {
                    surface.RootSplitNode = SessionPersistenceService.DeserializeSplitNode(surfState.RootNode);
                }

                workspace.Surfaces.Add(surface);
            }

            if (wsState.SelectedSurfaceIndex.HasValue &&
                wsState.SelectedSurfaceIndex.Value >= 0 &&
                wsState.SelectedSurfaceIndex.Value < workspace.Surfaces.Count)
            {
                workspace.SelectedSurface = workspace.Surfaces[wsState.SelectedSurfaceIndex.Value];
            }
            else if (workspace.Surfaces.Count > 0)
            {
                workspace.SelectedSurface = workspace.Surfaces[0];
            }

            var vm = CreateWorkspaceViewModel(workspace);
            Workspaces.Add(vm);
        }

        SelectedWorkspace = !string.IsNullOrWhiteSpace(selectedWorkspaceId)
            ? Workspaces.FirstOrDefault(workspace => string.Equals(workspace.Workspace.Id, selectedWorkspaceId, StringComparison.Ordinal))
            : null;
        SelectedWorkspace ??= Workspaces.FirstOrDefault();

        if (session.Window != null)
        {
            SidebarWidth = Math.Clamp(session.Window.SidebarWidth, 220, 500);
            SidebarVisible = session.Window.SidebarVisible;
            CompactSidebar = false;
        }
    }

    private static SplitNode CloneSplitNode(SplitNode source, Dictionary<string, string> paneIdMap)
    {
        var clone = new SplitNode
        {
            IsLeaf = source.IsLeaf,
            Direction = source.Direction,
            SplitRatio = source.SplitRatio,
        };

        if (source.IsLeaf)
        {
            var oldPaneId = source.PaneId;
            var newPaneId = Guid.NewGuid().ToString();
            clone.PaneId = newPaneId;

            if (!string.IsNullOrWhiteSpace(oldPaneId))
                paneIdMap[oldPaneId] = newPaneId;
        }
        else
        {
            clone.First = source.First != null ? CloneSplitNode(source.First, paneIdMap) : null;
            clone.Second = source.Second != null ? CloneSplitNode(source.Second, paneIdMap) : null;
        }

        return clone;
    }

    private async Task<string> HandlePipeCommand(string command, Dictionary<string, string> args)
    {
        if (IsBrowserScriptingCommand(command))
            return await await Application.Current.Dispatcher.InvokeAsync(() => HandleBrowserScriptingCommandAsync(command, args));

        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            return command switch
            {
                "NOTIFY" => HandleNotifyCommand(args),
                "HOOK.COMMAND" => HandleHookCommand(args),
                "WORKSPACE.LIST" => HandleWorkspaceList(),
                "WORKSPACE.CREATE" => HandleWorkspaceCreate(args),
                "WORKSPACE.SELECT" => HandleWorkspaceSelect(args),
                "SURFACE.CREATE" => HandleSurfaceCreate(args),
                "SURFACE.SELECT" => HandleSurfaceSelect(args),
                "SURFACE.RESUME.SHOW" => HandleSurfaceResumeShow(args),
                "SURFACE.RESUME.SET" => HandleSurfaceResumeSet(args),
                "SURFACE.RESUME.CLEAR" => HandleSurfaceResumeClear(args),
                "SESSION.RESTORE" => HandleSessionRestore(args),
                "BROWSER.OPEN" => HandleBrowserOpen(args),
                "BROWSER.NEW" => HandleBrowserNew(args),
                "BROWSER.OPEN_SPLIT" => HandleBrowserOpenSplit(args),
                "SPLIT.RIGHT" => HandleSplit(SplitDirection.Vertical),
                "SPLIT.DOWN" => HandleSplit(SplitDirection.Horizontal),
                "PANE.LIST" => HandlePaneList(args),
                "PANE.FOCUS" => HandlePaneFocus(args),
                "PANE.WRITE" => HandlePaneWrite(args),
                "PANE.READ" => HandlePaneRead(args),
                "CONFIG.RELOAD" => HandleConfigReload(),
                "STATUS" => HandleStatus(),
                _ => JsonSerializer.Serialize(new { error = $"Unknown command: {command}" }),
            };
        });
    }

    private async Task<V2Response> HandleV2PipeRequest(V2Request request)
    {
        try
        {
            if (AppLifecycleV2ApiService.CanHandle(request.Method))
                return await _appLifecycleApiService.HandleRequestAsync(request);

            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                return request.Method switch
                {
                    var method when ECodex.Services.WindowApiService<Window>.CanHandle(method) => App.WindowApi.HandleRequest(request),
                    var method when WorkspaceV2ApiService.CanHandle(method) => _workspaceApiService.HandleRequest(request),
                    var method when SurfaceV2ApiService.CanHandle(method) => _surfaceApiService.HandleRequest(request),
                    var method when PaneV2ApiService.CanHandle(method) => _paneApiService.HandleRequest(request),
                    var method when NotificationV2ApiService.CanHandle(method) => _notificationApiService.HandleRequest(request),
                    var method when ConfigV2ApiService.CanHandle(method) => _configApiService.HandleRequest(request),
                    var method when StatusV2ApiService.CanHandle(method) => _statusApiService.HandleRequest(request),
                    _ => V2Response.FromStableError(
                        request.Id,
                        V2ErrorCodes.NotSupported,
                        $"ecodex.v2 method is not supported yet: {request.Method}"),
                };
            });
        }
        catch (Exception ex)
        {
            return V2Response.FromStableError(request.Id, V2ErrorCodes.InternalError, ex.Message);
        }
    }

    private static void RequestApplicationShutdownAfterResponse()
    {
        var app = Application.Current;
        if (app?.Dispatcher == null)
            return;

        _ = Task.Run(async () =>
        {
            await Task.Delay(150).ConfigureAwait(false);
            await app.Dispatcher.InvokeAsync(() => App.RequestShutdown(0));
        });
    }

    private static JsonElement ParseJsonElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static bool IsBrowserScriptingCommand(string command)
    {
        return command is
            BrowserScriptingCliCommands.Snapshot or
            BrowserScriptingCliCommands.Click or
            BrowserScriptingCliCommands.Fill or
            BrowserScriptingCliCommands.Hover or
            BrowserScriptingCliCommands.Press or
            BrowserScriptingCliCommands.Eval or
            BrowserScriptingCliCommands.Screenshot;
    }

    private string HandleConfigReload()
    {
        return ConfigReloadRequested?.Invoke()
            ?? JsonSerializer.Serialize(new
            {
                ok = true,
                loadedPaths = Array.Empty<string>(),
                diagnostics = Array.Empty<object>(),
                note = "No config reload handler registered.",
            });
    }

    private async Task<string> HandleBrowserScriptingCommandAsync(string command, Dictionary<string, string> args)
    {
        if (!TryGetBrowserSurfaceRef(args, out var surfaceRef, out var refError))
            return SerializeBrowserScriptingError(refError!, null);

        var resolved = _browserScriptingService.ResolveSurfaceRef(surfaceRef);
        if (!resolved.Success)
            return SerializeBrowserScriptingError(resolved.Error!, resolved.Diagnostics);

        if (command == BrowserScriptingCliCommands.Snapshot)
        {
            var snapshot = await BrowserScriptingRuntime.GetSnapshotAsync(resolved.Surface!.SurfaceId);
            if (snapshot == null)
            {
                return SerializeBrowserScriptingError(
                    new V2Error(V2ErrorCodes.NotFound, $"Browser snapshot not available: {resolved.Surface.SurfaceId}"),
                    resolved.Diagnostics);
            }

            return SerializeBrowserScriptingSuccess(new
            {
                resolved.Diagnostics.SurfaceRef,
                resolved.Surface.SurfaceId,
                snapshot,
            });
        }

        if (command is BrowserScriptingCliCommands.Eval or BrowserScriptingCliCommands.Screenshot)
        {
            var script = GetArg(args, "script", "_arg0") ?? "";
            if (command == BrowserScriptingCliCommands.Eval && string.IsNullOrWhiteSpace(script))
            {
                return SerializeBrowserScriptingError(
                    new V2Error(V2ErrorCodes.InvalidRef, "Missing required argument: script"),
                    resolved.Diagnostics);
            }

            var action = command == BrowserScriptingCliCommands.Eval
                ? BrowserScriptingActionKind.Eval
                : BrowserScriptingActionKind.Screenshot;
            var outcome = await BrowserScriptingRuntime.ExecuteActionAsync(new BrowserScriptingActionRequest(
                SurfaceId: resolved.Surface!.SurfaceId,
                Action: action,
                Node: null,
                Value: null,
                Key: null,
                Script: script));

            return SerializeBrowserActionOutcome(outcome, resolved.Diagnostics);
        }

        if (!TryCreateBrowserLocator(args, out var locator, out var locatorError))
            return SerializeBrowserScriptingError(locatorError!, resolved.Diagnostics);

        var liveSnapshot = await BrowserScriptingRuntime.GetSnapshotAsync(resolved.Surface!.SurfaceId);
        if (liveSnapshot == null)
        {
            return SerializeBrowserScriptingError(
                new V2Error(V2ErrorCodes.NotFound, $"Browser snapshot not available: {resolved.Surface.SurfaceId}"),
                resolved.Diagnostics);
        }

        var node = BrowserScriptingService
            .MatchLocator(liveSnapshot, BrowserScriptingLocator.First(locator!))
            .FirstOrDefault();
        if (node == null)
        {
            return SerializeBrowserScriptingError(
                new V2Error(V2ErrorCodes.NotFound, "Locator did not match any node."),
                resolved.Diagnostics);
        }

        var kind = command switch
        {
            BrowserScriptingCliCommands.Click => BrowserScriptingActionKind.Click,
            BrowserScriptingCliCommands.Fill => BrowserScriptingActionKind.Fill,
            BrowserScriptingCliCommands.Hover => BrowserScriptingActionKind.Hover,
            BrowserScriptingCliCommands.Press => BrowserScriptingActionKind.Press,
            _ => BrowserScriptingActionKind.Click,
        };

        var value = args.ContainsKey("value")
            ? args["value"]
            : GetArg(args, "_arg1");
        if (command == BrowserScriptingCliCommands.Fill && value == null)
        {
            return SerializeBrowserScriptingError(
                new V2Error(V2ErrorCodes.InvalidRef, "Missing required argument: value"),
                resolved.Diagnostics);
        }

        var key = GetArg(args, "key", "_arg1") ?? "Enter";
        var nodeOutcome = await BrowserScriptingRuntime.ExecuteActionAsync(new BrowserScriptingActionRequest(
            SurfaceId: resolved.Surface!.SurfaceId,
            Action: kind,
            Node: node,
            Value: value,
            Key: key,
            Script: null));

        return SerializeBrowserActionOutcome(nodeOutcome, resolved.Diagnostics);
    }

    private bool TryGetBrowserSurfaceRef(
        Dictionary<string, string> args,
        out string surfaceRef,
        out V2Error? error)
    {
        surfaceRef = GetArg(args, "surfaceRef") ?? "";
        error = null;
        if (!string.IsNullOrWhiteSpace(surfaceRef))
            return true;

        if (!TryResolveWorkspace(args, out var workspace, out var workspaceError))
        {
            error = new V2Error(V2ErrorCodes.NotFound, workspaceError);
            return false;
        }

        SurfaceViewModel? surface = null;
        if (HasSurfaceSelector(args))
        {
            if (!TryResolveSurface(workspace, args, out var resolvedSurface, out var surfaceError))
            {
                error = new V2Error(V2ErrorCodes.NotFound, surfaceError);
                return false;
            }

            surface = resolvedSurface;
        }
        else
        {
            surface = workspace.SelectedSurface?.Surface.Kind == SurfaceKind.Browser
                ? workspace.SelectedSurface
                : workspace.Surfaces.FirstOrDefault(item => item.Surface.Kind == SurfaceKind.Browser);
        }

        if (surface == null)
        {
            error = new V2Error(V2ErrorCodes.NotFound, "No browser surface available.");
            return false;
        }

        if (surface.Surface.Kind != SurfaceKind.Browser)
        {
            error = new V2Error(V2ErrorCodes.NotSupported, $"Surface is not a browser surface: {surface.Surface.Id}");
            return false;
        }

        surfaceRef = TrackBrowserSurface(workspace, surface);
        return true;
    }

    private static bool TryCreateBrowserLocator(
        Dictionary<string, string> args,
        out BrowserScriptingLocator? locator,
        out V2Error? error)
    {
        locator = null;
        error = null;

        if (TryGetNonEmpty(args, "testid", out var testId) || TryGetNonEmpty(args, "testId", out testId))
        {
            locator = BrowserScriptingLocator.TestId(testId);
            return true;
        }

        if (TryGetNonEmpty(args, "text", out var text))
        {
            locator = BrowserScriptingLocator.Text(text);
            return true;
        }

        if (TryGetNonEmpty(args, "role", out var role))
        {
            locator = BrowserScriptingLocator.Role(role, GetArg(args, "name"));
            return true;
        }

        error = new V2Error(V2ErrorCodes.InvalidRef, "Missing locator. Use --testid, --text, or --role.");
        return false;
    }

    private static bool TryGetNonEmpty(Dictionary<string, string> args, string key, out string value)
    {
        value = "";
        if (!args.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return false;

        value = raw;
        return true;
    }

    private static string SerializeBrowserActionOutcome(
        BrowserScriptingActionOutcome outcome,
        BrowserScriptingDiagnostics diagnostics)
    {
        return outcome.Success
            ? SerializeBrowserScriptingSuccess(new { value = outcome.Value, diagnostics })
            : SerializeBrowserScriptingError(outcome.Error!, diagnostics);
    }

    private static string SerializeBrowserScriptingSuccess(object payload)
    {
        return JsonSerializer.Serialize(new
        {
            ok = true,
            result = payload,
        }, BrowserScriptingJsonOptions);
    }

    private static string SerializeBrowserScriptingError(V2Error error, BrowserScriptingDiagnostics? diagnostics)
    {
        return JsonSerializer.Serialize(new
        {
            ok = false,
            error,
            diagnostics,
        }, BrowserScriptingJsonOptions);
    }

    private string HandleNotifyCommand(Dictionary<string, string> args)
    {
        var title = args.GetValueOrDefault("title", "Terminal");
        var body = args.GetValueOrDefault("body", "");
        var subtitle = args.GetValueOrDefault("subtitle");
        var workspaceId = SelectedWorkspace?.Workspace.Id ?? "";
        var surfaceId = SelectedWorkspace?.SelectedSurface?.Surface.Id ?? "";

        _notificationService.AddNotification(
            workspaceId, surfaceId, null,
            title, subtitle, body,
            NotificationSource.Cli);

        return JsonSerializer.Serialize(new { ok = true });
    }

    private string HandleHookCommand(Dictionary<string, string> args)
    {
        var phase = args.GetValueOrDefault("phase", "");
        var command = args.GetValueOrDefault("command", "");
        var exitCode = args.GetValueOrDefault("exitCode", "");
        var cwd = args.GetValueOrDefault("cwd", "");
        var sanitized = App.CommandLogService.SanitizeCommandForStorage(command) ?? "[redacted]";
        App.DaemonLog($"[Hook] phase={phase} exitCode={exitCode} cwd={cwd} command={sanitized}");

        return JsonSerializer.Serialize(new { ok = true });
    }

    private string HandleWorkspaceList()
    {
        var list = Workspaces.Select(w => new
        {
            id = w.Workspace.Id,
            name = w.Workspace.Name,
            selected = w == SelectedWorkspace,
            surfaces = w.Surfaces.Count,
            workingDirectory = w.WorkingDirectory,
        });
        return JsonSerializer.Serialize(list);
    }

    private string HandleWorkspaceCreate(Dictionary<string, string> args)
    {
        var name = args.GetValueOrDefault("name");
        var workingDirectory = GetArg(args, "workingDirectory", "cwd", "folder", "path");
        if (!TryCreateWorkspace(name, workingDirectory, out var ws, out var error) || ws == null)
        {
            return JsonSerializer.Serialize(new { ok = false, error });
        }

        return JsonSerializer.Serialize(new
        {
            ok = true,
            id = ws.Workspace.Id,
            name = ws.Name,
            workingDirectory = ws.WorkingDirectory,
        });
    }

    private string HandleWorkspaceSelect(Dictionary<string, string> args)
    {
        if (args.TryGetValue("index", out var indexStr) && int.TryParse(indexStr, out int index))
        {
            if (TryResolveCollectionIndex(index, Workspaces.Count, out var resolvedIndex))
            {
                SelectWorkspace(resolvedIndex);
                return JsonSerializer.Serialize(new { ok = true });
            }
        }
        if (args.TryGetValue("id", out var id))
        {
            var ws = Workspaces.FirstOrDefault(w => w.Workspace.Id == id);
            if (ws != null)
            {
                SelectedWorkspace = ws;
                return JsonSerializer.Serialize(new { ok = true });
            }
        }
        if (args.TryGetValue("name", out var name))
        {
            var ws = Workspaces.FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? Workspaces.FirstOrDefault(w => w.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (ws != null)
            {
                SelectedWorkspace = ws;
                return JsonSerializer.Serialize(new { ok = true });
            }
        }
        return JsonSerializer.Serialize(new { error = "Project not found" });
    }

    private string HandleSurfaceCreate(Dictionary<string, string> args)
    {
        SelectedWorkspace?.CreateNewSurface();
        return JsonSerializer.Serialize(new { ok = true });
    }

    private string HandleSurfaceSelect(Dictionary<string, string> args)
    {
        if (!TryResolveWorkspace(args, out var workspace, out var error))
            return JsonSerializer.Serialize(new { error });

        if (!TryResolveSurface(workspace, args, out var surface, out error))
            return JsonSerializer.Serialize(new { error });

        SelectedWorkspace = workspace;
        workspace.SelectedSurface = surface;

        return JsonSerializer.Serialize(new
        {
            ok = true,
            workspaceId = workspace.Workspace.Id,
            workspaceName = workspace.Name,
            surfaceId = surface.Surface.Id,
            surfaceName = surface.Name,
        });
    }

    private string HandleSurfaceResumeShow(Dictionary<string, string> args)
    {
        if (!TryResolveWorkspace(args, out var workspace, out var error))
            return JsonSerializer.Serialize(new { error });

        if (!TryResolveSurface(workspace, args, out var surface, out error))
            return JsonSerializer.Serialize(new { error });

        var bindings = new ResumeBindingService()
            .FindForSurface(workspace.Workspace.Id, surface.Surface.Id);

        object? pane = null;
        if (!IsTruthy(args, "all"))
        {
            if (!TryResolvePaneId(surface, args, out var paneId, out var paneIndex, out var paneName, out error))
                return JsonSerializer.Serialize(new { error });

            bindings = bindings
                .Where(b => string.Equals(b.PaneId, paneId, StringComparison.Ordinal))
                .ToList();

            pane = CreatePaneResponse(surface, paneId, paneIndex, paneName);
        }

        return JsonSerializer.Serialize(new
        {
            ok = true,
            workspace = new
            {
                id = workspace.Workspace.Id,
                name = workspace.Name,
            },
            surface = new
            {
                id = surface.Surface.Id,
                name = surface.Name,
            },
            pane,
            bindings,
        });
    }

    private string HandleSurfaceResumeSet(Dictionary<string, string> args)
    {
        if (!TryResolveWorkspace(args, out var workspace, out var error))
            return JsonSerializer.Serialize(new { error });

        if (!TryResolveSurface(workspace, args, out var surface, out error))
            return JsonSerializer.Serialize(new { error });

        if (!TryResolvePaneId(surface, args, out var paneId, out var paneIndex, out var paneName, out error))
            return JsonSerializer.Serialize(new { error });

        var shell = GetArg(args, "shell", "command") ?? GetJoinedPositionals(args);
        if (string.IsNullOrWhiteSpace(shell))
            return JsonSerializer.Serialize(new { error = "Missing required argument: shell" });

        var kind = (GetArg(args, "kind") ?? ResumeBindingKinds.Custom).Trim().ToLowerInvariant();
        if (kind is not (ResumeBindingKinds.Tmux or ResumeBindingKinds.Custom))
            return JsonSerializer.Serialize(new { error = $"Unsupported resume kind: {kind}" });

        var workingDirectory = GetArg(args, "workingDirectory", "cwd");
        if (string.IsNullOrWhiteSpace(workingDirectory))
            workingDirectory = surface.GetSession(paneId)?.WorkingDirectory;

        var trusted = TryGetBool(args, "trusted", out var parsedTrusted) && parsedTrusted;
        var approvedPrefix = GetArg(args, "approvedPrefix", "prefix");
        var binding = new ResumeBinding
        {
            Id = GetArg(args, "id") ?? "",
            WorkspaceId = workspace.Workspace.Id,
            SurfaceId = surface.Surface.Id,
            PaneId = paneId,
            Kind = kind,
            Checkpoint = GetArg(args, "checkpoint"),
            Shell = shell,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory,
            Trusted = trusted,
            TrustReason = trusted
                ? (string.IsNullOrWhiteSpace(approvedPrefix) ? "cli" : "user-approved-prefix")
                : null,
            ApprovedPrefix = string.IsNullOrWhiteSpace(approvedPrefix) ? null : approvedPrefix,
        };

        var saved = new ResumeBindingService().SetForPane(binding);
        surface.RefreshResumeBindings();
        return JsonSerializer.Serialize(new
        {
            ok = true,
            workspaceId = workspace.Workspace.Id,
            workspaceName = workspace.Name,
            surfaceId = surface.Surface.Id,
            surfaceName = surface.Name,
            paneId,
            paneIndex,
            paneName,
            binding = saved,
        });
    }

    private string HandleSurfaceResumeClear(Dictionary<string, string> args)
    {
        var service = new ResumeBindingService();
        if (args.TryGetValue("id", out var bindingId) && !string.IsNullOrWhiteSpace(bindingId))
        {
            var removedById = service.Remove(bindingId);
            if (removedById)
                RefreshResumeBindings();
            return JsonSerializer.Serialize(new
            {
                ok = removedById,
                removed = removedById ? 1 : 0,
                id = bindingId,
            });
        }

        if (!TryResolveWorkspace(args, out var workspace, out var error))
            return JsonSerializer.Serialize(new { error });

        if (!TryResolveSurface(workspace, args, out var surface, out error))
            return JsonSerializer.Serialize(new { error });

        if (!TryResolvePaneId(surface, args, out var paneId, out var paneIndex, out var paneName, out error))
            return JsonSerializer.Serialize(new { error });

        var removed = service.RemoveForPane(workspace.Workspace.Id, surface.Surface.Id, paneId);
        if (removed > 0)
            surface.RefreshResumeBindings();
        return JsonSerializer.Serialize(new
        {
            ok = true,
            removed,
            workspaceId = workspace.Workspace.Id,
            workspaceName = workspace.Name,
            surfaceId = surface.Surface.Id,
            surfaceName = surface.Name,
            paneId,
            paneIndex,
            paneName,
        });
    }

    private void RefreshResumeBindings()
    {
        foreach (var workspace in Workspaces)
        {
            foreach (var surface in workspace.Surfaces)
                surface.RefreshResumeBindings();
        }
    }

    public RestoreSessionResult RestoreResumeBindingsForSelected(bool runTrusted = false, bool focusFirst = true)
    {
        var workspace = SelectedWorkspace;
        var surface = workspace?.SelectedSurface;
        if (workspace == null || surface == null)
            return new RestoreSessionResult(0, 0, 0, null, null, null, null, null);

        return RestoreResumeBindings([(workspace, surface)], runTrusted, focusFirst);
    }

    private string HandleSessionRestore(Dictionary<string, string> args)
    {
        var runTrusted = IsTruthy(args, "trusted") || IsTruthy(args, "runTrusted");
        var focusFirst = !IsTruthy(args, "noFocus");

        List<(WorkspaceViewModel workspace, SurfaceViewModel surface)> targets = [];
        if (IsTruthy(args, "all"))
        {
            targets.AddRange(Workspaces.SelectMany(workspace => workspace.Surfaces
                .Select(surface => (workspace, surface))));
        }
        else
        {
            if (!TryResolveWorkspace(args, out var workspace, out var error))
                return JsonSerializer.Serialize(new { error });

            if (!TryResolveSurface(workspace, args, out var surface, out error))
                return JsonSerializer.Serialize(new { error });

            targets.Add((workspace, surface));
        }

        var result = RestoreResumeBindings(targets, runTrusted, focusFirst);
        return JsonSerializer.Serialize(new
        {
            ok = true,
            scannedSurfaces = result.ScannedSurfaces,
            pendingBindings = result.PendingBindings,
            trustedStarted = result.TrustedStarted,
            firstPending = result.FirstPaneId == null
                ? null
                : new
                {
                    workspaceId = result.FirstWorkspaceId,
                    workspaceName = result.FirstWorkspaceName,
                    surfaceId = result.FirstSurfaceId,
                    surfaceName = result.FirstSurfaceName,
                    paneId = result.FirstPaneId,
                },
        });
    }

    private RestoreSessionResult RestoreResumeBindings(
        IReadOnlyList<(WorkspaceViewModel workspace, SurfaceViewModel surface)> targets,
        bool runTrusted,
        bool focusFirst)
    {
        var scanned = 0;
        var pending = 0;
        var trustedStarted = 0;
        (WorkspaceViewModel workspace, SurfaceViewModel surface, ResumeBinding binding)? firstPending = null;

        foreach (var (workspace, surface) in targets)
        {
            scanned++;
            surface.RefreshResumeBindings();
            if (runTrusted)
                trustedStarted += surface.RunTrustedResumeBindings(requireEnabledSetting: false);

            var bindings = surface.GetPendingResumeBindings();
            pending += bindings.Count;
            if (firstPending == null && bindings.Count > 0)
                firstPending = (workspace, surface, bindings[0]);
        }

        if (focusFirst && firstPending is { } target)
        {
            SelectedWorkspace = target.workspace;
            target.workspace.SelectedSurface = target.surface;
            target.surface.FocusPane(target.binding.PaneId);
            target.surface.FlashPaneAttention(target.binding.PaneId);
        }

        return new RestoreSessionResult(
            scanned,
            pending,
            trustedStarted,
            firstPending?.workspace.Workspace.Id,
            firstPending?.workspace.Name,
            firstPending?.surface.Surface.Id,
            firstPending?.surface.Name,
            firstPending?.binding.PaneId);
    }

    private string HandleBrowserOpen(Dictionary<string, string> args)
    {
        if (!TryResolveWorkspace(args, out var workspace, out var error))
            return JsonSerializer.Serialize(new { error });

        var url = GetBrowserUrl(args);
        if (string.IsNullOrWhiteSpace(url))
            return JsonSerializer.Serialize(new { error = "Missing required argument: url" });

        SurfaceViewModel? surface = null;
        if (HasSurfaceSelector(args))
        {
            if (!TryResolveSurface(workspace, args, out var resolvedSurface, out error))
                return JsonSerializer.Serialize(new { error });

            surface = resolvedSurface;
            if (surface.Surface.Kind != SurfaceKind.Browser)
                surface = null;
        }
        else if (workspace.SelectedSurface?.Surface.Kind == SurfaceKind.Browser)
        {
            surface = workspace.SelectedSurface;
        }

        var created = surface == null;
        surface ??= workspace.CreateBrowserSurface(url, GetArg(args, "name", "title"));
        surface.OpenBrowserUrl(url);
        SelectedWorkspace = workspace;
        workspace.SelectedSurface = surface;

        return CreateBrowserResponse(workspace, surface, created, fallbackMode: null);
    }

    private string HandleBrowserNew(Dictionary<string, string> args)
    {
        if (!TryResolveWorkspace(args, out var workspace, out var error))
            return JsonSerializer.Serialize(new { error });

        var url = GetBrowserUrl(args);
        if (string.IsNullOrWhiteSpace(url))
            return JsonSerializer.Serialize(new { error = "Missing required argument: url" });

        var surface = workspace.CreateBrowserSurface(url, GetArg(args, "name", "title"));
        SelectedWorkspace = workspace;
        return CreateBrowserResponse(workspace, surface, created: true, fallbackMode: null);
    }

    private string HandleBrowserOpenSplit(Dictionary<string, string> args)
    {
        if (!TryResolveWorkspace(args, out var workspace, out var error))
            return JsonSerializer.Serialize(new { error });

        var url = GetBrowserUrl(args);
        if (string.IsNullOrWhiteSpace(url))
            return JsonSerializer.Serialize(new { error = "Missing required argument: url" });

        var surface = workspace.CreateBrowserSurface(url, GetArg(args, "name", "title"));
        SelectedWorkspace = workspace;
        return CreateBrowserResponse(
            workspace,
            surface,
            created: true,
            fallbackMode: "new-surface",
            direction: GetArg(args, "direction") ?? "right");
    }

    private string CreateBrowserResponse(
        WorkspaceViewModel workspace,
        SurfaceViewModel surface,
        bool created,
        string? fallbackMode,
        string? direction = null)
    {
        var surfaceRef = TrackBrowserSurface(workspace, surface);
        return JsonSerializer.Serialize(new
        {
            ok = true,
            created,
            fallbackMode,
            direction,
            workspaceId = workspace.Workspace.Id,
            workspaceName = workspace.Name,
            surfaceId = surface.Surface.Id,
            surfaceRef,
            surfaceName = surface.Name,
            kind = surface.Surface.Kind.ToString(),
            url = surface.Surface.BrowserUrl,
            title = surface.Surface.BrowserTitle,
        });
    }

    private string TrackBrowserSurface(WorkspaceViewModel workspace, SurfaceViewModel surface)
    {
        return _browserScriptingService.TrackSurface(new BrowserScriptingSurfaceDescriptor(
            WorkspaceId: workspace.Workspace.Id,
            WorkspaceName: workspace.Name,
            SurfaceId: surface.Surface.Id,
            SurfaceName: surface.Name,
            Kind: surface.Surface.Kind,
            Url: surface.Surface.BrowserUrl,
            Title: surface.Surface.BrowserTitle));
    }

    private IEnumerable<WorkspaceV2ApiWorkspace> CreateWorkspaceApiWorkspaces()
    {
        return Workspaces.Select((workspace, index) =>
            new WorkspaceV2ApiWorkspace(
                Workspace: workspace,
                WorkspaceId: workspace.Workspace.Id,
                WorkspaceName: workspace.Name,
                WorkspaceRef: new ShortRef(ShortRefKind.Workspace, index + 1),
                IsCurrent: workspace == SelectedWorkspace,
                SurfaceCount: workspace.Surfaces.Count,
                WorkingDirectory: workspace.WorkingDirectory));
    }

    private WorkspaceViewModel CreateWorkspaceForV2Api(WorkspaceCreateRequest request)
    {
        var workspace = CreateWorkspaceOrThrow(request);
        WorkspaceOrderChanged?.Invoke();
        return workspace;
    }

    private void SelectWorkspaceForV2Api(WorkspaceViewModel workspace)
    {
        SelectedWorkspace = workspace;
    }

    private bool CloseWorkspaceForV2Api(WorkspaceViewModel workspace)
    {
        if (Workspaces.Count <= 1 || !Workspaces.Contains(workspace))
            return false;

        CloseWorkspace(workspace);
        WorkspaceOrderChanged?.Invoke();
        return true;
    }

    private bool RenameWorkspaceForV2Api(WorkspaceViewModel workspace, string name)
    {
        if (!Workspaces.Contains(workspace) || string.IsNullOrWhiteSpace(name))
            return false;

        workspace.Name = name.Trim();
        WorkspaceOrderChanged?.Invoke();
        RequestSessionCheckpoint();
        return true;
    }

    private bool ReorderWorkspacesForV2Api(IReadOnlyList<string> workspaceIds)
    {
        if (workspaceIds.Count != Workspaces.Count)
            return false;

        var currentOrder = Workspaces.Select(workspace => workspace.Workspace.Id).ToList();
        if (currentOrder.SequenceEqual(workspaceIds))
            return true;

        for (var targetIndex = 0; targetIndex < workspaceIds.Count; targetIndex++)
        {
            var workspace = Workspaces.FirstOrDefault(item => item.Workspace.Id == workspaceIds[targetIndex]);
            if (workspace == null)
                return false;

            MoveWorkspace(workspace, targetIndex);
        }

        WorkspaceOrderChanged?.Invoke();
        return true;
    }

    private IEnumerable<PaneV2ApiWorkspace> CreatePaneApiWorkspaces()
    {
        return Workspaces.Select((workspace, workspaceIndex) =>
            new PaneV2ApiWorkspace(
                Workspace: workspace,
                WorkspaceId: workspace.Workspace.Id,
                WorkspaceName: workspace.Name,
                WorkspaceRef: new ShortRef(ShortRefKind.Workspace, workspaceIndex + 1),
                IsCurrent: workspace == SelectedWorkspace,
                Surfaces: workspace.Surfaces
                    .Select((surface, surfaceIndex) =>
                        new PaneV2ApiSurface(
                            Surface: surface,
                            SurfaceId: surface.Surface.Id,
                            SurfaceName: surface.Name,
                            SurfaceRef: new ShortRef(ShortRefKind.Surface, surfaceIndex + 1),
                            IsCurrent: surface == workspace.SelectedSurface,
                            Kind: surface.Surface.Kind.ToString(),
                            IsZoomed: surface.IsZoomed,
                            Panes: CreatePaneApiPanes(surface)))
                    .ToList()));
    }

    private static IReadOnlyList<PaneV2ApiPane> CreatePaneApiPanes(SurfaceViewModel surface)
    {
        return surface.RootNode.GetLeaves()
            .Select(leaf => leaf.PaneId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Select((paneId, paneIndex) =>
            {
                surface.Surface.PaneCustomNames.TryGetValue(paneId, out var customName);
                return new PaneV2ApiPane(
                    Pane: paneId,
                    PaneId: paneId,
                    PaneRef: new ShortRef(ShortRefKind.Pane, paneIndex + 1),
                    PaneName: string.IsNullOrWhiteSpace(customName) ? $"Pane {paneIndex + 1}" : customName!,
                    IsFocused: string.Equals(surface.FocusedPaneId, paneId, StringComparison.Ordinal),
                    WorkingDirectory: surface.GetSession(paneId)?.WorkingDirectory ?? "");
            })
            .ToList();
    }

    private bool FocusPaneForV2Api(SurfaceViewModel surface, string paneId)
    {
        if (surface.RootNode.FindNode(paneId) == null)
            return false;

        surface.FocusPane(paneId);
        return true;
    }

    private bool WritePaneForV2Api(SurfaceViewModel surface, string paneId, string text, bool submit, string submitKey)
    {
        if (!FocusPaneForV2Api(surface, paneId))
            return false;

        var session = surface.GetSession(paneId);
        if (session == null)
            return false;

        var textToWrite = submit ? text.TrimEnd('\r', '\n') : text;
        if (!string.IsNullOrEmpty(textToWrite))
            session.Write(textToWrite);

        if (submit)
        {
            var submitSequence = ResolveSubmitSequence(submitKey);
            if (!string.IsNullOrEmpty(submitSequence))
                session.Write(submitSequence);

            if (!string.IsNullOrWhiteSpace(textToWrite))
                surface.RegisterCommandSubmission(paneId, textToWrite);
        }

        return true;
    }

    private PaneV2ApiReadResult? ReadPaneForV2Api(SurfaceViewModel surface, string paneId, int lines, int maxChars)
    {
        var session = surface.GetSession(paneId);
        if (session == null)
            return null;

        var allText = session.Buffer.ExportPlainText(maxScrollbackLines: 20000);
        var tailText = TailLines(allText, lines);
        if (tailText.Length > maxChars)
            tailText = "..." + tailText[^maxChars..];

        return new PaneV2ApiReadResult(tailText, lines, maxChars);
    }

    private bool SplitPaneForV2Api(SurfaceViewModel surface, string paneId, SplitDirection direction, string? shell)
    {
        var before = surface.RootNode.GetLeaves().Count();
        if (!FocusPaneForV2Api(surface, paneId))
            return false;

        surface.SplitFocused(direction, shell);
        return surface.RootNode.GetLeaves().Count() > before;
    }

    private bool ClosePaneForV2Api(SurfaceViewModel surface, string paneId)
    {
        if (surface.RootNode.GetLeaves().Count() <= 1)
            return false;

        if (surface.RootNode.FindNode(paneId) == null)
            return false;

        surface.ClosePane(paneId);
        return surface.RootNode.FindNode(paneId) == null;
    }

    private bool ResizePaneForV2Api(SurfaceViewModel surface, string paneId, double delta)
    {
        return surface.ResizePane(paneId, delta);
    }

    private bool SwapPanesForV2Api(SurfaceViewModel surface, string paneId, string otherPaneId)
    {
        return surface.SwapPanes(paneId, otherPaneId);
    }

    private bool ZoomSurfaceForV2Api(SurfaceViewModel surface, bool? zoomed)
    {
        return surface.SetZoom(zoomed);
    }

    private IEnumerable<SurfaceV2ApiWorkspace> CreateSurfaceApiWorkspaces()
    {
        return Workspaces.Select((workspace, workspaceIndex) =>
            new SurfaceV2ApiWorkspace(
                Workspace: workspace,
                WorkspaceId: workspace.Workspace.Id,
                WorkspaceName: workspace.Name,
                WorkspaceRef: new ShortRef(ShortRefKind.Workspace, workspaceIndex + 1),
                IsCurrent: workspace == SelectedWorkspace,
                Surfaces: workspace.Surfaces
                    .Select((surface, surfaceIndex) =>
                        new SurfaceV2ApiSurface(
                            Surface: surface,
                            SurfaceId: surface.Surface.Id,
                            SurfaceName: surface.Name,
                            SurfaceRef: new ShortRef(ShortRefKind.Surface, surfaceIndex + 1),
                            IsCurrent: surface == workspace.SelectedSurface,
                            Kind: surface.Surface.Kind.ToString()))
                    .ToList()));
    }

    private bool MoveSurfaceForV2Api(WorkspaceViewModel workspace, SurfaceViewModel surface, int targetIndex)
    {
        var sourceIndex = workspace.Surfaces.IndexOf(surface);
        if (sourceIndex < 0)
            return false;

        if (sourceIndex == targetIndex)
            return true;

        var moved = workspace.MoveSurface(surface, targetIndex);
        if (moved)
            WorkspaceOrderChanged?.Invoke();

        return moved;
    }

    private bool ReorderSurfacesForV2Api(WorkspaceViewModel workspace, IReadOnlyList<string> surfaceIds)
    {
        if (surfaceIds.Count != workspace.Surfaces.Count)
            return false;

        var currentOrder = workspace.Surfaces.Select(surface => surface.Surface.Id).ToList();
        if (currentOrder.SequenceEqual(surfaceIds))
            return true;

        for (var targetIndex = 0; targetIndex < surfaceIds.Count; targetIndex++)
        {
            var surface = workspace.Surfaces.FirstOrDefault(item => item.Surface.Id == surfaceIds[targetIndex]);
            if (surface == null)
                return false;

            workspace.MoveSurface(surface, targetIndex);
        }

        WorkspaceOrderChanged?.Invoke();
        return true;
    }

    private void SelectSurfaceForV2Api(WorkspaceViewModel workspace, SurfaceViewModel surface)
    {
        SelectedWorkspace = workspace;
        workspace.SelectedSurface = surface;
    }

    private string HandleSplit(SplitDirection direction)
    {
        SelectedWorkspace?.SelectedSurface?.SplitFocused(direction);
        return JsonSerializer.Serialize(new { ok = true });
    }

    private string HandlePaneList(Dictionary<string, string> args)
    {
        if (!TryResolveWorkspace(args, out var workspace, out var error))
            return JsonSerializer.Serialize(new { error });

        if (!TryResolveSurface(workspace, args, out var surface, out error))
            return JsonSerializer.Serialize(new { error });

        var leaves = surface.RootNode.GetLeaves()
            .Select(l => l.PaneId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToList();

        var panes = leaves
            .Select((paneId, idx) =>
            {
                surface.Surface.PaneCustomNames.TryGetValue(paneId, out var customName);
                return new
                {
                    index = idx + 1,
                    id = paneId,
                    name = string.IsNullOrWhiteSpace(customName) ? $"Pane {idx + 1}" : customName,
                    customName = customName ?? "",
                    focused = string.Equals(surface.FocusedPaneId, paneId, StringComparison.Ordinal),
                    workingDirectory = surface.GetSession(paneId)?.WorkingDirectory ?? "",
                };
            })
            .ToList();

        return JsonSerializer.Serialize(new
        {
            workspace = new
            {
                id = workspace.Workspace.Id,
                name = workspace.Name,
            },
            surface = new
            {
                id = surface.Surface.Id,
                name = surface.Name,
            },
            panes,
        });
    }

    private string HandlePaneFocus(Dictionary<string, string> args)
    {
        if (!TryResolveWorkspace(args, out var workspace, out var error))
            return JsonSerializer.Serialize(new { error });

        if (!TryResolveSurface(workspace, args, out var surface, out error))
            return JsonSerializer.Serialize(new { error });

        if (!TryResolvePaneId(surface, args, out var paneId, out var paneIndex, out var paneName, out error))
            return JsonSerializer.Serialize(new { error });

        SelectedWorkspace = workspace;
        workspace.SelectedSurface = surface;
        surface.FocusPane(paneId);

        return JsonSerializer.Serialize(new
        {
            ok = true,
            workspaceId = workspace.Workspace.Id,
            workspaceName = workspace.Name,
            surfaceId = surface.Surface.Id,
            surfaceName = surface.Name,
            paneId,
            paneIndex,
            paneName,
        });
    }

    private string HandlePaneWrite(Dictionary<string, string> args)
    {
        var text = args.TryGetValue("text", out var requestedText) ? (requestedText ?? "") : "";

        if (!TryResolveWorkspace(args, out var workspace, out var error))
            return JsonSerializer.Serialize(new { error });

        if (!TryResolveSurface(workspace, args, out var surface, out error))
            return JsonSerializer.Serialize(new { error });

        if (!TryResolvePaneId(surface, args, out var paneId, out var paneIndex, out var paneName, out error))
            return JsonSerializer.Serialize(new { error });

        var session = surface.GetSession(paneId);
        if (session == null)
            return JsonSerializer.Serialize(new { error = $"Pane session not found: {paneId}" });

        bool submit = args.TryGetValue("submit", out var submitRaw)
            && bool.TryParse(submitRaw, out var parsedSubmit)
            && parsedSubmit;

        if (submit)
            text = text.TrimEnd('\r', '\n');

        if (!submit && string.IsNullOrWhiteSpace(text))
            return JsonSerializer.Serialize(new { error = "Missing required argument: text" });

        var submitKey = args.TryGetValue("submitKey", out var submitKeyRaw)
            ? submitKeyRaw ?? ""
            : "auto";

        if (!string.IsNullOrEmpty(text))
            session.Write(text);

        if (submit)
        {
            var submitSequence = ResolveSubmitSequence(submitKey);
            if (!string.IsNullOrEmpty(submitSequence))
                session.Write(submitSequence);

            if (!string.IsNullOrWhiteSpace(text))
                surface.RegisterCommandSubmission(paneId, text);
        }

        return JsonSerializer.Serialize(new
        {
            ok = true,
            workspaceId = workspace.Workspace.Id,
            workspaceName = workspace.Name,
            surfaceId = surface.Surface.Id,
            surfaceName = surface.Name,
            paneId,
            paneIndex,
            paneName,
            submit,
            submitKey,
            bytes = text.Length,
        });
    }

    private string HandlePaneRead(Dictionary<string, string> args)
    {
        if (!TryResolveWorkspace(args, out var workspace, out var error))
            return JsonSerializer.Serialize(new { error });

        if (!TryResolveSurface(workspace, args, out var surface, out error))
            return JsonSerializer.Serialize(new { error });

        if (!TryResolvePaneId(surface, args, out var paneId, out var paneIndex, out var paneName, out error))
            return JsonSerializer.Serialize(new { error });

        var session = surface.GetSession(paneId);
        if (session == null)
            return JsonSerializer.Serialize(new { error = $"Pane session not found: {paneId}" });

        int lines = 80;
        if (args.TryGetValue("lines", out var linesRaw) && int.TryParse(linesRaw, out var parsedLines))
            lines = Math.Clamp(parsedLines, 1, 5000);

        int maxChars = 20000;
        if (args.TryGetValue("maxChars", out var charsRaw) && int.TryParse(charsRaw, out var parsedChars))
            maxChars = Math.Clamp(parsedChars, 512, 200000);

        var allText = session.Buffer.ExportPlainText(maxScrollbackLines: 20000);
        var tailText = TailLines(allText, lines);
        if (tailText.Length > maxChars)
            tailText = "..." + tailText[^maxChars..];

        return JsonSerializer.Serialize(new
        {
            ok = true,
            workspaceId = workspace.Workspace.Id,
            workspaceName = workspace.Name,
            surfaceId = surface.Surface.Id,
            surfaceName = surface.Name,
            paneId,
            paneIndex,
            paneName,
            lines,
            maxChars,
            text = tailText,
        });
    }

    private string HandleStatus()
    {
        // 版本号以主程序集为主（由 Directory.Build.props <Version> 生成），避免硬编码。
        var version = VersionService.GetInformationalVersion(typeof(App).Assembly);
        return JsonSerializer.Serialize(new
        {
            version,
            workspaces = Workspaces.Count,
            selectedWorkspace = SelectedWorkspace?.Workspace.Id,
            unreadNotifications = TotalUnreadCount,
        });
    }

    private static object CreatePaneResponse(SurfaceViewModel surface, string paneId, int paneIndex, string paneName)
    {
        return new
        {
            id = paneId,
            index = paneIndex,
            name = paneName,
            workingDirectory = surface.GetSession(paneId)?.WorkingDirectory ?? "",
        };
    }

    private static string? GetArg(Dictionary<string, string> args, params string[] names)
    {
        foreach (var name in names)
        {
            if (args.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string? GetBrowserUrl(Dictionary<string, string> args)
    {
        return BrowserPaneViewModel.NormalizeUrl(GetArg(args, "url", "href", "_arg0") ?? "");
    }

    private static bool HasSurfaceSelector(Dictionary<string, string> args)
    {
        return args.ContainsKey("surfaceId") ||
               args.ContainsKey("surfaceName") ||
               args.ContainsKey("surfaceIndex");
    }

    private static string GetJoinedPositionals(Dictionary<string, string> args)
    {
        var values = args
            .Select(kvp => new
            {
                kvp.Value,
                Index = TryParsePositionalIndex(kvp.Key, out var index) ? index : -1,
            })
            .Where(x => x.Index >= 0)
            .OrderBy(x => x.Index)
            .Select(x => x.Value);

        return string.Join(" ", values);
    }

    private static bool TryParsePositionalIndex(string key, out int index)
    {
        index = -1;
        return key.StartsWith("_arg", StringComparison.Ordinal) &&
               int.TryParse(key[4..], out index);
    }

    private static bool IsTruthy(Dictionary<string, string> args, string key)
    {
        return TryGetBool(args, key, out var value) && value;
    }

    private static bool TryGetBool(Dictionary<string, string> args, string key, out bool value)
    {
        value = false;
        if (!args.TryGetValue(key, out var raw))
            return false;

        if (bool.TryParse(raw, out value))
            return true;

        if (raw == "1")
        {
            value = true;
            return true;
        }

        if (raw == "0")
        {
            value = false;
            return true;
        }

        return false;
    }

    private bool TryResolveWorkspace(Dictionary<string, string> args, out WorkspaceViewModel workspace, out string error)
    {
        workspace = null!;
        error = "";

        var defaultWorkspace = SelectedWorkspace ?? Workspaces.FirstOrDefault();
        if (defaultWorkspace == null)
        {
            error = "No project available.";
            return false;
        }

        workspace = defaultWorkspace;

        if (args.TryGetValue("workspaceId", out var workspaceId) && !string.IsNullOrWhiteSpace(workspaceId))
        {
            var byId = Workspaces.FirstOrDefault(w => string.Equals(w.Workspace.Id, workspaceId, StringComparison.Ordinal));
            if (byId == null)
            {
                error = $"Project id not found: {workspaceId}";
                return false;
            }

            workspace = byId;
            return true;
        }

        if (args.TryGetValue("workspaceName", out var workspaceName) && !string.IsNullOrWhiteSpace(workspaceName))
        {
            var byName = Workspaces.FirstOrDefault(w => string.Equals(w.Name, workspaceName, StringComparison.OrdinalIgnoreCase))
                ?? Workspaces.FirstOrDefault(w => w.Name.Contains(workspaceName, StringComparison.OrdinalIgnoreCase));
            if (byName == null)
            {
                error = $"Project name not found: {workspaceName}";
                return false;
            }

            workspace = byName;
            return true;
        }

        if (args.TryGetValue("workspaceIndex", out var workspaceIndexRaw)
            && int.TryParse(workspaceIndexRaw, out var workspaceIndex))
        {
            if (!TryResolveCollectionIndex(workspaceIndex, Workspaces.Count, out var resolvedIndex))
            {
                error = $"Project index out of range: {workspaceIndex}";
                return false;
            }

            workspace = Workspaces[resolvedIndex];
            return true;
        }

        return true;
    }

    private static bool TryResolveSurface(WorkspaceViewModel workspace, Dictionary<string, string> args, out SurfaceViewModel surface, out string error)
    {
        surface = null!;
        error = "";

        var defaultSurface = workspace.SelectedSurface ?? workspace.Surfaces.FirstOrDefault();
        if (defaultSurface == null)
        {
            error = "No surface available in project.";
            return false;
        }

        surface = defaultSurface;

        if (args.TryGetValue("surfaceId", out var surfaceId) && !string.IsNullOrWhiteSpace(surfaceId))
        {
            var byId = workspace.Surfaces.FirstOrDefault(s => string.Equals(s.Surface.Id, surfaceId, StringComparison.Ordinal));
            if (byId == null)
            {
                error = $"Surface id not found: {surfaceId}";
                return false;
            }

            surface = byId;
            return true;
        }

        if (args.TryGetValue("surfaceName", out var surfaceName) && !string.IsNullOrWhiteSpace(surfaceName))
        {
            var byName = workspace.Surfaces.FirstOrDefault(s => string.Equals(s.Name, surfaceName, StringComparison.OrdinalIgnoreCase))
                ?? workspace.Surfaces.FirstOrDefault(s => s.Name.Contains(surfaceName, StringComparison.OrdinalIgnoreCase));
            if (byName == null)
            {
                error = $"Surface name not found: {surfaceName}";
                return false;
            }

            surface = byName;
            return true;
        }

        if (args.TryGetValue("surfaceIndex", out var surfaceIndexRaw)
            && int.TryParse(surfaceIndexRaw, out var surfaceIndex))
        {
            if (!TryResolveCollectionIndex(surfaceIndex, workspace.Surfaces.Count, out var resolvedIndex))
            {
                error = $"Surface index out of range: {surfaceIndex}";
                return false;
            }

            surface = workspace.Surfaces[resolvedIndex];
            return true;
        }

        return true;
    }

    private static bool TryResolvePaneId(
        SurfaceViewModel surface,
        Dictionary<string, string> args,
        out string paneId,
        out int paneIndex,
        out string paneName,
        out string error)
    {
        paneId = "";
        paneIndex = -1;
        paneName = "";
        error = "";

        var panes = surface.RootNode.GetLeaves()
            .Select(l => l.PaneId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Select((id, idx) =>
            {
                surface.Surface.PaneCustomNames.TryGetValue(id, out var customName);
                return new
                {
                    Id = id,
                    Index = idx + 1,
                    Name = string.IsNullOrWhiteSpace(customName) ? $"Pane {idx + 1}" : customName!,
                    CustomName = customName ?? "",
                };
            })
            .ToList();

        if (panes.Count == 0)
        {
            error = "No panes available in surface.";
            return false;
        }

        string? target = null;

        if (args.TryGetValue("paneId", out var requestedPaneId) && !string.IsNullOrWhiteSpace(requestedPaneId))
        {
            target = panes.FirstOrDefault(p => string.Equals(p.Id, requestedPaneId, StringComparison.Ordinal))?.Id;
            if (target == null)
            {
                error = $"Pane id not found: {requestedPaneId}";
                return false;
            }
        }
        else if (args.TryGetValue("paneName", out var requestedPaneName) && !string.IsNullOrWhiteSpace(requestedPaneName))
        {
            target = panes.FirstOrDefault(p => string.Equals(p.CustomName, requestedPaneName, StringComparison.OrdinalIgnoreCase))?.Id
                ?? panes.FirstOrDefault(p => string.Equals(p.Name, requestedPaneName, StringComparison.OrdinalIgnoreCase))?.Id;
            if (target == null)
            {
                error = $"Pane name not found: {requestedPaneName}";
                return false;
            }
        }
        else if (args.TryGetValue("paneIndex", out var paneIndexRaw) && int.TryParse(paneIndexRaw, out var requestedIndex))
        {
            if (!TryResolveCollectionIndex(requestedIndex, panes.Count, out var resolvedIndex))
            {
                error = $"Pane index out of range: {requestedIndex}";
                return false;
            }

            target = panes[resolvedIndex].Id;
        }
        else
        {
            target = !string.IsNullOrWhiteSpace(surface.FocusedPaneId)
                ? panes.FirstOrDefault(p => string.Equals(p.Id, surface.FocusedPaneId, StringComparison.Ordinal))?.Id
                : null;

            target ??= panes[0].Id;
        }

        var pane = panes.First(p => string.Equals(p.Id, target, StringComparison.Ordinal));
        paneId = pane.Id;
        paneIndex = pane.Index;
        paneName = pane.Name;
        return true;
    }

    private static bool TryResolveCollectionIndex(int requested, int count, out int zeroBasedIndex)
    {
        zeroBasedIndex = -1;
        if (count <= 0)
            return false;

        if (requested >= 1 && requested <= count)
        {
            zeroBasedIndex = requested - 1;
            return true;
        }

        if (requested >= 0 && requested < count)
        {
            zeroBasedIndex = requested;
            return true;
        }

        return false;
    }

    private static string TailLines(string? text, int lines)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var split = text.Replace("\r", "", StringComparison.Ordinal).Split('\n');
        var tail = split.TakeLast(Math.Max(1, lines));
        return string.Join("\n", tail).TrimEnd();
    }

    private static string ResolveSubmitSequence(string? submitKey)
    {
        var key = (submitKey ?? "auto").Trim().ToLowerInvariant();

        if (key is "auto" or "")
            return "\r";

        return key switch
        {
            "enter" or "cr" or "ctrl+m" => "\r",
            "linefeed" or "lf" or "ctrl+j" => "\n",
            "crlf" => "\r\n",
            "none" => "",
            _ => "\r",
        };
    }
}
