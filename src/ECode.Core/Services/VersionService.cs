using System.Reflection;

namespace ECode.Core.Services;

/// <summary>
/// 从程序集元数据中提取版本号信息
/// </summary>
public static class VersionService
{
    public static string GetInformationalVersion(Assembly assembly, string fallback = "0.0.0")
    {
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Split('+', 2)[0];
        }

        return assembly.GetName().Version?.ToString() ?? fallback;
    }
}
