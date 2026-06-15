using System.Text.Json;
using ECodeX.Core.Config;
using ECodeX.Core.Models;

namespace ECodeX.Core.Services;

/// <summary>
/// 读写 resume.json，并提供面向 surface 的恢复绑定查询与信任前缀更新。
/// </summary>
public sealed class ResumeBindingService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath;

    public ResumeBindingService(string? filePath = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(CompatibilityOptions.GetAppDataDir(), "resume.json")
            : filePath;
    }

    public ResumeBindingFile Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new ResumeBindingFile();

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<ResumeBindingFile>(json, JsonOptions) ?? new ResumeBindingFile();
        }
        catch
        {
            return new ResumeBindingFile();
        }
    }

    public void Save(ResumeBindingFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? ".");
        ScrubSensitiveEnvironment(file);
        var json = JsonSerializer.Serialize(file, JsonOptions);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    public ResumeBinding Add(ResumeBinding binding)
    {
        var file = Load();
        var now = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(binding.Id))
            binding.Id = Guid.NewGuid().ToString();

        if (binding.CreatedAtUtc == default)
            binding.CreatedAtUtc = now;

        binding.Environment = DropSensitiveEnvironment(binding.Environment);
        binding.UpdatedAtUtc = now;

        var existingIndex = file.Bindings.FindIndex(b => string.Equals(b.Id, binding.Id, StringComparison.Ordinal));
        if (existingIndex >= 0)
            file.Bindings[existingIndex] = binding;
        else
            file.Bindings.Add(binding);

        Save(file);
        return binding;
    }

    public ResumeBinding SetForPane(ResumeBinding binding)
    {
        if (string.IsNullOrWhiteSpace(binding.WorkspaceId))
            throw new ArgumentException("WorkspaceId is required.", nameof(binding));
        if (string.IsNullOrWhiteSpace(binding.SurfaceId))
            throw new ArgumentException("SurfaceId is required.", nameof(binding));
        if (string.IsNullOrWhiteSpace(binding.PaneId))
            throw new ArgumentException("PaneId is required.", nameof(binding));

        var file = Load();
        var now = DateTime.UtcNow;
        var existing = file.Bindings
            .Where(b =>
                string.Equals(b.WorkspaceId, binding.WorkspaceId, StringComparison.Ordinal) &&
                string.Equals(b.SurfaceId, binding.SurfaceId, StringComparison.Ordinal) &&
                string.Equals(b.PaneId, binding.PaneId, StringComparison.Ordinal))
            .OrderByDescending(b => b.UpdatedAtUtc)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(binding.Id))
            binding.Id = existing?.Id ?? Guid.NewGuid().ToString();

        if (binding.CreatedAtUtc == default)
            binding.CreatedAtUtc = existing?.CreatedAtUtc ?? now;

        binding.Environment = DropSensitiveEnvironment(binding.Environment);
        binding.UpdatedAtUtc = now;

        file.Bindings.RemoveAll(b =>
            string.Equals(b.WorkspaceId, binding.WorkspaceId, StringComparison.Ordinal) &&
            string.Equals(b.SurfaceId, binding.SurfaceId, StringComparison.Ordinal) &&
            string.Equals(b.PaneId, binding.PaneId, StringComparison.Ordinal));
        file.Bindings.Add(binding);

        Save(file);
        return binding;
    }

    public bool Remove(string bindingId)
    {
        if (string.IsNullOrWhiteSpace(bindingId))
            return false;

        var file = Load();
        var removed = file.Bindings.RemoveAll(b => string.Equals(b.Id, bindingId, StringComparison.Ordinal)) > 0;
        if (removed)
            Save(file);

        return removed;
    }

    public int RemoveForPane(string workspaceId, string surfaceId, string paneId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId) ||
            string.IsNullOrWhiteSpace(surfaceId) ||
            string.IsNullOrWhiteSpace(paneId))
        {
            return 0;
        }

        var file = Load();
        var removed = file.Bindings.RemoveAll(b =>
            string.Equals(b.WorkspaceId, workspaceId, StringComparison.Ordinal) &&
            string.Equals(b.SurfaceId, surfaceId, StringComparison.Ordinal) &&
            string.Equals(b.PaneId, paneId, StringComparison.Ordinal));

        if (removed > 0)
            Save(file);

        return removed;
    }

    public IReadOnlyList<ResumeBinding> FindForSurface(string workspaceId, string surfaceId)
    {
        return Load().Bindings
            .Where(b =>
                string.Equals(b.WorkspaceId, workspaceId, StringComparison.Ordinal) &&
                string.Equals(b.SurfaceId, surfaceId, StringComparison.Ordinal))
            .OrderByDescending(b => b.UpdatedAtUtc)
            .ToList();
    }

    public int TrustPrefix(string workspaceId, string surfaceId, string approvedPrefix, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(approvedPrefix))
            return 0;

        var file = Load();
        var trustedCount = 0;
        var now = DateTime.UtcNow;

        foreach (var binding in file.Bindings)
        {
            if (!string.Equals(binding.WorkspaceId, workspaceId, StringComparison.Ordinal) ||
                !string.Equals(binding.SurfaceId, surfaceId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(workingDirectory) &&
                !string.Equals(binding.WorkingDirectory, workingDirectory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!binding.Shell.StartsWith(approvedPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            binding.Trusted = true;
            binding.TrustReason = "user-approved-prefix";
            binding.ApprovedPrefix = approvedPrefix;
            binding.UpdatedAtUtc = now;
            trustedCount++;
        }

        if (trustedCount > 0)
            Save(file);

        return trustedCount;
    }

    public bool TrustBinding(string bindingId, string trustReason = "user-approved-binding")
    {
        if (string.IsNullOrWhiteSpace(bindingId))
            return false;

        var file = Load();
        var binding = file.Bindings.FirstOrDefault(b => string.Equals(b.Id, bindingId, StringComparison.Ordinal));
        if (binding == null)
            return false;

        binding.Trusted = true;
        binding.TrustReason = string.IsNullOrWhiteSpace(trustReason) ? "user-approved-binding" : trustReason;
        binding.ApprovedPrefix = string.IsNullOrWhiteSpace(binding.ApprovedPrefix)
            ? binding.Shell
            : binding.ApprovedPrefix;
        binding.UpdatedAtUtc = DateTime.UtcNow;
        Save(file);
        return true;
    }

    public static Dictionary<string, string> DropSensitiveEnvironment(IReadOnlyDictionary<string, string>? environment)
    {
        if (environment == null || environment.Count == 0)
            return [];

        return environment
            .Where(kvp => !IsSensitiveEnvironmentName(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static void ScrubSensitiveEnvironment(ResumeBindingFile file)
    {
        foreach (var binding in file.Bindings)
        {
            binding.Environment = DropSensitiveEnvironment(binding.Environment);
        }
    }

    private static bool IsSensitiveEnvironmentName(string key)
    {
        var upper = key.ToUpperInvariant();
        return upper.Contains("PASSWORD", StringComparison.Ordinal) ||
               upper.Contains("PASSWD", StringComparison.Ordinal) ||
               upper.Contains("SECRET", StringComparison.Ordinal) ||
               upper.Contains("API_KEY", StringComparison.Ordinal) ||
               upper.Contains("ACCESS_KEY", StringComparison.Ordinal) ||
               upper is "TOKEN" ||
               upper.EndsWith("_TOKEN", StringComparison.Ordinal) ||
               upper.Contains("_TOKEN_", StringComparison.Ordinal) ||
               upper.StartsWith("TOKEN_", StringComparison.Ordinal);
    }
}
