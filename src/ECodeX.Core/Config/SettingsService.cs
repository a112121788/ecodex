using System.Text.Json;

namespace ECodeX.Core.Config;

/// <summary>
/// 管理 <see cref="ECodeXSettings"/> 的读取、写入和缓存。
/// 设置存储于 <c>%USERPROFILE%/.ecodex/settings.json</c>。
/// </summary>
public static class SettingsService
{
    private static string SettingsDir => CompatibilityOptions.GetAppDataDir();

    private static string SettingsPath => CompatibilityOptions.GetSettingsPath();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static ECodeXSettings? _current;

    /// <summary>
    /// 当前内存中的设置实例（首次访问时加载）。
    /// </summary>
    public static ECodeXSettings Current => _current ??= Load();

    /// <summary>
    /// 在调用 <see cref="NotifyChanged"/> 后触发，通知设置已被修改。
    /// </summary>
    public static event Action? SettingsChanged;

    /// <summary>
    /// 从磁盘读取设置。任何失败时返回全新的默认实例。
    /// </summary>
    public static ECodeXSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new ECodeXSettings();

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<ECodeXSettings>(json, JsonOptions) ?? new ECodeXSettings();
        }
        catch
        {
            return new ECodeXSettings();
        }
    }

    /// <summary>
    /// 将给定设置原子性地持久化到磁盘（先写入 .tmp，再移动）。
    /// </summary>
    public static void Save(ECodeXSettings? settings = null)
    {
        settings ??= Current;

        try
        {
            Directory.CreateDirectory(SettingsDir);

            var tmpPath = SettingsPath + ".tmp";
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, SettingsPath, overwrite: true);
        }
        catch
        {
            // 吞掉写入失败（权限问题、磁盘已满等），
            // 以避免让应用程序崩溃。
        }
    }

    /// <summary>
    /// 将设置重置为默认值并持久化。
    /// </summary>
    public static ECodeXSettings Reset()
    {
        _current = new ECodeXSettings();
        Save(_current);
        return _current;
    }

    /// <summary>
    /// 触发 <see cref="SettingsChanged"/> 事件。
    /// 修改 <see cref="Current"/> 属性后调用此方法。
    /// </summary>
    public static void NotifyChanged() => SettingsChanged?.Invoke();
}
