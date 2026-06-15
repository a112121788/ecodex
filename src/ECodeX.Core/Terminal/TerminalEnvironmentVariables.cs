namespace ECodeX.Core.Terminal;

/// <summary>
/// Builds the environment passed to shell processes started by ecodex.
/// </summary>
public static class TerminalEnvironmentVariables
{
    public const string WorkspaceId = "ECODEX_WORKSPACE_ID";

    public static Dictionary<string, string> ForWorkspace(string? workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
            return [];

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [WorkspaceId] = workspaceId,
        };
    }

    public static SortedDictionary<string, string> MergeWithCurrent(IReadOnlyDictionary<string, string>? overrides)
    {
        var merged = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key?.ToString();
            if (IsValidName(key))
                merged[key!] = entry.Value?.ToString() ?? "";
        }

        if (overrides != null)
        {
            foreach (var (key, value) in overrides)
            {
                if (IsValidName(key))
                    merged[key] = value;
            }
        }

        return merged;
    }

    private static bool IsValidName(string? name)
    {
        return !string.IsNullOrWhiteSpace(name) &&
               !name.Contains('=', StringComparison.Ordinal);
    }
}
