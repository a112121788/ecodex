using System.Text.RegularExpressions;

namespace ECodeX.Cli.Commands;

public sealed record ShellSetupState(
    string UserPath,
    string PowerShellProfile,
    string CmdAutoRun);

public sealed record ShellSetupDiff(
    bool UserPathChanged,
    bool PowerShellProfileChanged,
    bool CmdAutoRunChanged)
{
    public bool AnyChanged => UserPathChanged || PowerShellProfileChanged || CmdAutoRunChanged;
}

public static class ShellSetup
{
    public const string PowerShellBeginMarker = "# >>> ecodex setup >>>";
    public const string PowerShellEndMarker = "# <<< ecodex setup <<<";
    public const string CmdBeginMarker = ":: >>> ecodex setup >>>";
    public const string CmdEndMarker = ":: <<< ecodex setup <<<";

    private const char PathSeparator = ';';

    public static ShellSetupState CreateInstallPlan(ShellSetupState current, string installDirectory)
    {
        var normalized = NormalizeInstallDirectory(installDirectory);
        return new ShellSetupState(
            AddPathEntry(current.UserPath, normalized),
            UpsertMarkedBlock(
                current.PowerShellProfile,
                PowerShellBeginMarker,
                PowerShellEndMarker,
                CreatePowerShellBlock(normalized)),
            UpsertMarkedBlock(
                current.CmdAutoRun,
                CmdBeginMarker,
                CmdEndMarker,
                CreateCmdBlock(normalized)));
    }

    public static ShellSetupState CreateUninstallPlan(ShellSetupState current, string installDirectory)
    {
        var normalized = NormalizeInstallDirectory(installDirectory);
        return new ShellSetupState(
            RemovePathEntry(current.UserPath, normalized),
            RemoveMarkedBlock(current.PowerShellProfile, PowerShellBeginMarker, PowerShellEndMarker),
            RemoveMarkedBlock(current.CmdAutoRun, CmdBeginMarker, CmdEndMarker));
    }

    public static bool IsInstalled(ShellSetupState current, string installDirectory)
    {
        return !CreateDiff(current, CreateInstallPlan(current, installDirectory)).AnyChanged;
    }

    public static ShellSetupDiff CreateDiff(ShellSetupState before, ShellSetupState after)
    {
        return new ShellSetupDiff(
            !string.Equals(before.UserPath, after.UserPath, StringComparison.Ordinal),
            !string.Equals(before.PowerShellProfile, after.PowerShellProfile, StringComparison.Ordinal),
            !string.Equals(before.CmdAutoRun, after.CmdAutoRun, StringComparison.Ordinal));
    }

    public static string FormatDiff(ShellSetupState before, ShellSetupState after)
    {
        var diff = CreateDiff(before, after);
        return string.Join(Environment.NewLine, new[]
        {
            $"PATH: {(diff.UserPathChanged ? "change" : "no change")}",
            $"PowerShell profile: {(diff.PowerShellProfileChanged ? "change" : "no change")}",
            $"cmd AutoRun: {(diff.CmdAutoRunChanged ? "change" : "no change")}",
        });
    }

    public static string AddPathEntry(string currentPath, string installDirectory)
    {
        var normalized = NormalizeInstallDirectory(installDirectory);
        var entries = SplitPath(currentPath).ToList();
        if (entries.Any(entry => SamePathEntry(entry, normalized)))
            return string.Join(PathSeparator, entries);

        entries.Add(normalized);
        return string.Join(PathSeparator, entries);
    }

    public static string RemovePathEntry(string currentPath, string installDirectory)
    {
        var normalized = NormalizeInstallDirectory(installDirectory);
        return string.Join(
            PathSeparator,
            SplitPath(currentPath).Where(entry => !SamePathEntry(entry, normalized)));
    }

    private static string CreatePowerShellBlock(string installDirectory)
    {
        var quotedDirectory = QuotePowerShellString(installDirectory);
        return string.Join(Environment.NewLine, new[]
        {
            PowerShellBeginMarker,
            $"$ecodexBin = {quotedDirectory}",
            "if (($env:Path -split ';') -notcontains $ecodexBin) { $env:Path = \"$ecodexBin;$env:Path\" }",
            "Set-Alias -Name ecodex -Value (Join-Path $ecodexBin 'ecodex.exe') -Scope Global",
            PowerShellEndMarker,
        });
    }

    private static string CreateCmdBlock(string installDirectory)
    {
        var exePath = installDirectory + "\\ecodex.exe";
        return string.Join(Environment.NewLine, new[]
        {
            CmdBeginMarker,
            $"doskey ecodex=\"{exePath}\" $*",
            CmdEndMarker,
        });
    }

    private static string UpsertMarkedBlock(string text, string beginMarker, string endMarker, string block)
    {
        var withoutExisting = RemoveMarkedBlock(text, beginMarker, endMarker);
        return string.IsNullOrWhiteSpace(withoutExisting)
            ? block
            : withoutExisting.TrimEnd() + Environment.NewLine + Environment.NewLine + block;
    }

    private static string RemoveMarkedBlock(string text, string beginMarker, string endMarker)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var pattern = $@"(?ms)^\s*{Regex.Escape(beginMarker)}\s*$.*?^\s*{Regex.Escape(endMarker)}\s*$\r?\n?";
        return Regex.Replace(text, pattern, "").TrimEnd('\r', '\n');
    }

    private static IEnumerable<string> SplitPath(string currentPath)
    {
        return (currentPath ?? "")
            .Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool SamePathEntry(string left, string right)
    {
        return string.Equals(
            NormalizeInstallDirectory(left.Trim('"')),
            NormalizeInstallDirectory(right.Trim('"')),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeInstallDirectory(string installDirectory)
    {
        var value = (installDirectory ?? "").Trim().Trim('"');
        while (value.Length > 1 && IsTrailingDirectorySeparator(value) && !IsWindowsDriveRoot(value))
            value = value[..^1];

        return value;
    }

    private static bool IsTrailingDirectorySeparator(string value)
    {
        return value[^1] is '\\' or '/';
    }

    private static bool IsWindowsDriveRoot(string value)
    {
        return value.Length == 3 && value[1] == ':' && value[2] is '\\' or '/';
    }

    private static string QuotePowerShellString(string value)
    {
        return "'" + value.Replace("'", "''") + "'";
    }
}
