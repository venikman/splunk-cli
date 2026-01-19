using System.Text.Json;
using SplunkTui.Models;

namespace SplunkTui.Formatters;

/// <summary>
/// Formats events as JSON Lines (one JSON object per line, no wrapper).
/// This format streams efficiently without buffering all events.
/// </summary>
public sealed class JsonlFormatter : IEventFormatter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,  // No indentation for JSONL
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<int> WriteAsync(
        TextWriter writer,
        IAsyncEnumerable<SplunkEvent[]> events,
        string[]? fields,
        ExportMetadata metadata,
        CancellationToken ct = default)
    {
        var count = 0;

        await foreach (var batch in events.WithCancellation(ct))
        {
            foreach (var evt in batch)
            {
                var filtered = FormatterUtils.FilterFields(evt, fields);
                var json = JsonSerializer.Serialize(filtered, Options);
                await writer.WriteLineAsync(json);
                count++;
            }
        }

        return count;
    }
}
