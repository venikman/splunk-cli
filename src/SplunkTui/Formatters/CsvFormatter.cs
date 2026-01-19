using SplunkTui.Models;

namespace SplunkTui.Formatters;

/// <summary>
/// Formats events as CSV with proper escaping.
/// </summary>
public sealed class CsvFormatter : IEventFormatter
{
    public async Task<int> WriteAsync(
        TextWriter writer,
        IAsyncEnumerable<SplunkEvent[]> events,
        string[]? fields,
        ExportMetadata metadata,
        CancellationToken ct = default)
    {
        var count = 0;
        string[]? headerFields = null;
        var isFirstBatch = true;

        await foreach (var batch in events.WithCancellation(ct))
        {
            if (batch.Length == 0) continue;

            // Determine fields from first batch if not specified
            if (isFirstBatch)
            {
                headerFields = fields ?? DetermineFields(batch);
                await WriteHeaderAsync(writer, headerFields);
                isFirstBatch = false;
            }

            foreach (var evt in batch)
            {
                await WriteRowAsync(writer, evt, headerFields!);
                count++;
            }
        }

        return count;
    }

    private static string[] DetermineFields(SplunkEvent[] batch)
    {
        // Collect all unique field names from the batch
        var fieldSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var evt in batch)
        {
            foreach (var key in evt.Keys)
            {
                fieldSet.Add(key);
            }
        }

        // Sort fields, but put common Splunk fields first
        var commonFields = new[] { "_time", "_raw", "host", "source", "sourcetype", "index" };
        var orderedFields = commonFields
            .Where(c => fieldSet.Remove(c))
            .ToList();

        // Add remaining fields sorted alphabetically
        orderedFields.AddRange(fieldSet.OrderBy(f => f, StringComparer.OrdinalIgnoreCase));

        return orderedFields.ToArray();
    }

    private static async Task WriteHeaderAsync(TextWriter writer, string[] fields)
    {
        await writer.WriteLineAsync(string.Join(",", fields.Select(EscapeField)));
    }

    private static async Task WriteRowAsync(TextWriter writer, SplunkEvent evt, string[] fields)
    {
        var values = fields.Select(f => EscapeField(evt.TryGetValue(f, out var v) ? v : null));
        await writer.WriteLineAsync(string.Join(",", values));
    }

    /// <summary>
    /// Escapes a field value for CSV according to RFC 4180.
    /// </summary>
    public static string EscapeField(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        // Check if escaping is needed
        var needsQuotes = value.Contains('"') ||
                          value.Contains(',') ||
                          value.Contains('\n') ||
                          value.Contains('\r');

        if (!needsQuotes)
            return value;

        // Escape double quotes by doubling them, then wrap in quotes
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
