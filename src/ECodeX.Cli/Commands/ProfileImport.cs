using System.Text.Json;
using System.Text.Json.Nodes;

namespace ECodeX.Cli.Commands;

public sealed record ProfileImportOptions(
    string ProfileName = "ECodeX Shell",
    string ProfileGuid = "{7f4f7d8d-7a1f-45f3-b0c7-ec0de0000001}",
    string CommandLine = "pwsh.exe -NoLogo",
    string StartingDirectory = "%USERPROFILE%",
    string ColorSchemeName = "ECodeX Dark",
    string FontFace = "Cascadia Mono",
    double FontSize = 11);

public sealed record ProfileImportPlan(
    string SettingsJson,
    bool ProfileAdded,
    bool ProfileUpdated,
    bool SchemeAdded,
    bool SchemeUpdated);

public static class ProfileImport
{
    private const string WindowsTerminalPackageName = "Microsoft.WindowsTerminal_8wekyb3d8bbwe";
    private const string WindowsTerminalPreviewPackageName = "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public static ProfileImportPlan CreateImportPlan(string? settingsJson, ProfileImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var root = ParseSettings(settingsJson);
        var profiles = EnsureObject(root, "profiles");
        var profileList = EnsureArray(profiles, "list");
        var schemes = EnsureArray(root, "schemes");

        var (schemeAdded, schemeUpdated) = UpsertScheme(schemes, options.ColorSchemeName);
        var (profileAdded, profileUpdated) = UpsertProfile(profileList, options);

        return new ProfileImportPlan(
            root.ToJsonString(SerializerOptions),
            profileAdded,
            profileUpdated,
            schemeAdded,
            schemeUpdated);
    }

    public static string GetDefaultSettingsPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var stablePath = Path.Combine(
            localAppData,
            "Packages",
            WindowsTerminalPackageName,
            "LocalState",
            "settings.json");

        if (File.Exists(stablePath))
            return stablePath;

        var previewPath = Path.Combine(
            localAppData,
            "Packages",
            WindowsTerminalPreviewPackageName,
            "LocalState",
            "settings.json");

        if (File.Exists(previewPath))
            return previewPath;

        return Path.Combine(localAppData, "Microsoft", "Windows Terminal", "settings.json");
    }

    private static JsonObject ParseSettings(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
            return [];

        var parseOptions = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        };

        var node = JsonNode.Parse(settingsJson, documentOptions: parseOptions);
        return node as JsonObject
            ?? throw new InvalidOperationException("Windows Terminal settings root must be a JSON object.");
    }

    private static JsonObject EnsureObject(JsonObject root, string propertyName)
    {
        if (root[propertyName] is JsonObject existing)
            return existing;

        if (root[propertyName] is JsonArray legacyProfiles && propertyName == "profiles")
        {
            var migrated = new JsonObject
            {
                ["list"] = legacyProfiles.DeepClone(),
            };
            root[propertyName] = migrated;
            return migrated;
        }

        var created = new JsonObject();
        root[propertyName] = created;
        return created;
    }

    private static JsonArray EnsureArray(JsonObject root, string propertyName)
    {
        if (root[propertyName] is JsonArray existing)
            return existing;

        var created = new JsonArray();
        root[propertyName] = created;
        return created;
    }

    private static (bool Added, bool Updated) UpsertProfile(JsonArray profileList, ProfileImportOptions options)
    {
        var profile = FindProfile(profileList, options.ProfileGuid, options.ProfileName);
        var added = profile == null;
        profile ??= new JsonObject();

        var before = profile.DeepClone().ToJsonString();
        profile["guid"] = options.ProfileGuid;
        profile["name"] = options.ProfileName;
        profile["commandline"] = options.CommandLine;
        profile["startingDirectory"] = options.StartingDirectory;
        profile["colorScheme"] = options.ColorSchemeName;
        profile["hidden"] = false;

        var font = profile["font"] as JsonObject ?? new JsonObject();
        font["face"] = options.FontFace;
        font["size"] = options.FontSize;
        profile["font"] = font;

        if (added)
            profileList.Add(profile);

        var updated = !added && !string.Equals(before, profile.ToJsonString(), StringComparison.Ordinal);
        return (added, updated);
    }

    private static JsonObject? FindProfile(JsonArray profileList, string guid, string profileName)
    {
        foreach (var item in profileList)
        {
            if (item is not JsonObject profile)
                continue;

            var existingGuid = profile["guid"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(existingGuid)
                && string.Equals(existingGuid, guid, StringComparison.OrdinalIgnoreCase))
                return profile;

            var existingName = profile["name"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(existingName)
                && string.Equals(existingName, profileName, StringComparison.OrdinalIgnoreCase))
                return profile;
        }

        return null;
    }

    private static (bool Added, bool Updated) UpsertScheme(JsonArray schemes, string schemeName)
    {
        var scheme = FindScheme(schemes, schemeName);
        if (scheme != null)
            return (false, false);

        schemes.Add(CreateDefaultScheme(schemeName));
        return (true, false);
    }

    private static JsonObject? FindScheme(JsonArray schemes, string schemeName)
    {
        foreach (var item in schemes)
        {
            if (item is not JsonObject scheme)
                continue;

            var existingName = scheme["name"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(existingName)
                && string.Equals(existingName, schemeName, StringComparison.OrdinalIgnoreCase))
                return scheme;
        }

        return null;
    }

    private static JsonObject CreateDefaultScheme(string schemeName) => new()
    {
        ["name"] = schemeName,
        ["background"] = "#101418",
        ["foreground"] = "#D7DEE8",
        ["cursorColor"] = "#8BD5CA",
        ["selectionBackground"] = "#264653",
        ["black"] = "#101418",
        ["red"] = "#FF6B6B",
        ["green"] = "#95D5B2",
        ["yellow"] = "#FFD166",
        ["blue"] = "#7AA2F7",
        ["purple"] = "#C792EA",
        ["cyan"] = "#8BD5CA",
        ["white"] = "#D7DEE8",
        ["brightBlack"] = "#5C6773",
        ["brightRed"] = "#FF8787",
        ["brightGreen"] = "#B7E4C7",
        ["brightYellow"] = "#FFE08A",
        ["brightBlue"] = "#A5BEFA",
        ["brightPurple"] = "#D6A2F0",
        ["brightCyan"] = "#B2F7EF",
        ["brightWhite"] = "#F8FAFC",
    };
}
