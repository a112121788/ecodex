namespace ECodeX.Core.Models;

public class CommandLogEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string PaneId { get; init; } = "";
    public string SurfaceId { get; init; } = "";
    public string WorkspaceId { get; init; } = "";
    public string? Command { get; set; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public int? ExitCode { get; set; }
    public string? WorkingDirectory { get; set; }

    public TimeSpan? Duration => CompletedAt.HasValue ? CompletedAt.Value - StartedAt : null;

    public string DurationDisplay
    {
        get
        {
            var d = Duration;
            if (d is null)
                return "运行中...";

            var ts = d.Value;
            if (ts.TotalSeconds < 1)
                return $"{(int)ts.TotalMilliseconds}ms";
            if (ts.TotalMinutes < 1)
                return $"{ts.TotalSeconds:F1}s";
            if (ts.TotalHours < 1)
                return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        }
    }

    /// <summary>
    /// 命令状态的 Segoe MDL2 Assets 字形。
    /// 退出码为 null = 时钟，0 = 勾选，其他 = 错误。
    /// </summary>
    public string StatusIcon => ExitCode switch
    {
        null => "\uE916",
        0 => "\uE73E",
        _ => "\uE711",
    };
}
