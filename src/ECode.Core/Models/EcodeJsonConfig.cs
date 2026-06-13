namespace ECode.Core.Models;

/// <summary>
/// ecode.json 配置文件根对象 - 定义命令、动作和 UI 配置
/// </summary>
public sealed class EcodeJsonConfig
{
    public Dictionary<string, EcodeAction> Actions { get; set; } = []; // 可绑定的动作（快捷键/按钮触发）
    public List<EcodeCommand> Commands { get; set; } = []; // 命令面板中的命令列表
    public EcodeUiConfig? Ui { get; set; }
}

/// <summary>
/// 命令定义 - 在命令面板中可搜索和执行的命令
/// </summary>
public sealed class EcodeCommand
{
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public List<string> Keywords { get; set; } = [];

    public string Command { get; set; } = "";

    public string Target { get; set; } = EcodeActionTargets.CurrentTerminal;

    public bool Confirm { get; set; }
}

public sealed class EcodeAction
{
    public string Type { get; set; } = "command";

    public string Title { get; set; } = "";

    public string? Subtitle { get; set; }

    public string? Command { get; set; }

    public string Target { get; set; } = EcodeActionTargets.CurrentTerminal;

    public bool Palette { get; set; } = true;

    public bool Confirm { get; set; }
}

public sealed class EcodeUiConfig
{
    public EcodeSurfaceTabBarConfig? SurfaceTabBar { get; set; }
}

public sealed class EcodeSurfaceTabBarConfig
{
    public List<EcodeUiButton> Buttons { get; set; } = [];
}

public sealed class EcodeUiButton
{
    public string Title { get; set; } = "";

    public string? Icon { get; set; }

    public string Action { get; set; } = "";
}

public static class EcodeActionTargets
{
    public const string CurrentTerminal = "currentTerminal";
    public const string NewTabInCurrentPane = "newTabInCurrentPane";
}

public enum EcodeJsonDiagnosticSeverity
{
    Warning,
    Error,
}

public sealed record EcodeJsonDiagnostic(
    EcodeJsonDiagnosticSeverity Severity,
    string Path,
    string Message);

public sealed class EcodeJsonLoadResult
{
    public EcodeJsonConfig Config { get; init; } = new();

    public List<EcodeJsonDiagnostic> Diagnostics { get; init; } = [];

    public List<string> LoadedPaths { get; init; } = [];

    public bool HasErrors => Diagnostics.Any(d => d.Severity == EcodeJsonDiagnosticSeverity.Error);
}
