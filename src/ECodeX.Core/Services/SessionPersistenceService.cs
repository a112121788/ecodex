using System.Text.Json;
using ECodeX.Core.Config;
using ECodeX.Core.Models;

namespace ECodeX.Core.Services;

/// <summary>
/// 保存并恢复应用程序会话状态（窗口布局、项目、Surface、分屏布局、工作目录）
/// 到 JSON 文件，或从 JSON 文件恢复。
/// </summary>
public class SessionPersistenceService
{
    private static string StateDir => CompatibilityOptions.GetAppDataDir();
    private static string StatePath => CompatibilityOptions.GetSessionStatePath();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static SessionState? Load()
    {
        try
        {
            if (!File.Exists(StatePath)) return null;
            var json = File.ReadAllText(StatePath);
            return JsonSerializer.Deserialize<SessionState>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(SessionState state)
    {
        try
        {
            Directory.CreateDirectory(StateDir);
            var json = JsonSerializer.Serialize(state, JsonOptions);
            // 先写入临时文件再重命名，以保证原子性
            var tempPath = StatePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, StatePath, overwrite: true);
        }
        catch
        {
            // 尽力而为 —— 保存失败时不要让程序崩溃
        }
    }

    public static SessionState BuildState(
        IReadOnlyList<Workspace> workspaces,
        int? selectedWorkspaceIndex,
        double windowX, double windowY, double windowWidth, double windowHeight,
        bool isMaximized, double sidebarWidth, bool sidebarVisible, bool compactSidebar)
    {
        var state = new SessionState
        {
            Version = 1,
            SelectedWorkspaceIndex = selectedWorkspaceIndex,
            Window = new WindowState
            {
                X = windowX,
                Y = windowY,
                Width = windowWidth,
                Height = windowHeight,
                IsMaximized = isMaximized,
                SidebarWidth = sidebarWidth,
                SidebarVisible = sidebarVisible,
                CompactSidebar = compactSidebar,
            },
        };

        foreach (var ws in workspaces)
        {
            var wsState = new WorkspaceState
            {
                Id = ws.Id,
                Name = ws.Name,
                IconGlyph = ws.IconGlyph,
                AccentColor = ws.AccentColor,
                WorkingDirectory = ws.WorkingDirectory,
                SelectedSurfaceIndex = ws.Surfaces.IndexOf(ws.SelectedSurface!),
            };

            foreach (var surface in ws.Surfaces)
            {
                var surfState = new SurfaceState
                {
                    Id = surface.Id,
                    Name = surface.Name,
                    Kind = surface.Kind,
                    BrowserUrl = surface.BrowserUrl,
                    BrowserTitle = surface.BrowserTitle,
                    BrowserHistory = surface.BrowserHistory.ToList(),
                    FocusedPaneId = surface.FocusedPaneId,
                    PaneCustomNames = new Dictionary<string, string>(surface.PaneCustomNames),
                    PaneSnapshots = surface.PaneSnapshots.ToDictionary(
                        kvp => kvp.Key,
                        kvp => ClonePaneSnapshot(kvp.Value)),
                    RootNode = SerializeSplitNode(surface.RootSplitNode),
                };
                wsState.Surfaces.Add(surfState);
            }

            state.Workspaces.Add(wsState);
        }

        return state;
    }

    private static PaneStateSnapshot ClonePaneSnapshot(PaneStateSnapshot source)
    {
        return new PaneStateSnapshot
        {
            CapturedAt = source.CapturedAt,
            WorkingDirectory = source.WorkingDirectory,
            Shell = source.Shell,
            CommandHistory = source.CommandHistory.ToList(),
            BufferSnapshot = source.BufferSnapshot == null
                ? null
                : new ECodeX.Core.Terminal.TerminalBufferSnapshot
                {
                    Cols = source.BufferSnapshot.Cols,
                    Rows = source.BufferSnapshot.Rows,
                    CursorRow = source.BufferSnapshot.CursorRow,
                    CursorCol = source.BufferSnapshot.CursorCol,
                    ScrollbackLines = source.BufferSnapshot.ScrollbackLines.ToList(),
                    ScreenLines = source.BufferSnapshot.ScreenLines.ToList(),
                },
        };
    }

    private static SplitNodeState SerializeSplitNode(SplitNode node)
    {
        return new SplitNodeState
        {
            IsLeaf = node.IsLeaf,
            Direction = node.Direction.ToString(),
            SplitRatio = node.SplitRatio,
            PaneId = node.PaneId,
            First = node.First != null ? SerializeSplitNode(node.First) : null,
            Second = node.Second != null ? SerializeSplitNode(node.Second) : null,
        };
    }

    public static SplitNode DeserializeSplitNode(SplitNodeState state)
    {
        var node = new SplitNode
        {
            IsLeaf = state.IsLeaf,
            Direction = Enum.TryParse<SplitDirection>(state.Direction, out var dir) ? dir : SplitDirection.Vertical,
            SplitRatio = state.SplitRatio,
            PaneId = state.PaneId,
            First = state.First != null ? DeserializeSplitNode(state.First) : null,
            Second = state.Second != null ? DeserializeSplitNode(state.Second) : null,
        };
        return node;
    }
}
