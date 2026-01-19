using System.Text.Json;
using SplunkTui.Models;

namespace SplunkTui.Formatters;

/// <summary>
/// Formats events as pretty-printed JSON with metadata wrapper.
/// Note: JSON format with metadata requires knowing the count, so events are collected first.
/// For large exports, use JSONL format which streams without buffering.
/// </summary>
public sealed class JsonFormatter : IEventFormatter
{
    public async Task<int> WriteAsync(
        TextWriter writer,
        IAsyncEnumerable<SplunkEvent[]> events,
        string[]? fields,
        ExportMetadata metadata,
        CancellationToken ct = default)
    {
        // Collect events (JSON with metadata requires count upfront)
        // For large exports, users should use JSONL format which streams
        var allEvents = new List<Dictionary<string, string?>>();

        await foreach (var batch in events.WithCancellation(ct))
        {
            foreach (var evt in batch)
            {
                var filtered = FormatterUtils.FilterFields(evt, fields);
                allEvents.Add(filtered);
            }
        }

        // Stream write using Utf8JsonWriter for efficiency
        using var stream = new MemoryStream();
        await using (var jsonWriter = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            jsonWriter.WriteStartObject();

            // Write metadata
            jsonWriter.WritePropertyName("meta");
            jsonWriter.WriteStartObject();
            jsonWriter.WriteString("query", metadata.Query);
            jsonWriter.WriteString("from", metadata.From);
            jsonWriter.WriteString("to", metadata.To);
            jsonWriter.WriteNumber("count", allEvents.Count);
            jsonWriter.WriteString("exported_at", metadata.ExportedAt.ToString("O"));
            jsonWriter.WriteEndObject();

            // Write results array
            jsonWriter.WritePropertyName("results");
            jsonWriter.WriteStartArray();

            foreach (var evt in allEvents)
            {
                jsonWriter.WriteStartObject();
                foreach (var kvp in evt)
                {
                    jsonWriter.WriteString(kvp.Key, kvp.Value);
                }
                jsonWriter.WriteEndObject();
            }

            jsonWriter.WriteEndArray();
            jsonWriter.WriteEndObject();
        }

        // Write to output
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync(ct);
        await writer.WriteAsync(json);

        return allEvents.Count;
    }
}
