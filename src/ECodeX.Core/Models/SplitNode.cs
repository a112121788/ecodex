namespace ECodeX.Core.Models;

public enum SplitDirection
{
    Horizontal,
    Vertical,
}

public class SplitNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public bool IsLeaf { get; set; } = true;
    public SplitDirection Direction { get; set; } = SplitDirection.Vertical;
    public SplitNode? First { get; set; }
    public SplitNode? Second { get; set; }
    public double SplitRatio { get; set; } = 0.5;
    public string? PaneId { get; set; }

    public static SplitNode CreateLeaf(string? paneId = null)
    {
        return new SplitNode
        {
            IsLeaf = true,
            PaneId = paneId ?? Guid.NewGuid().ToString(),
        };
    }

    /// <summary>
    /// 将此叶子节点拆分为包含两个子节点的容器。
    /// 返回新的第二个子节点（新创建的面板）。
    /// </summary>
    public SplitNode Split(SplitDirection direction)
    {
        if (!IsLeaf)
            throw new InvalidOperationException("无法拆分非叶子节点。");

        var firstChild = new SplitNode
        {
            IsLeaf = true,
            PaneId = PaneId,
        };

        var secondChild = CreateLeaf();

        IsLeaf = false;
        Direction = direction;
        First = firstChild;
        Second = secondChild;
        PaneId = null;
        SplitRatio = 0.5;

        return secondChild;
    }

    /// <summary>
    /// 递归查找包含指定面板 ID 的节点。
    /// </summary>
    public SplitNode? FindNode(string paneId)
    {
        if (IsLeaf)
            return PaneId == paneId ? this : null;

        return First?.FindNode(paneId) ?? Second?.FindNode(paneId);
    }

    /// <summary>
    /// 查找具有指定 ID 的节点的父节点。
    /// </summary>
    public SplitNode? FindParent(string nodeId)
    {
        if (IsLeaf) return null;

        if (First?.Id == nodeId || Second?.Id == nodeId)
            return this;

        return First?.FindParent(nodeId) ?? Second?.FindParent(nodeId);
    }

    /// <summary>
    /// 按遍历顺序返回所有叶子节点（终端面板）。
    /// </summary>
    public IEnumerable<SplitNode> GetLeaves()
    {
        if (IsLeaf)
        {
            yield return this;
            yield break;
        }

        if (First != null)
        {
            foreach (var leaf in First.GetLeaves())
                yield return leaf;
        }

        if (Second != null)
        {
            foreach (var leaf in Second.GetLeaves())
                yield return leaf;
        }
    }

    /// <summary>
    /// 移除具有指定面板 ID 的叶子节点。
    /// 幸存的兄弟节点会替换父容器。
    /// 移除成功返回 true。
    /// </summary>
    public bool Remove(string paneId)
    {
        if (IsLeaf) return false;

        // 检查直接子节点之一是否为目标
        SplitNode? target = null;
        SplitNode? survivor = null;

        if (First is { IsLeaf: true, PaneId: not null } && First.PaneId == paneId)
        {
            target = First;
            survivor = Second;
        }
        else if (Second is { IsLeaf: true, PaneId: not null } && Second.PaneId == paneId)
        {
            target = Second;
            survivor = First;
        }

        if (target != null && survivor != null)
        {
            // 用幸存者的内容替换此节点的内容
            IsLeaf = survivor.IsLeaf;
            Direction = survivor.Direction;
            First = survivor.First;
            Second = survivor.Second;
            SplitRatio = survivor.SplitRatio;
            PaneId = survivor.PaneId;
            return true;
        }

        // 递归到子节点
        if (First?.Remove(paneId) == true) return true;
        if (Second?.Remove(paneId) == true) return true;

        return false;
    }

    /// <summary>
    /// 获取指定面板 ID 之后的下一个叶子节点（用于焦点导航）。
    /// </summary>
    public SplitNode? GetNextLeaf(string paneId)
    {
        var leaves = GetLeaves().ToList();
        for (int i = 0; i < leaves.Count; i++)
        {
            if (leaves[i].PaneId == paneId && i + 1 < leaves.Count)
                return leaves[i + 1];
        }
        return leaves.Count > 0 ? leaves[0] : null;
    }

    /// <summary>
    /// 获取指定面板 ID 之前的上一个叶子节点。
    /// </summary>
    public SplitNode? GetPreviousLeaf(string paneId)
    {
        var leaves = GetLeaves().ToList();
        for (int i = 0; i < leaves.Count; i++)
        {
            if (leaves[i].PaneId == paneId && i - 1 >= 0)
                return leaves[i - 1];
        }
        return leaves.Count > 0 ? leaves[^1] : null;
    }

    /// <summary>创建具有指定数量等宽垂直列的布局。</summary>
    public static SplitNode CreateColumns(int count)
    {
        if (count <= 1) return CreateLeaf();
        var node = CreateLeaf();
        for (int i = 1; i < count; i++)
            node = new SplitNode
            {
                IsLeaf = false,
                Direction = SplitDirection.Vertical,
                SplitRatio = (double)i / (i + 1),
                First = node,
                Second = CreateLeaf(),
            };
        return node;
    }

    /// <summary>创建具有指定数量等高水平行的布局。</summary>
    public static SplitNode CreateRows(int count)
    {
        if (count <= 1) return CreateLeaf();
        var node = CreateLeaf();
        for (int i = 1; i < count; i++)
            node = new SplitNode
            {
                IsLeaf = false,
                Direction = SplitDirection.Horizontal,
                SplitRatio = (double)i / (i + 1),
                First = node,
                Second = CreateLeaf(),
            };
        return node;
    }

    /// <summary>创建 2x2 网格布局。</summary>
    public static SplitNode CreateGrid()
    {
        return new SplitNode
        {
            IsLeaf = false,
            Direction = SplitDirection.Horizontal,
            SplitRatio = 0.5,
            First = new SplitNode
            {
                IsLeaf = false,
                Direction = SplitDirection.Vertical,
                SplitRatio = 0.5,
                First = CreateLeaf(),
                Second = CreateLeaf(),
            },
            Second = new SplitNode
            {
                IsLeaf = false,
                Direction = SplitDirection.Vertical,
                SplitRatio = 0.5,
                First = CreateLeaf(),
                Second = CreateLeaf(),
            },
        };
    }

    /// <summary>创建主+堆叠布局（左侧大面板，右侧堆叠面板）。</summary>
    public static SplitNode CreateMainStack(int stackCount = 2)
    {
        var stack = CreateLeaf();
        for (int i = 1; i < stackCount; i++)
            stack = new SplitNode
            {
                IsLeaf = false,
                Direction = SplitDirection.Horizontal,
                SplitRatio = (double)i / (i + 1),
                First = stack,
                Second = CreateLeaf(),
            };
        return new SplitNode
        {
            IsLeaf = false,
            Direction = SplitDirection.Vertical,
            SplitRatio = 0.6,
            First = CreateLeaf(),
            Second = stack,
        };
    }

    /// <summary>递归地将所有分割比例设置为 0.5（使面板大小相等）。</summary>
    public void Equalize()
    {
        if (IsLeaf) return;
        SplitRatio = 0.5;
        First?.Equalize();
        Second?.Equalize();
    }

    /// <summary>调整包含指定面板的最近祖先节点的分割比例。</summary>
    public bool ResizePane(string paneId, double delta)
    {
        if (IsLeaf) return false;
        bool inFirst = First?.FindNode(paneId) != null;
        bool inSecond = Second?.FindNode(paneId) != null;
        if (!inFirst && !inSecond) return false;

        // 直接子节点是目标
        if ((First?.IsLeaf == true && First.PaneId == paneId) ||
            (Second?.IsLeaf == true && Second.PaneId == paneId))
        {
            SplitRatio = Math.Clamp(SplitRatio + (inFirst ? delta : -delta), 0.1, 0.9);
            return true;
        }
        // 递归到包含该面板的子树
        if (inFirst && First!.ResizePane(paneId, delta)) return true;
        if (inSecond && Second!.ResizePane(paneId, delta)) return true;
        // 面板嵌套更深 — 在此层调整
        SplitRatio = Math.Clamp(SplitRatio + (inFirst ? delta : -delta), 0.1, 0.9);
        return true;
    }

    /// <summary>通过交换两个面板的 PaneId 来交换它们。</summary>
    public bool SwapPanes(string paneId1, string paneId2)
    {
        var node1 = FindNode(paneId1);
        var node2 = FindNode(paneId2);
        if (node1 == null || node2 == null || !node1.IsLeaf || !node2.IsLeaf) return false;
        (node1.PaneId, node2.PaneId) = (node2.PaneId, node1.PaneId);
        return true;
    }
}
