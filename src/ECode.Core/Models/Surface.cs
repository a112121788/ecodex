namespace ECode.Core.Models;

/// <summary>
/// Surface (页面) - Workspace 的标签页，包含分屏布局树和多个终端面板
/// </summary>
public class Surface
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Terminal";
    public SplitNode RootSplitNode { get; set; } = SplitNode.CreateLeaf(); // 分屏布局的根节点
    public string? FocusedPaneId { get; set; }
    public Dictionary<string, string> PaneCustomNames { get; set; } = []; // 面板自定义名称映射
    public Dictionary<string, PaneStateSnapshot> PaneSnapshots { get; set; } = []; // 面板状态快照
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
