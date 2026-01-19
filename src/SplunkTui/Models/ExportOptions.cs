namespace SplunkTui.Models;

/// <summary>
/// Options for the export command, resolved from CLI args, env vars, and config.
/// </summary>
public sealed record ExportOptions
{
    // Connection
    public required string Url { get; init; }
    public required string Token { get; init; }
    public bool Insecure { get; init; }

    // Query
    public required string Query { get; init; }

    // Time range
    public string EarliestTime { get; init; } = "-1d";
    public string LatestTime { get; init; } = "now";

    // Size control
    public int MaxResults { get; init; } = 10_000;
    public int BatchSize { get; init; } = 10_000;

    // Output
    public OutputFormat Format { get; init; } = OutputFormat.Csv;
    public string? OutputPath { get; init; }
    public string[]? Fields { get; init; }
    public bool ShowProgress { get; init; }
}

public enum OutputFormat
{
    Csv,
    Json,
    Jsonl
}

public static class OutputFormatExtensions
{
    public static OutputFormat Parse(string value) => value.ToLowerInvariant() switch
    {
        "csv" => OutputFormat.Csv,
        "json" => OutputFormat.Json,
        "jsonl" => OutputFormat.Jsonl,
        _ => throw new ArgumentException($"Unknown format: {value}. Valid formats: csv, json, jsonl")
    };
}
