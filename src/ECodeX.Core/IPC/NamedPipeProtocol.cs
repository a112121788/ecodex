using System.Text;
using System.Text.Json;
using ECodeX.Core.IPC.V2;

namespace ECodeX.Core.IPC;

public enum NamedPipeProtocolKind
{
    V1,
    V2,
}

public sealed record NamedPipeV1Request(string Command, IReadOnlyDictionary<string, string> Args);

public sealed record NamedPipeIncomingRequest(
    NamedPipeProtocolKind Kind,
    NamedPipeV1Request? V1,
    V2Request? V2,
    V2Response? V2ErrorResponse);

public static class NamedPipeProtocol
{
    public static NamedPipeIncomingRequest ParseFirstLine(string requestLine)
    {
        var trimmed = requestLine.Trim();
        if (trimmed.StartsWith('{'))
        {
            var parsed = V2Protocol.ParseRequest(trimmed);
            return new NamedPipeIncomingRequest(
                NamedPipeProtocolKind.V2,
                V1: null,
                V2: parsed.Request,
                V2ErrorResponse: parsed.ErrorResponse);
        }

        var parts = requestLine.Split(' ', 2);
        var command = parts[0].ToUpperInvariant();
        var args = new Dictionary<string, string>();
        if (parts.Length > 1)
            ParseArgs(parts[1], args);

        return new NamedPipeIncomingRequest(
            NamedPipeProtocolKind.V1,
            V1: new NamedPipeV1Request(command, args),
            V2: null,
            V2ErrorResponse: null);
    }

    public static void ParseArgs(string argsString, Dictionary<string, string> args)
    {
        var trimmed = argsString.Trim();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                var json = JsonSerializer.Deserialize<Dictionary<string, string>>(trimmed);
                if (json != null)
                {
                    foreach (var kvp in json)
                        args[kvp.Key] = kvp.Value;
                    return;
                }
            }
            catch
            {
                // 回退到 key=value 解析。
            }
        }

        foreach (var part in SplitRespectingQuotes(trimmed))
        {
            int eq = part.IndexOf('=');
            if (eq > 0)
            {
                var key = part[..eq];
                var value = part[(eq + 1)..].Trim('"', '\'');
                args[key] = value;
            }
            else
            {
                args.TryAdd("_arg" + args.Count, part);
            }
        }
    }

    private static IEnumerable<string> SplitRespectingQuotes(string input)
    {
        var current = new StringBuilder();
        bool inQuote = false;
        char quoteChar = '\0';

        foreach (var c in input)
        {
            if (inQuote)
            {
                if (c == quoteChar)
                    inQuote = false;
                else
                    current.Append(c);
            }
            else if (c is '"' or '\'')
            {
                inQuote = true;
                quoteChar = c;
            }
            else if (c == ' ')
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            yield return current.ToString();
    }
}
