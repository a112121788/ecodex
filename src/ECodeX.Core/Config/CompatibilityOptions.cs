namespace ECodeX.Core.Config;

/// <summary>
/// 集中读取“当前数据目录 / 配置目录 / 命名管道名”的辅助类。
/// 运行时数据固定写入用户主目录下的 <c>%USERPROFILE%\.ecodex</c>。
/// </summary>
public static class CompatibilityOptions
{
    public const string NewAppFolder = ".ecodex";
    public const string GlobalConfigFolder = "ecodex";
    public const string LegacyAppFolder = "cmux";
    public const string NewConfigFileName = "ecodex.json";
    public const string LegacyConfigFileName = "cmux.json";
    public const string NewConfigDir = ".ecodex";
    public const string LegacyConfigDir = ".cmux";
    public const string NewMutexName = @"Global\ECodeXDaemon";
    public const string LegacyMutexName = @"Global\CmuxDaemon";
    public const string NewMainPipe = "ecodex";
    public const string LegacyMainPipe = "cmux";
    public const string NewDaemonPipe = "ecodex-daemon";
    public const string LegacyDaemonPipe = "cmux-daemon";

    private static string UserProfileDir => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// 当前 ECodeX 的数据根目录（无尾随分隔符）。
    /// </summary>
    public static string GetAppDataDir()
    {
        return Path.Combine(UserProfileDir, NewAppFolder);
    }

    /// <summary>
    /// 决定要读取的 session.json 实际路径。
    /// </summary>
    public static string GetSessionStatePath() => Path.Combine(GetAppDataDir(), "session.json");

    /// <summary>
    /// 决定要读取的 settings.json 实际路径。
    /// </summary>
    public static string GetSettingsPath() => Path.Combine(GetAppDataDir(), "settings.json");

    /// <summary>
    /// 决定要读取的 snippets.json 实际路径。
    /// </summary>
    public static string GetSnippetsPath() => Path.Combine(GetAppDataDir(), "snippets.json");

    /// <summary>
    /// 决定要读取的命令日志目录。
    /// </summary>
    public static string GetCommandLogsDir() => Path.Combine(GetAppDataDir(), "logs");


    /// <summary>
    /// 在工作目录下寻找 `ecodex.json` / `cmux.json` 的回退候选路径。
    /// 返回值顺序：新路径 → 旧路径。
    /// </summary>
    public static IEnumerable<string> EnumerateConfigFileCandidates(string workingDirectory)
    {
        // 新路径优先
        yield return Path.Combine(workingDirectory, NewConfigDir, NewConfigFileName);
        yield return Path.Combine(workingDirectory, NewConfigFileName);
        // 全局配置仍沿用 ~/.config/ecodex/ecodex.json，避免和 ~/.ecodex 运行时数据混用。
        yield return Path.Combine(UserProfileDir, ".config", GlobalConfigFolder, NewConfigFileName);

        // 旧 cmux.json 路径仅用于配置文件兼容，不涉及运行时数据目录。
        if (ShouldReadLegacyConfigFile())
        {
            yield return Path.Combine(workingDirectory, LegacyConfigDir, LegacyConfigFileName);
            yield return Path.Combine(workingDirectory, LegacyConfigFileName);
            yield return Path.Combine(UserProfileDir, ".config", LegacyAppFolder, LegacyConfigFileName);
        }
    }

    /// <summary>
    /// 旧命名管道（`\\.\pipe\cmux`、`\\.\pipe\cmux-{tag}`）是否仍要监听。
    /// </summary>
    public static bool ShouldListenLegacyMainPipe(string? tag)
    {
        return ReadCompatFlag("compatListenLegacyMainPipe", defaultValue: true);
    }

    public static bool ShouldListenLegacyDaemonPipe()
    {
        return ReadCompatFlag("compatListenLegacyDaemonPipe", defaultValue: true);
    }

    /// <summary>
    /// 旧 mutex 名 `Global\CmuxDaemon` 是否应视为“已存在”来让出单实例。
    /// </summary>
    public static bool ShouldHonorLegacyMutex()
    {
        return ReadCompatFlag("compatListenLegacyDaemonPipe", defaultValue: true);
    }

    /// <summary>CLI 顶层是否接受旧命令名 `cmux *`。</summary>
    public static bool ShouldAcceptLegacyCliCommand()
    {
        return ReadCompatFlag("compatAcceptLegacyCliCommand", defaultValue: true);
    }

    private static bool ShouldReadLegacyConfigFile()
    {
        return ReadCompatFlag("compatReadLegacyConfigFile", defaultValue: true);
    }

    private static bool ReadCompatFlag(string propertyName, bool defaultValue)
    {
        try
        {
            var settingsPath = GetSettingsPath();
            if (!File.Exists(settingsPath)) return defaultValue;

            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (doc.RootElement.TryGetProperty(propertyName, out var value)
                && value.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
            {
                return value.GetBoolean();
            }
        }
        catch
        {
            // 损坏的设置文件不应阻断启动。
        }

        return defaultValue;
    }
}
