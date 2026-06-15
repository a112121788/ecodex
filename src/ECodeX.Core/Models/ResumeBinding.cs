using System.Text.Json.Serialization;

namespace ECodeX.Core.Models;

/// <summary>
/// resume.json 根对象，用于保存 pane 与可恢复会话命令之间的绑定关系。
/// </summary>
public sealed class ResumeBindingFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("bindings")]
    public List<ResumeBinding> Bindings { get; set; } = [];
}

/// <summary>
/// 单个 pane 的恢复绑定。是否自动执行由后续 ResumeBindingService 信任策略决定。
/// </summary>
public sealed class ResumeBinding
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("workspaceId")]
    public string WorkspaceId { get; set; } = "";

    [JsonPropertyName("surfaceId")]
    public string SurfaceId { get; set; } = "";

    [JsonPropertyName("paneId")]
    public string PaneId { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = ResumeBindingKinds.Custom;

    [JsonPropertyName("checkpoint")]
    public string? Checkpoint { get; set; }

    [JsonPropertyName("shell")]
    public string Shell { get; set; } = "";

    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; set; }

    [JsonPropertyName("environment")]
    public Dictionary<string, string> Environment { get; set; } = [];

    [JsonPropertyName("trusted")]
    public bool Trusted { get; set; }

    [JsonPropertyName("trustReason")]
    public string? TrustReason { get; set; }

    [JsonPropertyName("approvedPrefix")]
    public string? ApprovedPrefix { get; set; }

    [JsonPropertyName("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class ResumeBindingKinds
{
    public const string Tmux = "tmux";
    public const string Custom = "custom";
}

