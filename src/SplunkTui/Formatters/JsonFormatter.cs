using System.Text.Json;
using SplunkTui.Models;

namespace SplunkTui.Formatters;

/// <summary>
/// Formats events as pretty-printed JSON with metadata wrapper.
/// </summary>
public sealed class JsonFormatter : IEventFormatter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<int> WriteAsync(
        TextWriter writer,
        IAsyncEnumerable<SplunkEvent[]> events,
        string[]? fields,
        ExportMetadata metadata,
        CancellationToken ct = default)
    {
        // Collect all events (JSON format requires complete structure)
        var allEvents = new List<Dictionary<string, string?>>();

        await foreach (var batch in events.WithCancellation(ct))
        {
            foreach (var evt in batch)
            {
                var filtered = FilterFields(evt, fields);
                allEvents.Add(filtered);
            }
        }

        // Build the output structure
        var output = new
        {
            meta = new
            {
                query = metadata.Query,
                from = metadata.From,
                to = metadata.To,
                count = allEvents.Count,
                exported_at = metadata.ExportedAt.ToString("O")
            },
            results = allEvents
        };

        var json = JsonSerializer.Serialize(output, Options);
        await writer.WriteAsync(json);

        return allEvents.Count;
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
