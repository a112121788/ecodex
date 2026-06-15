namespace ECodeX.Core.Services;

/// <summary>
/// 将拖拽/剪贴板路径转换为可直接粘贴到终端的参数文本。
/// </summary>
public static class ShellPathQuoter
{
    private static readonly char[] QuoteRequiredChars = [' ', '\t', '\n', '\r', '"'];

    public static string QuotePathForShell(string path)
    {
        if (path.IndexOfAny(QuoteRequiredChars) < 0)
            return path;

        return "\"" + path.Replace("\"", "\\\"") + "\"";
    }

    public static string JoinQuotedPaths(IEnumerable<string> paths)
    {
        return string.Join(" ", paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(QuotePathForShell));
    }
}
