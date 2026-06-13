namespace ECode.Core.Models;

/// <summary>
/// 终端会话转录文件的元数据条目，记录捕获时间、所属窗格及文件路径信息。
/// </summary>
public class TerminalTranscriptEntry
{
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public DateTime CapturedAt { get; init; }
    public string WorkspaceId { get; init; } = string.Empty;
    public string SurfaceId { get; init; } = string.Empty;
    public string PaneId { get; init; } = string.Empty;
    public string? WorkingDirectory { get; init; }
    public string Reason { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
}
