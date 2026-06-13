using System.Text.RegularExpressions;

namespace ECode.Core.Models;

/// <summary>
/// 代码片段 - 支持 {{key}} 占位符替换的可复用文本模板
/// </summary>
public partial class Snippet
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
    public string Category { get; set; } = "General";
    public List<string> Tags { get; set; } = [];
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int UseCount { get; set; }
    public bool IsFavorite { get; set; }

    /// <summary>
    /// 将 <see cref="Content"/> 中的 <c>{{key}}</c> 占位符替换为字典中的值。
    /// 未匹配到的占位符保持原样。
    /// </summary>
    public string Resolve(Dictionary<string, string>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
            return Content;

        return PlaceholderRegex().Replace(Content, match =>
        {
            var key = match.Groups[1].Value;
            return parameters.TryGetValue(key, out var value) ? value : match.Value;
        });
    }

    /// <summary>
    /// 返回未进行参数替换的原始内容。
    /// </summary>
    public string Resolve() => Content;

    /// <summary>
    /// 从 <see cref="Content"/> 中提取所有不同的占位符名称。
    /// </summary>
    public List<string> GetPlaceholders()
    {
        return PlaceholderRegex()
            .Matches(Content)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex PlaceholderRegex();
}
