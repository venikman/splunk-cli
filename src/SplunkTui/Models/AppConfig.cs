namespace SplunkTui.Models;

/// <summary>
/// Root configuration model matching ~/.splunk-tui.json structure.
/// </summary>
public sealed record AppConfig
{
    public ConnectionConfig Connection { get; init; } = new();
    public DefaultsConfig Defaults { get; init; } = new();
    public List<SavedSearch> SavedSearches { get; init; } = [];
    public List<string> History { get; init; } = [];

    public static AppConfig Empty => new();
}

public sealed record ConnectionConfig
{
    public string? Url { get; init; }
    public string? Token { get; init; }
    public bool Insecure { get; init; }
}

public sealed record DefaultsConfig
{
    public string TimeRange { get; init; } = "-1d";
    public int MaxResults { get; init; } = 10_000;
    public int BatchSize { get; init; } = 10_000;
    public string Format { get; init; } = "csv";
}

public sealed record SavedSearch
{
    public required string Name { get; init; }
    public required string Query { get; init; }
    public string? TimeRange { get; init; }
}
