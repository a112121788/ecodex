namespace ECode.Core.Config;

/// <summary>
/// 集中读取“当前数据目录 / 配置目录 / 命名管道名”的辅助类。
/// 内部按 <see cref="ECodeSettings"/> 的兼容开关选择新目录或旧目录，
/// 并在第一次访问时执行旧目录 → 新目录的单向迁移。
///
/// 旧目录 `%LOCALAPPDATA%\cmux\` 在迁移后保留只读，
/// 关闭 <see cref="ECodeSettings.CompatMigrateLegacyDataDir"/> 仍可读取旧数据。
/// </summary>
public static class CompatibilityOptions
{
    public const string NewAppFolder = "ecode";
    public const string LegacyAppFolder = "cmux";
    public const string NewConfigFileName = "ecode.json";
    public const string LegacyConfigFileName = "cmux.json";
    public const string NewConfigDir = ".ecode";
    public const string LegacyConfigDir = ".cmux";
    public const string NewMutexName = @"Global\ECodeDaemon";
    public const string LegacyMutexName = @"Global\CmuxDaemon";
    public const string NewMainPipe = "ecode";
    public const string LegacyMainPipe = "cmux";
    public const string NewDaemonPipe = "ecode-daemon";
    public const string LegacyDaemonPipe = "cmux-daemon";

    private static bool _migrationChecked;
    private static readonly object _migrationLock = new();

    // 当 SettingsService 还未就绪（避免循环调用）时直接采用默认兼容值（true）。
    private static bool IsCompatFlagOnDuringBoot()
    {
        try
        {
            // 首次访问时仍可能触发 SettingsService.Load()，而 Load 又会调用 GetSettingsPath()，
            // 再回调到此处。为避免死锁，这里仅在迁移检查完成（_migrationChecked=true）后才返回 false，
            // 否则保持默认值 true（旧行为）。
            return !_migrationChecked ? true : SafeReadCompatFlag();
        }
        catch
        {
            return true;
        }
    }

    private static bool SafeReadCompatFlag()
    {
        try { return SettingsService.Current.CompatMigrateLegacyDataDir; }
        catch { return true; }
    }

    /// <summary>
    /// 当前 ECode 的数据根目录（无尾随分隔符）。如果新目录不存在但旧目录存在并启用了迁移，
    /// 会先执行迁移再返回新目录。
    /// </summary>
    public static string GetAppDataDir()
    {
        EnsureMigrated();
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            NewAppFolder);
    }

    /// <summary>
    /// 旧数据根目录，仅在兼容读取场景使用；新写入不要直接调用。
    /// </summary>
    public static string GetLegacyAppDataDir()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LegacyAppFolder);
    }

    /// <summary>
    /// 决定要读取的 session.json 实际路径（优先新路径；新文件不存在且兼容开关打开时回退到旧文件）。
    /// </summary>
    public static string GetSessionStatePath()
    {
        var newPath = Path.Combine(GetAppDataDir(), "session.json");
        if (File.Exists(newPath)) return newPath;
        if (IsCompatFlagOnDuringBoot())
        {
            var legacy = Path.Combine(GetLegacyAppDataDir(), "session.json");
            if (File.Exists(legacy)) return legacy;
        }
        return newPath;
    }

    /// <summary>
    /// 决定要读取的 settings.json 实际路径。
    /// </summary>
    public static string GetSettingsPath()
    {
        var newPath = Path.Combine(GetAppDataDir(), "settings.json");
        if (File.Exists(newPath)) return newPath;
        if (IsCompatFlagOnDuringBoot())
        {
            var legacy = Path.Combine(GetLegacyAppDataDir(), "settings.json");
            if (File.Exists(legacy)) return legacy;
        }
        return newPath;
    }

    /// <summary>
    /// 决定要读取的 snippets.json 实际路径。
    /// </summary>
    public static string GetSnippetsPath()
    {
        var newPath = Path.Combine(GetAppDataDir(), "snippets.json");
        if (File.Exists(newPath)) return newPath;
        if (IsCompatFlagOnDuringBoot())
        {
            var legacy = Path.Combine(GetLegacyAppDataDir(), "snippets.json");
            if (File.Exists(legacy)) return legacy;
        }
        return newPath;
    }

    /// <summary>
    /// 决定要读取的命令日志目录；新目录存在时优先，否则回退。
    /// </summary>
    public static string GetCommandLogsDir()
    {
        var newDir = Path.Combine(GetAppDataDir(), "logs");
        if (Directory.Exists(newDir)) return newDir;
        if (IsCompatFlagOnDuringBoot())
        {
            var legacy = Path.Combine(GetLegacyAppDataDir(), "logs");
            if (Directory.Exists(legacy)) return legacy;
        }
        return newDir;
    }

    /// <summary>
    /// 决定 Agent 会话目录。
    /// </summary>
    public static string GetAgentDir()
    {
        var newDir = Path.Combine(GetAppDataDir(), "agent");
        if (Directory.Exists(newDir)) return newDir;
        if (IsCompatFlagOnDuringBoot())
        {
            var legacy = Path.Combine(GetLegacyAppDataDir(), "agent");
            if (Directory.Exists(legacy)) return legacy;
        }
        return newDir;
    }

    /// <summary>
    /// 在工作目录下寻找 `ecode.json` / `cmux.json` 的回退候选路径。
    /// 返回值顺序：新路径 → 旧路径。
    /// </summary>
    public static IEnumerable<string> EnumerateConfigFileCandidates(string workingDirectory)
    {
        // 新路径优先
        yield return Path.Combine(workingDirectory, NewConfigDir, NewConfigFileName);
        yield return Path.Combine(workingDirectory, NewConfigFileName);
        // 全局配置
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(userProfile, ".config", NewAppFolder, NewConfigFileName);

        // 旧路径（兼容读取）
        if (IsCompatFlagOnDuringBoot())
        {
            yield return Path.Combine(workingDirectory, LegacyConfigDir, LegacyConfigFileName);
            yield return Path.Combine(workingDirectory, LegacyConfigFileName);
            yield return Path.Combine(userProfile, ".config", LegacyAppFolder, LegacyConfigFileName);
        }
    }

    /// <summary>
    /// 旧数据目录的命名管道（`\\.\pipe\cmux`、`\\.\pipe\cmux-{tag}`、`\\.\pipe\cmux-daemon`）是否仍要监听。
    /// </summary>
    public static bool ShouldListenLegacyMainPipe(string? tag)
    {
        return IsCompatFlagOnDuringBoot();
    }

    public static bool ShouldListenLegacyDaemonPipe()
    {
        return IsCompatFlagOnDuringBoot();
    }

    /// <summary>
    /// 旧 mutex 名 `Global\CmuxDaemon` 是否应视为“已存在”来让出单实例。
    /// </summary>
    public static bool ShouldHonorLegacyMutex()
    {
        return IsCompatFlagOnDuringBoot();
    }

    /// <summary>CLI 顶层是否接受旧命令名 `cmux *`。</summary>
    public static bool ShouldAcceptLegacyCliCommand()
    {
        return IsCompatFlagOnDuringBoot();
    }

    /// <summary>
    /// 首启迁移：把旧目录下的子文件/子目录复制到新目录，写一条 `migrated-data` 日志。
    /// 仅执行一次。
    /// </summary>
    public static void EnsureMigrated()
    {
        if (_migrationChecked) return;
        lock (_migrationLock)
        {
            if (_migrationChecked) return;
            _migrationChecked = true;

            try
            {
                if (!IsCompatFlagOnDuringBoot()) return;

                var newDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    NewAppFolder);
                var legacyDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    LegacyAppFolder);

                if (!Directory.Exists(legacyDir)) return;
                if (Directory.Exists(newDir) && Directory.EnumerateFileSystemEntries(newDir).Any())
                {
                    // 新目录已有内容，假定已经迁移过；不要覆盖用户数据。
                    return;
                }

                Directory.CreateDirectory(newDir);
                var logger = DaemonLog.ForMigration();
                int copiedFiles = 0;
                foreach (var entry in Directory.EnumerateFileSystemEntries(legacyDir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(legacyDir, entry);
                    var target = Path.Combine(newDir, rel);
                    if (Directory.Exists(entry))
                    {
                        Directory.CreateDirectory(target);
                    }
                    else if (File.Exists(entry))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(entry, target, overwrite: false);
                        copiedFiles++;
                    }
                }
                logger.Write($"migrated-data source='{legacyDir}' target='{newDir}' files={copiedFiles}");
            }
            catch
            {
                // 迁移失败不应阻塞主程序；错误留给 daemon-debug.log 后续观察。
            }
        }
    }
}

/// <summary>
/// 轻量日志写入器，向 `daemon-debug.log` 追加兼容期事件。
/// 与 <see cref="ECode.Core.IPC.DaemonClient.LogDaemon"/> 共享文件句柄语义。
/// </summary>
internal static class DaemonLog
{
    private static readonly object _lock = new();

    public static DaemonLogWriter ForMigration() => new();

    public readonly struct DaemonLogWriter
    {
        public void Write(string message)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    CompatibilityOptions.NewAppFolder);
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "daemon-debug.log");
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] [compat] {message}{Environment.NewLine}";
                lock (_lock)
                {
                    File.AppendAllText(path, line);
                }
            }
            catch
            {
                // 日志写入失败不应阻塞主流程。
            }
        }
    }
}
