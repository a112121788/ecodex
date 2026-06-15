using System.Text.Json;
using ECodeX.Core.Models;

namespace ECodeX.Core.Services;

/// <summary>
/// 加载、验证和合并全局及项目级 ecodex.json 配置文件
/// </summary>
public sealed class EcodexJsonService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public EcodexJsonLoadResult Load(string? workspaceDirectory)
    {
        return Load(workspaceDirectory, GetGlobalConfigPath());
    }

    public EcodexJsonLoadResult Load(string? workspaceDirectory, string? globalConfigPath)
    {
        var paths = new List<string>();

        if (!string.IsNullOrWhiteSpace(globalConfigPath))
            paths.Add(globalConfigPath);

        if (!string.IsNullOrWhiteSpace(workspaceDirectory))
            paths.AddRange(GetWorkspaceConfigPaths(workspaceDirectory));

        return LoadFromFiles(paths);
    }

    public EcodexJsonLoadResult LoadFromFiles(IEnumerable<string> configPaths)
    {
        var result = new EcodexJsonLoadResult();

        foreach (var path in configPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
                continue;

            try
            {
                var json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<EcodexJsonConfig>(json, JsonOptions);
                if (config == null)
                {
                    result.Diagnostics.Add(new EcodexJsonDiagnostic(
                        EcodexJsonDiagnosticSeverity.Error,
                        path,
                        "ecodex.json is empty or does not contain a valid object."));
                    continue;
                }

                Normalize(config);
                Validate(config, path, result.Diagnostics);
                MergeInto(result.Config, config);
                result.LoadedPaths.Add(path);
            }
            catch (JsonException ex)
            {
                result.Diagnostics.Add(new EcodexJsonDiagnostic(
                    EcodexJsonDiagnosticSeverity.Error,
                    path,
                    $"Invalid ecodex.json schema or JSON syntax: {ex.Message}"));
            }
            catch (IOException ex)
            {
                result.Diagnostics.Add(new EcodexJsonDiagnostic(
                    EcodexJsonDiagnosticSeverity.Error,
                    path,
                    $"Unable to read ecodex.json: {ex.Message}"));
            }
            catch (UnauthorizedAccessException ex)
            {
                result.Diagnostics.Add(new EcodexJsonDiagnostic(
                    EcodexJsonDiagnosticSeverity.Error,
                    path,
                    $"Unable to read ecodex.json: {ex.Message}"));
            }
        }

        return result;
    }

    public static IReadOnlyList<string> GetWorkspaceConfigPaths(string workspaceDirectory)
    {
        return
        [
            Path.Combine(workspaceDirectory, ".ecodex", "ecodex.json"),
            Path.Combine(workspaceDirectory, "ecodex.json"),
        ];
    }

    public static string GetGlobalConfigPath()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(profile, ".config", "ecodex", "ecodex.json");
    }

    private static void Normalize(EcodexJsonConfig config)
    {
        config.Actions ??= [];
        config.Commands ??= [];
        if (config.Workspace != null)
        {
            config.Workspace.Surfaces ??= [];
            foreach (var surface in config.Workspace.Surfaces)
            {
                surface.Type = NormalizeSurfaceType(surface.Type);
                surface.Name = string.IsNullOrWhiteSpace(surface.Name) ? null : surface.Name.Trim();
                surface.Url = string.IsNullOrWhiteSpace(surface.Url) ? null : surface.Url.Trim();
            }
        }

        foreach (var command in config.Commands)
        {
            command.Name = command.Name.Trim();
            command.Command = command.Command.Trim();
            command.Target = NormalizeTarget(command.Target);
            command.Keywords = command.Keywords
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        foreach (var (_, action) in config.Actions)
        {
            action.Type = string.IsNullOrWhiteSpace(action.Type) ? "command" : action.Type.Trim();
            action.Title = action.Title.Trim();
            action.Command = string.IsNullOrWhiteSpace(action.Command) ? null : action.Command.Trim();
            action.Target = NormalizeTarget(action.Target);
        }

        if (config.Ui?.SurfaceTabBar?.Buttons != null)
        {
            foreach (var button in config.Ui.SurfaceTabBar.Buttons)
            {
                button.Title = button.Title.Trim();
                button.Action = button.Action.Trim();
            }
        }
    }

    private static string NormalizeTarget(string? target)
    {
        return string.IsNullOrWhiteSpace(target)
            ? EcodexActionTargets.CurrentTerminal
            : target.Trim();
    }

    private static string NormalizeSurfaceType(string? type)
    {
        return string.IsNullOrWhiteSpace(type)
            ? EcodexSurfaceTypes.Terminal
            : type.Trim().ToLowerInvariant();
    }

    private static void Validate(EcodexJsonConfig config, string path, List<EcodexJsonDiagnostic> diagnostics)
    {
        for (var i = 0; i < config.Commands.Count; i++)
        {
            var command = config.Commands[i];
            if (string.IsNullOrWhiteSpace(command.Name))
            {
                diagnostics.Add(new EcodexJsonDiagnostic(
                    EcodexJsonDiagnosticSeverity.Error,
                    path,
                    $"commands[{i}].name is required."));
            }

            if (string.IsNullOrWhiteSpace(command.Command))
            {
                diagnostics.Add(new EcodexJsonDiagnostic(
                    EcodexJsonDiagnosticSeverity.Error,
                    path,
                    $"commands[{i}].command is required."));
            }

            ValidateTarget(command.Target, path, $"commands[{i}].target", diagnostics);
        }

        foreach (var (id, action) in config.Actions)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                diagnostics.Add(new EcodexJsonDiagnostic(
                    EcodexJsonDiagnosticSeverity.Error,
                    path,
                    "actions contains an empty action id."));
            }

            if (!string.Equals(action.Type, "command", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new EcodexJsonDiagnostic(
                    EcodexJsonDiagnosticSeverity.Warning,
                    path,
                    $"actions.{id}.type '{action.Type}' is not supported in M1 and will be ignored by the UI."));
            }

            if (string.Equals(action.Type, "command", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(action.Command))
            {
                diagnostics.Add(new EcodexJsonDiagnostic(
                    EcodexJsonDiagnosticSeverity.Error,
                    path,
                    $"actions.{id}.command is required for command actions."));
            }

            ValidateTarget(action.Target, path, $"actions.{id}.target", diagnostics);
        }

        if (config.Workspace?.Surfaces != null)
        {
            for (var i = 0; i < config.Workspace.Surfaces.Count; i++)
            {
                var surface = config.Workspace.Surfaces[i];
                if (!string.Equals(surface.Type, EcodexSurfaceTypes.Terminal, StringComparison.Ordinal) &&
                    !string.Equals(surface.Type, EcodexSurfaceTypes.Browser, StringComparison.Ordinal))
                {
                    diagnostics.Add(new EcodexJsonDiagnostic(
                        EcodexJsonDiagnosticSeverity.Error,
                        path,
                        $"workspace.surfaces[{i}].type '{surface.Type}' is not supported."));
                    continue;
                }

                if (string.Equals(surface.Type, EcodexSurfaceTypes.Browser, StringComparison.Ordinal) &&
                    string.IsNullOrWhiteSpace(surface.Url))
                {
                    diagnostics.Add(new EcodexJsonDiagnostic(
                        EcodexJsonDiagnosticSeverity.Error,
                        path,
                        $"workspace.surfaces[{i}].url is required for browser surfaces."));
                }
            }

            if (config.Workspace.SelectedSurfaceIndex is { } selectedIndex &&
                (selectedIndex < 0 || selectedIndex >= config.Workspace.Surfaces.Count))
            {
                diagnostics.Add(new EcodexJsonDiagnostic(
                    EcodexJsonDiagnosticSeverity.Warning,
                    path,
                    $"workspace.selectedSurfaceIndex {selectedIndex} is outside workspace.surfaces."));
            }
        }
    }

    private static void ValidateTarget(
        string target,
        string path,
        string field,
        List<EcodexJsonDiagnostic> diagnostics)
    {
        if (string.Equals(target, EcodexActionTargets.CurrentTerminal, StringComparison.Ordinal) ||
            string.Equals(target, EcodexActionTargets.NewTabInCurrentPane, StringComparison.Ordinal))
        {
            return;
        }

        diagnostics.Add(new EcodexJsonDiagnostic(
            EcodexJsonDiagnosticSeverity.Warning,
            path,
            $"{field} '{target}' is not supported in M1."));
    }

    private static void MergeInto(EcodexJsonConfig target, EcodexJsonConfig source)
    {
        MergeCommands(target.Commands, source.Commands);

        foreach (var (id, action) in source.Actions)
            target.Actions[id] = action;

        if (source.Workspace != null)
            target.Workspace = source.Workspace;

        if (source.Ui != null)
            target.Ui = source.Ui;
    }

    private static void MergeCommands(List<EcodexCommand> target, List<EcodexCommand> source)
    {
        foreach (var command in source)
        {
            var existingIndex = target.FindIndex(c => string.Equals(c.Name, command.Name, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
                target[existingIndex] = command;
            else
                target.Add(command);
        }
    }
}
