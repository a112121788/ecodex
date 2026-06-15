namespace ECodeX.Core.Models;

public enum ShortRefKind
{
    Window,
    Workspace,
    Surface,
    Pane,
}

public readonly record struct ShortRef(ShortRefKind Kind, int Index)
{
    public override string ToString()
    {
        return $"{KindToPrefix(Kind)}:{Index}";
    }

    public static ShortRef Parse(string value)
    {
        return TryParse(value, out var reference)
            ? reference
            : throw new FormatException($"Invalid short ref: {value}");
    }

    public static bool TryParse(string? value, out ShortRef reference)
    {
        reference = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Trim().Split(':', 2);
        if (parts.Length != 2 ||
            !TryParseKind(parts[0], out var kind) ||
            !int.TryParse(parts[1], out var index) ||
            index < 1)
        {
            return false;
        }

        reference = new ShortRef(kind, index);
        return true;
    }

    public static string KindToPrefix(ShortRefKind kind)
    {
        return kind switch
        {
            ShortRefKind.Window => "window",
            ShortRefKind.Workspace => "workspace",
            ShortRefKind.Surface => "surface",
            ShortRefKind.Pane => "pane",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    public static bool TryParseKind(string? value, out ShortRefKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        kind = value.Trim().ToLowerInvariant() switch
        {
            "window" => ShortRefKind.Window,
            "workspace" => ShortRefKind.Workspace,
            "surface" => ShortRefKind.Surface,
            "pane" => ShortRefKind.Pane,
            _ => default,
        };

        return value.Trim().ToLowerInvariant() is "window" or "workspace" or "surface" or "pane";
    }
}

public sealed class ShortRefIndex
{
    private readonly Dictionary<ShortRef, string> _refToId;
    private readonly Dictionary<string, ShortRef> _idToRef;

    private ShortRefIndex(Dictionary<ShortRef, string> refToId, Dictionary<string, ShortRef> idToRef)
    {
        _refToId = refToId;
        _idToRef = idToRef;
    }

    public static ShortRefIndex FromIds(ShortRefKind kind, IEnumerable<string> ids, int startIndex = 1)
    {
        if (startIndex < 1)
            throw new ArgumentOutOfRangeException(nameof(startIndex), "Short ref indices are 1-based.");

        var refToId = new Dictionary<ShortRef, string>();
        var idToRef = new Dictionary<string, ShortRef>(StringComparer.Ordinal);
        var index = startIndex;
        foreach (var id in ids.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            if (idToRef.ContainsKey(id))
                continue;

            var reference = new ShortRef(kind, index);
            refToId[reference] = id;
            idToRef[id] = reference;
            index++;
        }

        return new ShortRefIndex(refToId, idToRef);
    }

    public bool TryResolve(ShortRef reference, out string id)
    {
        return _refToId.TryGetValue(reference, out id!);
    }

    public bool TryGetRef(string id, out ShortRef reference)
    {
        return _idToRef.TryGetValue(id, out reference);
    }

    public string Resolve(ShortRef reference)
    {
        return TryResolve(reference, out var id)
            ? id
            : throw new KeyNotFoundException($"Short ref not found: {reference}");
    }

    public ShortRef GetRef(string id)
    {
        return TryGetRef(id, out var reference)
            ? reference
            : throw new KeyNotFoundException($"UUID not found: {id}");
    }
}
