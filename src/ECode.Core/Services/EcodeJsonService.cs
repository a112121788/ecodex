using System.Text.Json;
using ECode.Core.Models;

namespace ECode.Core.Services;

/// <summary>
/// 加载、验证和合并全局及项目级 ecode.json 配置文件
/// </summary>
public sealed class EcodeJsonService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public EcodeJsonLoadResult Load(string? workspaceDirectory)
    {
        return Load(workspaceDirectory, GetGlobalConfigPath());
    }

    public EcodeJsonLoadResult Load(string? workspaceDirectory, string? globalConfigPath)
    {
        var paths = new List<string>();

        if (!string.IsNullOrWhiteSpace(globalConfigPath))
            paths.Add(globalConfigPath);

        if (!string.IsNullOrWhiteSpace(workspaceDirectory))
            paths.AddRange(GetWorkspaceConfigPaths(workspaceDirectory));

        return LoadFromFiles(paths);
    }

    public EcodeJsonLoadResult LoadFromFiles(IEnumerable<string> configPaths)
    {
        var result = new EcodeJsonLoadResult();

        foreach (var path in configPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
                continue;

            try
            {
                var json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<EcodeJsonConfig>(json, JsonOptions);
                if (config == null)
                {
                    result.Diagnostics.Add(new EcodeJsonDiagnostic(
                        EcodeJsonDiagnosticSeverity.Error,
                        path,
                        "ecode.json is empty or does not contain a valid object."));
                    continue;
                }

                Normalize(config);
                Validate(config, path, result.Diagnostics);
                MergeInto(result.Config, config);
                result.LoadedPaths.Add(path);
            }
            catch (JsonException ex)
            {
                result.Diagnostics.Add(new EcodeJsonDiagnostic(
                    EcodeJsonDiagnosticSeverity.Error,
                    path,
                    $"Invalid ecode.json schema or JSON syntax: {ex.Message}"));
            }
            catch (IOException ex)
            {
                result.Diagnostics.Add(new EcodeJsonDiagnostic(
                    EcodeJsonDiagnosticSeverity.Error,
                    path,
                    $"Unable to read ecode.json: {ex.Message}"));
            }
            catch (UnauthorizedAccessException ex)
            {
                result.Diagnostics.Add(new EcodeJsonDiagnostic(
                    EcodeJsonDiagnosticSeverity.Error,
                    path,
                    $"Unable to read ecode.json: {ex.Message}"));
            }
        }

        return result;
    }

    public static IReadOnlyList<string> GetWorkspaceConfigPaths(string workspaceDirectory)
    {
        return
        [
            Path.Combine(workspaceDirectory, ".ecode", "ecode.json"),
            Path.Combine(workspaceDirectory, "ecode.json"),
        ];
    }

    public static string GetGlobalConfigPath()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(profile, ".config", "ecode", "ecode.json");
    }

    private static void Normalize(EcodeJsonConfig config)
    {
        config.Actions ??= [];
        config.Commands ??= [];

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
            ? EcodeActionTargets.CurrentTerminal
            : target.Trim();
    }

    private static void Validate(EcodeJsonConfig config, string path, List<EcodeJsonDiagnostic> diagnostics)
    {
        for (var i = 0; i < config.Commands.Count; i++)
        {
            var command = config.Commands[i];
            if (string.IsNullOrWhiteSpace(command.Name))
            {
                diagnostics.Add(new EcodeJsonDiagnostic(
                    EcodeJsonDiagnosticSeverity.Error,
                    path,
                    $"commands[{i}].name is required."));
            }

            if (string.IsNullOrWhiteSpace(command.Command))
            {
                diagnostics.Add(new EcodeJsonDiagnostic(
                    EcodeJsonDiagnosticSeverity.Error,
                    path,
                    $"commands[{i}].command is required."));
            }

            ValidateTarget(command.Target, path, $"commands[{i}].target", diagnostics);
        }

        foreach (var (id, action) in config.Actions)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                diagnostics.Add(new EcodeJsonDiagnostic(
                    EcodeJsonDiagnosticSeverity.Error,
                    path,
                    "actions contains an empty action id."));
            }

            if (!string.Equals(action.Type, "command", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new EcodeJsonDiagnostic(
                    EcodeJsonDiagnosticSeverity.Warning,
                    path,
                    $"actions.{id}.type '{action.Type}' is not supported in M1 and will be ignored by the UI."));
            }

            if (string.Equals(action.Type, "command", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(action.Command))
            {
                diagnostics.Add(new EcodeJsonDiagnostic(
                    EcodeJsonDiagnosticSeverity.Error,
                    path,
                    $"actions.{id}.command is required for command actions."));
            }

            ValidateTarget(action.Target, path, $"actions.{id}.target", diagnostics);
        }
    }

    private static void ValidateTarget(
        string target,
        string path,
        string field,
        List<EcodeJsonDiagnostic> diagnostics)
    {
        if (string.Equals(target, EcodeActionTargets.CurrentTerminal, StringComparison.Ordinal) ||
            string.Equals(target, EcodeActionTargets.NewTabInCurrentPane, StringComparison.Ordinal))
        {
            return;
        }

        diagnostics.Add(new EcodeJsonDiagnostic(
            EcodeJsonDiagnosticSeverity.Warning,
            path,
            $"{field} '{target}' is not supported in M1."));
    }

    private static void MergeInto(EcodeJsonConfig target, EcodeJsonConfig source)
    {
        MergeCommands(target.Commands, source.Commands);

        foreach (var (id, action) in source.Actions)
            target.Actions[id] = action;

        if (source.Ui != null)
            target.Ui = source.Ui;
    }

    private static void MergeCommands(List<EcodeCommand> target, List<EcodeCommand> source)
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
