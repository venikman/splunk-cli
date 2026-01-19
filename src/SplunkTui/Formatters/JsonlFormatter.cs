using System.Text.Json;
using SplunkTui.Models;

namespace SplunkTui.Formatters;

/// <summary>
/// Formats events as JSON Lines (one JSON object per line, no wrapper).
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
                var filtered = FilterFields(evt, fields);
                var json = JsonSerializer.Serialize(filtered, Options);
                await writer.WriteLineAsync(json);
                count++;
            }
        }

        return count;
    }

    private static Dictionary<string, string?> FilterFields(SplunkEvent evt, string[]? fields)
    {
        if (fields == null || fields.Length == 0)
        {
            return new Dictionary<string, string?>(evt);
        }

        var filtered = new Dictionary<string, string?>();
        foreach (var field in fields)
        {
            if (evt.TryGetValue(field, out var value))
            {
                filtered[field] = value;
            }
        }
        return filtered;
    }
}
