using System.Security.Cryptography;
using System.Text.Json;
using ECodeX.Core.Config;

namespace ECodeX.Core.Services;

/// <summary>
/// 使用 Windows DPAPI 加密存储密钥到 %USERPROFILE%/.ecodex/secrets.json。
/// </summary>
public static class SecretStoreService
{
    private static string SecretsDir => CompatibilityOptions.GetAppDataDir();
    private static string SecretsPath => Path.Combine(SecretsDir, "secrets.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string? GetSecret(string secretName)
    {
        if (string.IsNullOrWhiteSpace(secretName))
            return null;

        try
        {
            var map = LoadRawSecrets();
            if (!map.TryGetValue(secretName, out var encoded) || string.IsNullOrWhiteSpace(encoded))
                return null;

            var encrypted = Convert.FromBase64String(encoded);
            var plain = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }

    public static void SetSecret(string secretName, string? value)
    {
        if (string.IsNullOrWhiteSpace(secretName))
            return;

        try
        {
            var map = LoadRawSecrets();

            if (string.IsNullOrWhiteSpace(value))
            {
                map.Remove(secretName);
            }
            else
            {
                var plain = System.Text.Encoding.UTF8.GetBytes(value);
                var encrypted = ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);
                map[secretName] = Convert.ToBase64String(encrypted);
            }

            SaveRawSecrets(map);
        }
        catch
        {
            // 忽略密钥持久化错误以避免应用崩溃。
        }
    }

    public static void RemoveSecret(string secretName)
    {
        SetSecret(secretName, null);
    }

    private static Dictionary<string, string> LoadRawSecrets()
    {
        try
        {
            if (!File.Exists(SecretsPath))
                return [];

            var json = File.ReadAllText(SecretsPath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void SaveRawSecrets(Dictionary<string, string> map)
    {
        Directory.CreateDirectory(SecretsDir);

        var tmp = SecretsPath + ".tmp";
        var json = JsonSerializer.Serialize(map, JsonOptions);
        File.WriteAllText(tmp, json);
        File.Move(tmp, SecretsPath, overwrite: true);
    }
}
