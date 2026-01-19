using System.Text.Json.Serialization;

namespace SplunkTui.Models;

/// <summary>
/// Represents a single Splunk event with dynamic fields.
/// </summary>
public sealed class SplunkEvent : Dictionary<string, string?>
{
    public SplunkEvent() : base(StringComparer.OrdinalIgnoreCase) { }

    public SplunkEvent(IDictionary<string, string?> dictionary)
        : base(dictionary, StringComparer.OrdinalIgnoreCase) { }

    // Common Splunk fields with typed accessors
    public string? Time => TryGetValue("_time", out var v) ? v : null;
    public string? Raw => TryGetValue("_raw", out var v) ? v : null;
    public string? Host => TryGetValue("host", out var v) ? v : null;
    public string? Source => TryGetValue("source", out var v) ? v : null;
    public string? SourceType => TryGetValue("sourcetype", out var v) ? v : null;
    public string? Index => TryGetValue("index", out var v) ? v : null;
}

/// <summary>
/// Metadata about an export operation.
/// </summary>
public sealed record ExportMetadata
{
    public required string Query { get; init; }
    public required string From { get; init; }
    public required string To { get; init; }
    public required int Count { get; init; }
    public required DateTimeOffset ExportedAt { get; init; }
}
