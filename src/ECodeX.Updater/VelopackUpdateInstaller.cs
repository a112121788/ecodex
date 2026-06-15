using System.Diagnostics;

namespace ECodeX.Updater;

public sealed record VelopackInstallPlan(
    Uri FeedUri,
    Uri SetupUri,
    string DownloadDirectory,
    string SetupFileName,
    bool Silent,
    bool WaitForExit)
{
    public string SetupPath => Path.Combine(DownloadDirectory, SetupFileName);
    public IReadOnlyList<string> Arguments => Silent ? ["--silent"] : [];
}

public sealed record VelopackInstallResult(
    string SetupPath,
    int? ProcessId,
    int? ExitCode);

public sealed class VelopackUpdateInstaller
{
    private readonly HttpClient _httpClient;

    public VelopackUpdateInstaller(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public static VelopackInstallPlan CreatePlan(
        Uri feedUri,
        string packId,
        string downloadDirectory,
        bool silent = true,
        bool waitForExit = false,
        Uri? setupUri = null)
    {
        var setupFileName = setupUri == null
            ? $"{packId}-Setup.exe"
            : Path.GetFileName(setupUri.LocalPath);

        return new VelopackInstallPlan(
            feedUri,
            setupUri ?? new Uri(VelopackFeedChecker.GetFeedRootUri(feedUri), setupFileName),
            downloadDirectory,
            setupFileName,
            silent,
            waitForExit);
    }

    public async Task<string> DownloadSetupAsync(
        VelopackInstallPlan plan,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(plan.DownloadDirectory);
        await using var source = await _httpClient.GetStreamAsync(plan.SetupUri, cancellationToken);
        await using var destination = File.Create(plan.SetupPath);
        await source.CopyToAsync(destination, cancellationToken);
        return plan.SetupPath;
    }

    public VelopackInstallResult StartInstaller(VelopackInstallPlan plan)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = plan.SetupPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        foreach (var argument in plan.Arguments)
            startInfo.ArgumentList.Add(argument);

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start Velopack setup.");

        if (!plan.WaitForExit)
            return new VelopackInstallResult(plan.SetupPath, process.Id, ExitCode: null);

        process.WaitForExit();
        return new VelopackInstallResult(plan.SetupPath, process.Id, process.ExitCode);
    }
}
