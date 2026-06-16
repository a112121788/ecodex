using System.Text.RegularExpressions;

namespace ECodex.Core.Services;

public enum PowerShellHookSetupStatus
{
    Installed,
    Missing,
    Drifted,
    Conflict,
}

public sealed record PowerShellHookPlan(
    PowerShellHookSetupStatus Status,
    bool Changed,
    string ProfileText,
    string Message);

public sealed record PowerShellHookInstallOptions(
    string ProfilePath,
    string BackupDirectory,
    string InstallDirectory);

public sealed record PowerShellHookInstallResult(
    PowerShellHookSetupStatus Status,
    bool Changed,
    string ProfilePath,
    string? BackupPath,
    string Message);

public sealed class PowerShellHookSetupService
{
    public const string BeginMarker = "# >>> ecodex shell integration >>>";
    public const string EndMarker = "# <<< ecodex shell integration <<<";

    private readonly Func<DateTimeOffset> _clock;

    public PowerShellHookSetupService()
        : this(() => DateTimeOffset.UtcNow)
    {
    }

    public PowerShellHookSetupService(Func<DateTimeOffset> clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public PowerShellHookInstallResult Install(PowerShellHookInstallOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var current = File.Exists(options.ProfilePath)
            ? File.ReadAllText(options.ProfilePath)
            : "";
        var plan = CreateInstallPlan(current, options.InstallDirectory);
        if (!plan.Changed)
        {
            return new PowerShellHookInstallResult(
                plan.Status,
                Changed: false,
                options.ProfilePath,
                BackupPath: null,
                plan.Message);
        }

        var backupPath = BackupFileIfExists(options.ProfilePath, options.BackupDirectory, _clock);
        var profileDirectory = Path.GetDirectoryName(options.ProfilePath);
        if (!string.IsNullOrWhiteSpace(profileDirectory))
            Directory.CreateDirectory(profileDirectory);

        File.WriteAllText(options.ProfilePath, plan.ProfileText);
        return new PowerShellHookInstallResult(
            plan.Status,
            Changed: true,
            options.ProfilePath,
            backupPath,
            plan.Message);
    }

    public static PowerShellHookPlan CreateInstallPlan(string profileText, string installDirectory)
    {
        profileText ??= "";
        if (HasMarkerConflict(profileText))
            return new PowerShellHookPlan(PowerShellHookSetupStatus.Conflict, false, profileText, "PowerShell hook marker conflict.");

        var block = CreateHookBlock(NormalizeInstallDirectory(installDirectory));
        var hasBlock = profileText.Contains(BeginMarker, StringComparison.Ordinal);
        var planned = UpsertMarkedBlock(profileText, block);
        if (!hasBlock)
            return new PowerShellHookPlan(PowerShellHookSetupStatus.Missing, true, planned, "PowerShell hook missing.");

        if (string.Equals(NormalizeLineEndings(profileText), NormalizeLineEndings(planned), StringComparison.Ordinal))
            return new PowerShellHookPlan(PowerShellHookSetupStatus.Installed, false, profileText, "PowerShell hook installed.");

        return new PowerShellHookPlan(PowerShellHookSetupStatus.Drifted, true, planned, "PowerShell hook drifted.");
    }

    public static PowerShellHookPlan CreateUninstallPlan(string profileText)
    {
        profileText ??= "";
        if (HasMarkerConflict(profileText))
            return new PowerShellHookPlan(PowerShellHookSetupStatus.Conflict, false, profileText, "PowerShell hook marker conflict.");

        if (!profileText.Contains(BeginMarker, StringComparison.Ordinal))
            return new PowerShellHookPlan(PowerShellHookSetupStatus.Missing, false, profileText, "PowerShell hook missing.");

        var planned = RemoveMarkedBlock(profileText);
        return new PowerShellHookPlan(PowerShellHookSetupStatus.Installed, true, planned, "PowerShell hook removed.");
    }

    public static string GetDefaultPowerShellProfilePath()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            documents = Path.Combine(profile, "Documents");
        }

        return Path.Combine(documents, "PowerShell", "Microsoft.PowerShell_profile.ps1");
    }

    public static string GetDefaultBackupDirectory()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(profile, ".ecodex", "backups");
    }

    public static string? BackupFileIfExists(
        string filePath,
        string backupDirectory,
        Func<DateTimeOffset>? clock = null)
    {
        if (!File.Exists(filePath))
            return null;

        Directory.CreateDirectory(backupDirectory);
        var timestamp = (clock ?? (() => DateTimeOffset.UtcNow))().UtcDateTime.ToString("yyyyMMdd-HHmmss");
        var extension = Path.GetExtension(filePath);
        var baseName = Path.GetFileNameWithoutExtension(filePath);
        var candidate = Path.Combine(backupDirectory, $"{baseName}-{timestamp}{extension}");
        var suffix = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(backupDirectory, $"{baseName}-{timestamp}-{suffix}{extension}");
            suffix++;
        }

        File.Copy(filePath, candidate);
        return candidate;
    }

    private static string CreateHookBlock(string installDirectory)
    {
        var quotedDirectory = QuotePowerShellString(installDirectory);
        return string.Join(Environment.NewLine, new[]
        {
            BeginMarker,
            "$global:__ecodexLastCommand = $null",
            $"$ecodexBin = {quotedDirectory}",
            "$ecodexCli = Join-Path $ecodexBin 'ecodex.exe'",
            "if (Get-Command Set-PSReadLineOption -ErrorAction SilentlyContinue) {",
            "    Set-PSReadLineOption -AddToHistoryHandler {",
            "        param([string]$command)",
            "        $global:__ecodexLastCommand = $command",
            "        try { & $ecodexCli hook event --phase start --command $command --exit-code 0 --cwd (Get-Location).Path *> $null } catch {}",
            "        return $true",
            "    }",
            "}",
            "if (-not $global:__ecodexPromptWrapped) {",
            "    $global:__ecodexPromptWrapped = $true",
            "    $global:__ecodexPreviousPrompt = (Get-Command prompt -CommandType Function -ErrorAction SilentlyContinue).ScriptBlock",
            "    function global:prompt {",
            "        $exitCode = if ($global:LASTEXITCODE -is [int]) { $global:LASTEXITCODE } elseif ($?) { 0 } else { 1 }",
            "        if ($global:__ecodexLastCommand) {",
            "            try { & $ecodexCli hook event --phase end --command $global:__ecodexLastCommand --exit-code $exitCode --cwd (Get-Location).Path *> $null } catch {}",
            "            $global:__ecodexLastCommand = $null",
            "        }",
            "        if ($global:__ecodexPreviousPrompt) { & $global:__ecodexPreviousPrompt } else { \"PS $($executionContext.SessionState.Path.CurrentLocation)> \" }",
            "    }",
            "}",
            EndMarker,
        });
    }

    private static string UpsertMarkedBlock(string text, string block)
    {
        var withoutExisting = RemoveMarkedBlock(text);
        return string.IsNullOrWhiteSpace(withoutExisting)
            ? block
            : withoutExisting.TrimEnd() + Environment.NewLine + Environment.NewLine + block;
    }

    private static string RemoveMarkedBlock(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var pattern = $@"(?ms)^\s*{Regex.Escape(BeginMarker)}\s*$.*?^\s*{Regex.Escape(EndMarker)}\s*$\r?\n?";
        return Regex.Replace(text, pattern, "").TrimEnd('\r', '\n');
    }

    private static bool HasMarkerConflict(string text)
    {
        var beginCount = CountOccurrences(text, BeginMarker);
        var endCount = CountOccurrences(text, EndMarker);
        return beginCount != endCount || beginCount > 1;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
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

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
