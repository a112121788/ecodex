using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ECodeX.Core.Services;

public sealed record ResumeProcessInfo(
    string ImageName,
    int ProcessId,
    string? SessionName,
    string? WindowTitle);

public sealed record ResumeProcessDetection(
    IReadOnlyList<ResumeProcessInfo> Processes,
    bool HasTmux,
    bool HasKnownShell,
    string? MatchingShellPath);

/// <summary>
/// 解析 Windows tasklist 输出，辅助判断 resume binding 是否可能还有可恢复进程。
/// </summary>
public static class ResumeProcessDetector
{
    public static ResumeProcessDetection DetectFromTaskListCsv(string taskListCsv, IEnumerable<string>? shellPaths = null)
    {
        var processes = ParseTaskListCsv(taskListCsv);
        var shellFileNames = (shellPaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => new
            {
                Path = path,
                FileName = Path.GetFileName(path.Trim()).ToLowerInvariant(),
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.FileName))
            .ToList();

        var hasTmux = processes.Any(IsTmuxProcess);
        string? matchingShell = null;
        var hasKnownShell = processes.Any(process =>
        {
            var imageName = process.ImageName.ToLowerInvariant();
            var match = shellFileNames.FirstOrDefault(shell =>
                string.Equals(imageName, shell.FileName, StringComparison.OrdinalIgnoreCase) ||
                (process.WindowTitle?.Contains(shell.Path, StringComparison.OrdinalIgnoreCase) ?? false));

            if (match != null)
            {
                matchingShell ??= match.Path;
                return true;
            }

            return IsCommonShellImage(imageName);
        });

        return new ResumeProcessDetection(processes, hasTmux, hasKnownShell, matchingShell);
    }

    public static ResumeProcessDetection DetectCurrentMachine(IEnumerable<string>? shellPaths = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new ResumeProcessDetection([], HasTmux: false, HasKnownShell: false, MatchingShellPath: null);

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "tasklist.exe",
                Arguments = "/fo csv /v",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process == null)
                return new ResumeProcessDetection([], HasTmux: false, HasKnownShell: false, MatchingShellPath: null);

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            return DetectFromTaskListCsv(output, shellPaths);
        }
        catch
        {
            return new ResumeProcessDetection([], HasTmux: false, HasKnownShell: false, MatchingShellPath: null);
        }
    }

    public static IReadOnlyList<ResumeProcessInfo> ParseTaskListCsv(string taskListCsv)
    {
        if (string.IsNullOrWhiteSpace(taskListCsv))
            return [];

        var rows = taskListCsv
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseCsvRow)
            .Where(row => row.Count >= 2)
            .ToList();

        if (rows.Count == 0)
            return [];

        var header = rows[0].Select(h => h.Trim()).ToList();
        var imageIndex = FindColumn(header, "Image Name", fallback: 0);
        var pidIndex = FindColumn(header, "PID", fallback: 1);
        var sessionIndex = FindColumn(header, "Session Name", fallback: -1);
        var titleIndex = FindColumn(header, "Window Title", fallback: -1);

        return rows.Skip(1)
            .Select(row => CreateProcessInfo(row, imageIndex, pidIndex, sessionIndex, titleIndex))
            .Where(info => info != null)
            .Cast<ResumeProcessInfo>()
            .ToList();
    }

    private static ResumeProcessInfo? CreateProcessInfo(
        IReadOnlyList<string> row,
        int imageIndex,
        int pidIndex,
        int sessionIndex,
        int titleIndex)
    {
        if (!TryGet(row, imageIndex, out var imageName) ||
            !TryGet(row, pidIndex, out var pidText) ||
            !int.TryParse(pidText, out var pid))
        {
            return null;
        }

        TryGet(row, sessionIndex, out var sessionName);
        TryGet(row, titleIndex, out var windowTitle);
        return new ResumeProcessInfo(imageName, pid, sessionName, windowTitle);
    }

    private static bool IsTmuxProcess(ResumeProcessInfo process)
    {
        return string.Equals(process.ImageName, "tmux.exe", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(process.ImageName, "tmux", StringComparison.OrdinalIgnoreCase) ||
               (process.WindowTitle?.Contains("tmux", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool IsCommonShellImage(string imageName)
    {
        return imageName is "pwsh.exe" or "powershell.exe" or "cmd.exe" or "bash.exe" or "wsl.exe" or
            "zsh.exe" or "fish.exe" or "nu.exe";
    }

    private static int FindColumn(IReadOnlyList<string> header, string name, int fallback)
    {
        for (var i = 0; i < header.Count; i++)
        {
            if (string.Equals(header[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return fallback;
    }

    private static bool TryGet(IReadOnlyList<string> row, int index, out string value)
    {
        if (index >= 0 && index < row.Count)
        {
            value = row[index];
            return !string.IsNullOrWhiteSpace(value);
        }

        value = "";
        return false;
    }

    private static List<string> ParseCsvRow(string row)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < row.Length; i++)
        {
            var ch = row[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < row.Length && row[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                cells.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        cells.Add(current.ToString());
        return cells;
    }
}
