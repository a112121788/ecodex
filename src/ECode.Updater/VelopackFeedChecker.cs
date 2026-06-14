using System.Net.Http;
using System.Text.RegularExpressions;

namespace ECode.Updater;

public sealed record VelopackReleaseEntry(
    string FileName,
    string Version,
    long SizeBytes);

public sealed record UpdateCheckResult(
    string FeedUrl,
    string CurrentVersion,
    string? LatestVersion,
    string? PackageFile,
    bool UpdateAvailable,
    string? Error = null);

public sealed class VelopackFeedChecker
{
    private static readonly Regex VersionRegex = new(
        @"(?<!\d)(?<version>\d+\.\d+\.\d+)(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HttpClient _httpClient;

    public VelopackFeedChecker(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<UpdateCheckResult> CheckAsync(
        Uri feedUri,
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var releasesUri = ResolveReleasesUri(feedUri);
            var releasesText = await _httpClient.GetStringAsync(releasesUri, cancellationToken);
            var latest = TryGetLatestRelease(releasesText);

            if (latest == null)
            {
                return new UpdateCheckResult(
                    feedUri.ToString(),
                    currentVersion,
                    LatestVersion: null,
                    PackageFile: null,
                    UpdateAvailable: false,
                    Error: "No Velopack release entries found.");
            }

            return new UpdateCheckResult(
                feedUri.ToString(),
                currentVersion,
                latest.Version,
                latest.FileName,
                IsNewer(latest.Version, currentVersion));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return new UpdateCheckResult(
                feedUri.ToString(),
                currentVersion,
                LatestVersion: null,
                PackageFile: null,
                UpdateAvailable: false,
                Error: ex.Message);
        }
    }

    public static VelopackReleaseEntry? TryGetLatestRelease(string releasesText)
    {
        return ParseReleases(releasesText)
            .OrderByDescending(entry => ParseVersion(entry.Version))
            .ThenByDescending(entry => entry.Version, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static IReadOnlyList<VelopackReleaseEntry> ParseReleases(string releasesText)
    {
        var entries = new List<VelopackReleaseEntry>();
        using var reader = new StringReader(releasesText ?? "");

        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
                continue;

            var fileName = parts.FirstOrDefault(part => part.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
            if (fileName == null)
                continue;

            var match = VersionRegex.Match(fileName);
            if (!match.Success)
                continue;

            var size = parts
                .Select(part => long.TryParse(part, out var parsed) ? parsed : 0)
                .DefaultIfEmpty(0)
                .Max();

            entries.Add(new VelopackReleaseEntry(fileName, match.Groups["version"].Value, size));
        }

        return entries;
    }

    public static bool IsNewer(string candidateVersion, string currentVersion)
    {
        var candidate = ParseVersion(candidateVersion);
        var current = ParseVersion(currentVersion);
        return candidate.CompareTo(current) > 0;
    }

    public static Uri ResolveReleasesUri(Uri feedUri)
    {
        return feedUri.AbsolutePath.EndsWith("/RELEASES", StringComparison.OrdinalIgnoreCase)
            ? feedUri
            : new Uri(feedUri.ToString().TrimEnd('/') + "/RELEASES", UriKind.Absolute);
    }

    public static Uri ResolvePackageUri(Uri feedUri, string packageFile)
    {
        return new Uri(GetFeedRootUri(feedUri), packageFile);
    }

    public static Uri GetFeedRootUri(Uri feedUri)
    {
        var value = feedUri.ToString();
        if (value.EndsWith("/RELEASES", StringComparison.OrdinalIgnoreCase))
            value = value[..^"/RELEASES".Length];

        return new Uri(value.TrimEnd('/') + "/", UriKind.Absolute);
    }

    private static Version ParseVersion(string version)
    {
        var core = version.Split('-', '+')[0];
        return Version.TryParse(core, out var parsed) ? parsed : new Version(0, 0, 0);
    }
}
