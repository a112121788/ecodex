using ECode.Core.Terminal;

namespace ECode.Core.Models;

/// <summary>
/// 终端窗格的状态快照，包含工作目录、Shell 类型、命令历史和缓冲区内容。
/// </summary>
public class PaneStateSnapshot
{
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public string? WorkingDirectory { get; set; }
    public string? Shell { get; set; }
    public List<string> CommandHistory { get; set; } = [];
    public TerminalBufferSnapshot? BufferSnapshot { get; set; }
}
