namespace ECodeX.Core.Models;

/// <summary>
/// ecodex.json 配置文件根对象 - 定义命令、动作和 UI 配置
/// </summary>
public sealed class EcodexJsonConfig
{
    public Dictionary<string, EcodexAction> Actions { get; set; } = []; // 可绑定的动作（快捷键/按钮触发）
    public List<EcodexCommand> Commands { get; set; } = []; // 命令面板中的命令列表
    public EcodexWorkspaceConfig? Workspace { get; set; } // 工作区布局配置
    public EcodexUiConfig? Ui { get; set; }
}

/// <summary>
/// 命令定义 - 在命令面板中可搜索和执行的命令
/// </summary>
public sealed class EcodexCommand
{
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public List<string> Keywords { get; set; } = [];

    public string Command { get; set; } = "";

    public string Target { get; set; } = EcodexActionTargets.CurrentTerminal;

    public bool Confirm { get; set; }
}

public sealed class EcodexAction
{
    public string Type { get; set; } = "command";

    public string Title { get; set; } = "";

    public string? Subtitle { get; set; }

    public string? Command { get; set; }

    public string Target { get; set; } = EcodexActionTargets.CurrentTerminal;

    public bool Palette { get; set; } = true;

    public bool Confirm { get; set; }
}

public sealed class EcodexUiConfig
{
    public EcodexSurfaceTabBarConfig? SurfaceTabBar { get; set; }
}

public sealed class EcodexWorkspaceConfig
{
    public List<EcodexSurfaceConfig> Surfaces { get; set; } = [];

    public int? SelectedSurfaceIndex { get; set; }
}

public sealed class EcodexSurfaceConfig
{
    public string Type { get; set; } = EcodexSurfaceTypes.Terminal;

    public string? Name { get; set; }

    public string? Url { get; set; }
}

public sealed class EcodexSurfaceTabBarConfig
{
    public List<EcodexUiButton> Buttons { get; set; } = [];
}

public sealed class EcodexUiButton
{
    public string Title { get; set; } = "";

    public string? Icon { get; set; }

    public string Action { get; set; } = "";
}

public static class EcodexActionTargets
{
    public const string CurrentTerminal = "currentTerminal";
    public const string NewTabInCurrentPane = "newTabInCurrentPane";
}

public static class EcodexSurfaceTypes
{
    public const string Terminal = "terminal";
    public const string Browser = "browser";
}

public enum EcodexJsonDiagnosticSeverity
{
    Warning,
    Error,
}

public sealed record EcodexJsonDiagnostic(
    EcodexJsonDiagnosticSeverity Severity,
    string Path,
    string Message);

public sealed class EcodexJsonLoadResult
{
    public EcodexJsonConfig Config { get; init; } = new();

    public List<EcodexJsonDiagnostic> Diagnostics { get; init; } = [];

    public List<string> LoadedPaths { get; init; } = [];

    public bool HasErrors => Diagnostics.Any(d => d.Severity == EcodexJsonDiagnosticSeverity.Error);
}
