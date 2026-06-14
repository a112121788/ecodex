using System.Text.Json;
using ECode.Core.Config;
using ECode.Core.IPC;
using ECode.Core.Services;

namespace ECode.Cli;

/// <summary>
/// ecode 命令行工具
/// 通过命名管道与正在运行的 ecode 应用通信。
///
/// 用法：
///   ecode notify --title "Title" --body "Body"
///   ecode workspace list
///   ecode workspace create --name "My Project"
///   ecode workspace select --index 0
///   ecode surface create
///   ecode split right
///   ecode split down
///   ecode status
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        // 兼容期：若用户在 CompatAcceptLegacyCliCommand=true 时仍然以
        // `cmux ...` 形式调用，把首参重写为 `ecode` 之后再走分派。
        if (CompatibilityOptions.ShouldAcceptLegacyCliCommand()
            && string.Equals(args[0], "cmux", StringComparison.Ordinal))
        {
            args = new[] { "ecode" }.Concat(args.Skip(1)).ToArray();
        }

        var command = args[0].ToLowerInvariant();

        try
        {
            return command switch
            {
                "notify" => await HandleNotify(args[1..]),
                "workspace" => await HandleWorkspace(args[1..]),
                "surface" => await HandleSurface(args[1..]),
                "browser" => await HandleBrowser(args[1..]),
                "split" => await HandleSplit(args[1..]),
                "restore-session" => await HandleRestoreSession(args[1..]),
                "reload-config" => await HandleReloadConfig(),
                "status" => await HandleStatus(),
                "help" or "--help" or "-h" => PrintHelp(),
                "version" or "--version" or "-v" => PrintVersion(),
                _ => Error($"Unknown command: {command}"),
            };
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("Error: Could not connect to ecode. Is it running?");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleNotify(string[] args)
    {
        var parsed = ParseArgs(args);
        var title = parsed.GetValueOrDefault("title", parsed.GetValueOrDefault("_arg0", "Terminal"));
        var body = parsed.GetValueOrDefault("body", parsed.GetValueOrDefault("_arg1", ""));
        var subtitle = parsed.GetValueOrDefault("subtitle");

        var cmdArgs = new Dictionary<string, string>
        {
            ["title"] = title,
            ["body"] = body,
        };
        if (subtitle != null) cmdArgs["subtitle"] = subtitle;

        var response = await NamedPipeClient.SendCommand("NOTIFY", cmdArgs);
        Console.WriteLine(response);
        return 0;
    }

    private static async Task<int> HandleWorkspace(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: ecode workspace <list|create|select>");
            return 1;
        }

        var subcommand = args[0].ToLowerInvariant();
        var parsed = ParseArgs(args[1..]);

        return subcommand switch
        {
            "list" or "ls" => await SendAndPrint("WORKSPACE.LIST"),
            "create" or "new" => await SendAndPrint("WORKSPACE.CREATE", parsed),
            "select" => await SendAndPrint("WORKSPACE.SELECT", parsed),
            "next" => await SendAndPrint("WORKSPACE.NEXT"),
            "previous" or "prev" => await SendAndPrint("WORKSPACE.PREVIOUS"),
            _ => Error($"Unknown workspace command: {subcommand}"),
        };
    }

    private static async Task<int> HandleSurface(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: ecode surface <create|next|previous|resume>");
            return 1;
        }

        var subcommand = args[0].ToLowerInvariant();

        return subcommand switch
        {
            "create" or "new" => await SendAndPrint("SURFACE.CREATE"),
            "next" => await SendAndPrint("SURFACE.NEXT"),
            "previous" or "prev" => await SendAndPrint("SURFACE.PREVIOUS"),
            "resume" => await HandleSurfaceResume(args[1..]),
            _ => Error($"Unknown surface command: {subcommand}"),
        };
    }

    private static async Task<int> HandleSurfaceResume(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: ecode surface resume <show|set|clear>");
            return 1;
        }

        var subcommand = args[0].ToLowerInvariant();
        var parsed = ParseArgs(args[1..]);
        NormalizeResumeSelectorAliases(parsed);

        return subcommand switch
        {
            "show" or "ls" or "list" => await SendAndPrint("SURFACE.RESUME.SHOW", parsed),
            "set" => await SendAndPrint("SURFACE.RESUME.SET", parsed),
            "clear" or "rm" or "remove" => await SendAndPrint("SURFACE.RESUME.CLEAR", parsed),
            _ => Error($"Unknown surface resume command: {subcommand}"),
        };
    }

    private static async Task<int> HandleSplit(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: ecode split <right|down>");
            return 1;
        }

        var direction = args[0].ToLowerInvariant();

        return direction switch
        {
            "right" or "vertical" or "v" => await SendAndPrint("SPLIT.RIGHT"),
            "down" or "horizontal" or "h" => await SendAndPrint("SPLIT.DOWN"),
            _ => Error($"Unknown split direction: {direction}"),
        };
    }

    private static async Task<int> HandleBrowser(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: ecode browser <open|new|open-split> <url>");
            return 1;
        }

        var subcommand = args[0].ToLowerInvariant();
        var parsed = ParseArgs(args[1..]);
        NormalizeBrowserArgs(parsed);

        return subcommand switch
        {
            "open" => await SendAndPrint("BROWSER.OPEN", parsed),
            "new" => await SendAndPrint("BROWSER.NEW", parsed),
            "open-split" or "split" => await SendAndPrint("BROWSER.OPEN_SPLIT", parsed),
            _ => Error($"Unknown browser command: {subcommand}"),
        };
    }

    private static async Task<int> HandleStatus()
    {
        return await SendAndPrint("STATUS");
    }

    private static async Task<int> HandleRestoreSession(string[] args)
    {
        var parsed = ParseArgs(args);
        NormalizeResumeSelectorAliases(parsed);
        return await SendAndPrint("SESSION.RESTORE", parsed);
    }

    private static async Task<int> HandleReloadConfig()
    {
        return await SendAndPrint("CONFIG.RELOAD");
    }

    private static async Task<int> SendAndPrint(string command, Dictionary<string, string>? args = null)
    {
        var response = await NamedPipeClient.SendCommand(command, args);

        // 美化输出 JSON
        try
        {
            using var doc = JsonDocument.Parse(response);
            var pretty = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(pretty);
        }
        catch
        {
            Console.WriteLine(response);
        }

        return 0;
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var result = new Dictionary<string, string>();
        int positional = 0;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.StartsWith("--"))
            {
                var key = arg[2..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    result[key] = args[i + 1];
                    i++;
                }
                else
                {
                    result[key] = "true";
                }
            }
            else if (arg.StartsWith('-') && arg.Length == 2)
            {
                var key = arg[1..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                {
                    result[key] = args[i + 1];
                    i++;
                }
                else
                {
                    result[key] = "true";
                }
            }
            else
            {
                result[$"_arg{positional}"] = arg;
                positional++;
            }
        }

        return result;
    }

    private static void NormalizeResumeSelectorAliases(Dictionary<string, string> args)
    {
        CopyAlias(args, "workspace", "workspaceName");
        CopyAlias(args, "surface", "surfaceName");
        CopyAlias(args, "pane", "paneName");
        CopyAlias(args, "cwd", "workingDirectory");
    }

    private static void NormalizeBrowserArgs(Dictionary<string, string> args)
    {
        CopyAlias(args, "_arg0", "url");
        CopyAlias(args, "workspace", "workspaceName");
        CopyAlias(args, "surface", "surfaceName");
    }

    private static void CopyAlias(Dictionary<string, string> args, string source, string target)
    {
        if (args.ContainsKey(target))
            return;

        if (args.TryGetValue(source, out var value))
            args[target] = value;
    }

    private static int PrintHelp()
    {
        Console.WriteLine("""
            ecode - Terminal multiplexer (Windows)

            Usage:
              ecode <command> [options]

            Commands:
              notify                Send a notification
                --title <text>      Notification title (default: "Terminal")
                --body <text>       Notification body
                --subtitle <text>   Notification subtitle

              workspace             Manage projects
                list                List all projects
                create              Create a new project
                  --name <text>     Project name
                select              Select a project
                  --index <n>       Project index (0-based)
                  --id <id>         Project ID
                next                Switch to next project
                previous            Switch to previous project

              surface               Manage surfaces (tabs within project)
                create              Create a new surface
                next                Switch to next surface
                previous            Switch to previous surface
                resume show         Show resume binding for focused/selected pane
                  --all             Show all bindings in the selected surface
                resume set          Save resume command for focused/selected pane
                  --shell <cmd>     Command to run, or pass it as first positional arg
                  --kind <kind>     tmux | custom (default: custom)
                  --checkpoint <id> Optional checkpoint/session label
                  --cwd <path>      Working directory override
                  --trusted <bool>  Mark binding trusted for future restore
                resume clear        Clear binding by --id or focused/selected pane

              split                 Split the focused pane
                right               Split vertically (left/right)
                down                Split horizontally (top/bottom)

              browser               Open browser surfaces
                open <url>          Open URL in current browser surface or a new one
                new <url>           Create a new browser surface
                open-split <url>    v1 compatibility entry; opens a browser surface
                  --direction <dir> right | down (reserved for mixed-pane support)

              reload-config         Reload ecode.json commands/actions

              restore-session       Refresh resume bindings and focus first recoverable pane
                --all               Scan all workspaces/surfaces
                --trusted           Also run trusted bindings immediately
                --workspace <name>  Select workspace by name
                --surface <name>    Select surface by name

              status                Show ecode status

            Keyboard Shortcuts (in the app):
              Ctrl+N                New project
              Ctrl+1-8              Jump to project 1-8
              Ctrl+9                Jump to last project
              Ctrl+Shift+W          Close project
              Ctrl+B                Toggle sidebar
              Ctrl+T                New surface (tab)
              Ctrl+W                Close surface
              Ctrl+D                Split right
              Ctrl+Shift+D          Split down
              Ctrl+Alt+Arrow        Focus pane directionally
              Ctrl+I                Toggle notification panel
              Ctrl+Shift+U          Jump to latest unread
              Ctrl+Shift+O          Restore session bindings
              Ctrl+Shift+,          Reload ecode.json
            """);
        return 0;
    }

    private static int PrintVersion()
    {
        var version = VersionService.GetInformationalVersion(typeof(Program).Assembly);
        Console.WriteLine($"ecode {version} (Windows)");
        return 0;
    }

    private static int Error(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
        return 1;
    }
}
