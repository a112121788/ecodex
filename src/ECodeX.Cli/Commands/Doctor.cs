using System.Text.Json;
using ECodeX.Core.Config;
using ECodeX.Core.IPC;
using ECodeX.Core.IPC.V2;

namespace ECodeX.Cli.Commands;

public sealed record DoctorCheck(
    string Name,
    string Status,
    string Detail,
    bool Ok);

public sealed record DoctorReport(IReadOnlyList<DoctorCheck> Checks)
{
    public bool Ok => Checks.All(check => check.Ok);
}

public sealed record DoctorSnapshot(
    bool IsWindows,
    Version OsVersion,
    string PathValue,
    string AppDirectory,
    string AppDataDirectory,
    bool AppDataDirectoryExists,
    bool WebView2Available,
    string WebView2Detail,
    bool DaemonAvailable,
    string DaemonDetail);

public static class Doctor
{
    private static readonly Version MinimumConPtyVersion = new(10, 0, 17763);
    private static readonly char[] PathSeparators = [';'];

    public static async Task<DoctorReport> RunAsync(int daemonTimeoutMs = 700)
    {
        var webView2 = DetectWebView2Runtime();
        var daemon = await CheckDaemonAsync(daemonTimeoutMs);
        var appDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var appDataDirectory = CompatibilityOptions.GetAppDataDir();

        return CreateReport(new DoctorSnapshot(
            IsWindows: OperatingSystem.IsWindows(),
            OsVersion: Environment.OSVersion.Version,
            PathValue: Environment.GetEnvironmentVariable("PATH") ?? "",
            AppDirectory: appDirectory,
            AppDataDirectory: appDataDirectory,
            AppDataDirectoryExists: Directory.Exists(appDataDirectory),
            WebView2Available: webView2.Available,
            WebView2Detail: webView2.Detail,
            DaemonAvailable: daemon.Available,
            DaemonDetail: daemon.Detail));
    }

    public static DoctorReport CreateReport(DoctorSnapshot snapshot)
    {
        return new DoctorReport([
            CheckConPty(snapshot),
            CheckWebView2(snapshot),
            CheckPath(snapshot),
            CheckDaemon(snapshot),
            CheckAppDataDirectory(snapshot),
        ]);
    }

    public static string FormatHuman(DoctorReport report)
    {
        var lines = new List<string>
        {
            "ECodeX doctor",
        };

        foreach (var check in report.Checks)
            lines.Add($"[{check.Status}] {check.Name}: {check.Detail}");

        lines.Add(report.Ok ? "Overall: ok" : "Overall: attention needed");
        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatJson(DoctorReport report)
    {
        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }

    private static DoctorCheck CheckConPty(DoctorSnapshot snapshot)
    {
        if (!snapshot.IsWindows)
            return new DoctorCheck("conpty", "fail", "ConPTY requires Windows 10 1809 or newer.", false);

        var ok = snapshot.OsVersion >= MinimumConPtyVersion;
        return new DoctorCheck(
            "conpty",
            ok ? "ok" : "fail",
            ok
                ? $"Windows {snapshot.OsVersion} supports ConPTY."
                : $"Windows {snapshot.OsVersion} is older than required {MinimumConPtyVersion}.",
            ok);
    }

    private static DoctorCheck CheckWebView2(DoctorSnapshot snapshot)
    {
        return new DoctorCheck(
            "webview2",
            snapshot.WebView2Available ? "ok" : "warn",
            snapshot.WebView2Detail,
            true);
    }

    private static DoctorCheck CheckPath(DoctorSnapshot snapshot)
    {
        var inPath = SplitPath(snapshot.PathValue)
            .Any(entry => SamePath(entry, snapshot.AppDirectory));

        return new DoctorCheck(
            "path",
            inPath ? "ok" : "warn",
            inPath
                ? $"CLI directory is on PATH: {snapshot.AppDirectory}"
                : $"CLI directory is not on PATH: {snapshot.AppDirectory}",
            true);
    }

    private static DoctorCheck CheckDaemon(DoctorSnapshot snapshot)
    {
        return new DoctorCheck(
            "daemon",
            snapshot.DaemonAvailable ? "ok" : "warn",
            snapshot.DaemonDetail,
            true);
    }

    private static DoctorCheck CheckAppDataDirectory(DoctorSnapshot snapshot)
    {
        return new DoctorCheck(
            "config",
            snapshot.AppDataDirectoryExists ? "ok" : "warn",
            snapshot.AppDataDirectoryExists
                ? $"Runtime/config directory exists: {snapshot.AppDataDirectory}"
                : $"Runtime/config directory does not exist yet: {snapshot.AppDataDirectory}",
            true);
    }

    private static async Task<(bool Available, string Detail)> CheckDaemonAsync(int timeoutMs)
    {
        try
        {
            var response = await NamedPipeClient.SendV2Request(new V2Request
            {
                Id = JsonSerializer.SerializeToElement("doctor"),
                Method = "health",
                Params = JsonSerializer.SerializeToElement(new Dictionary<string, string>()),
            }, timeoutMs: timeoutMs);

            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("result", out var result)
                && result.TryGetProperty("ok", out var ok)
                && ok.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return (ok.GetBoolean(), ok.GetBoolean()
                    ? "Main app pipe responded to health check."
                    : "Main app pipe responded, but health is not ok.");
            }

            return (true, "Main app pipe responded.");
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or OperationCanceledException)
        {
            return (false, "Main app pipe did not respond; start ecodex-app.exe if CLI control is needed.");
        }
        catch (Exception ex)
        {
            return (false, $"Main app pipe health check failed: {ex.Message}");
        }
    }

    private static (bool Available, string Detail) DetectWebView2Runtime()
    {
        if (!OperatingSystem.IsWindows())
            return (false, "WebView2 Runtime is only available on Windows.");

        var candidates = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        };

        foreach (var root in candidates.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var applicationDir = Path.Combine(root, "Microsoft", "EdgeWebView", "Application");
            var exe = FindWebView2Executable(applicationDir);
            if (exe != null)
                return (true, $"WebView2 Runtime found: {exe}");
        }

        return (false, "WebView2 Runtime was not found; install Microsoft Edge WebView2 Runtime for browser surfaces.");
    }

    private static string? FindWebView2Executable(string applicationDir)
    {
        if (!Directory.Exists(applicationDir))
            return null;

        var direct = Path.Combine(applicationDir, "msedgewebview2.exe");
        if (File.Exists(direct))
            return direct;

        foreach (var directory in Directory.EnumerateDirectories(applicationDir))
        {
            var candidate = Path.Combine(directory, "msedgewebview2.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> SplitPath(string pathValue)
    {
        return pathValue.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool SamePath(string left, string right)
    {
        return string.Equals(
            NormalizePath(left),
            NormalizePath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().Trim('"').TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
