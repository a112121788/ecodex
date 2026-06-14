namespace ECode.Core.Services;

public enum CliIdFormat
{
    Refs,
    Uuids,
    Both,
}

public sealed record CliGlobalOptions(CliIdFormat IdFormat, bool Json)
{
    public static CliGlobalOptions DefaultHuman { get; } = new(CliIdFormat.Refs, Json: false);

    public static bool TryExtract(
        IReadOnlyList<string> args,
        out CliGlobalOptions options,
        out string[] remainingArgs,
        out string error)
    {
        var remaining = new List<string>();
        var json = false;
        CliIdFormat? explicitIdFormat = null;
        error = "";

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase))
            {
                json = true;
                continue;
            }

            if (arg.StartsWith("--id-format=", StringComparison.OrdinalIgnoreCase))
            {
                var value = arg["--id-format=".Length..];
                if (!TryParseIdFormat(value, out var parsed))
                {
                    options = DefaultHuman;
                    remainingArgs = [];
                    error = $"Invalid --id-format: {value}. Expected refs, uuids, or both.";
                    return false;
                }

                explicitIdFormat = parsed;
                continue;
            }

            if (string.Equals(arg, "--id-format", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count || args[i + 1].StartsWith('-'))
                {
                    options = DefaultHuman;
                    remainingArgs = [];
                    error = "Missing value for --id-format. Expected refs, uuids, or both.";
                    return false;
                }

                var value = args[++i];
                if (!TryParseIdFormat(value, out var parsed))
                {
                    options = DefaultHuman;
                    remainingArgs = [];
                    error = $"Invalid --id-format: {value}. Expected refs, uuids, or both.";
                    return false;
                }

                explicitIdFormat = parsed;
                continue;
            }

            remaining.Add(arg);
        }

        options = new CliGlobalOptions(explicitIdFormat ?? (json ? CliIdFormat.Both : CliIdFormat.Refs), json);
        remainingArgs = remaining.ToArray();
        return true;
    }

    public Dictionary<string, string> ToPipeArgs()
    {
        return new Dictionary<string, string>
        {
            ["idFormat"] = FormatIdFormat(IdFormat),
            ["json"] = Json ? "true" : "false",
        };
    }

    public static bool TryParseIdFormat(string? value, out CliIdFormat idFormat)
    {
        idFormat = value?.Trim().ToLowerInvariant() switch
        {
            "refs" => CliIdFormat.Refs,
            "uuids" => CliIdFormat.Uuids,
            "both" => CliIdFormat.Both,
            _ => default,
        };

        return value?.Trim().ToLowerInvariant() is "refs" or "uuids" or "both";
    }

    public static string FormatIdFormat(CliIdFormat idFormat)
    {
        return idFormat switch
        {
            CliIdFormat.Refs => "refs",
            CliIdFormat.Uuids => "uuids",
            CliIdFormat.Both => "both",
            _ => throw new ArgumentOutOfRangeException(nameof(idFormat), idFormat, null),
        };
    }
}
