using System.Globalization;

namespace ECodex.Core.Services;

public sealed class FailureLoopDaemonLogProvider
{
    public FailureLoopDaemonLogInput? ParseLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var fields = ParseFields(line);
        if (!fields.TryGetValue("ts", out var rawTimestamp) ||
            !DateTimeOffset.TryParse(
                rawTimestamp,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
        {
            return null;
        }

        fields.TryGetValue("paneId", out var paneId);
        return new FailureLoopDaemonLogInput(timestamp.ToUniversalTime(), line, NormalizeLogValue(paneId));
    }

    public IReadOnlyList<FailureLoopDaemonLogInput> ParseLines(
        IEnumerable<string> lines,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? paneId = null)
    {
        ArgumentNullException.ThrowIfNull(lines);

        return lines
            .Select(ParseLine)
            .Where(input => input != null)
            .Cast<FailureLoopDaemonLogInput>()
            .Where(input => IsWithinWindow(input.TimestampUtc, fromUtc, toUtc))
            .Where(input => MatchesPane(paneId, input.PaneId))
            .ToArray();
    }

    public IReadOnlyList<FailureLoopDaemonLogInput> ReadTail(
        string filePath,
        int maxLines,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? paneId = null)
    {
        if (string.IsNullOrWhiteSpace(filePath) || maxLines <= 0 || !File.Exists(filePath))
            return [];

        var tail = new Queue<string>(maxLines);
        foreach (var line in File.ReadLines(filePath))
        {
            if (tail.Count == maxLines)
                tail.Dequeue();

            tail.Enqueue(line);
        }

        return ParseLines(tail, fromUtc, toUtc, paneId);
    }

    private static Dictionary<string, string?> ParseFields(string line)
    {
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
        var index = 0;

        while (index < line.Length)
        {
            SkipWhitespace(line, ref index);
            if (index >= line.Length)
                break;

            var keyStart = index;
            while (index < line.Length && line[index] != '=' && !char.IsWhiteSpace(line[index]))
                index++;

            if (index >= line.Length || line[index] != '=')
                break;

            var key = line[keyStart..index];
            index++;
            fields[key] = ReadValue(line, ref index);
        }

        return fields;
    }

    private static string? ReadValue(string line, ref int index)
    {
        if (index >= line.Length)
            return string.Empty;

        if (line[index] != '"')
        {
            var valueStart = index;
            while (index < line.Length && !char.IsWhiteSpace(line[index]))
                index++;

            return line[valueStart..index];
        }

        index++;
        var value = new System.Text.StringBuilder();
        while (index < line.Length)
        {
            var ch = line[index++];
            if (ch == '"')
                break;

            if (ch == '\\' && index < line.Length)
            {
                value.Append(line[index++] switch
                {
                    'r' => '\r',
                    'n' => '\n',
                    '"' => '"',
                    '\\' => '\\',
                    var escaped => escaped,
                });
                continue;
            }

            value.Append(ch);
        }

        return value.ToString();
    }

    private static void SkipWhitespace(string line, ref int index)
    {
        while (index < line.Length && char.IsWhiteSpace(line[index]))
            index++;
    }

    private static bool IsWithinWindow(DateTimeOffset timestampUtc, DateTimeOffset? fromUtc, DateTimeOffset? toUtc)
    {
        if (fromUtc is not null && timestampUtc < fromUtc.Value)
            return false;

        if (toUtc is not null && timestampUtc > toUtc.Value)
            return false;

        return true;
    }

    private static bool MatchesPane(string? expectedPaneId, string? actualPaneId)
        => string.IsNullOrWhiteSpace(expectedPaneId) ||
           string.Equals(expectedPaneId, actualPaneId, StringComparison.Ordinal);

    private static string? NormalizeLogValue(string? value)
        => string.IsNullOrWhiteSpace(value) || value == "-" ? null : value;
}
