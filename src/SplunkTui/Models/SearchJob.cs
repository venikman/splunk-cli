namespace SplunkTui.Models;

/// <summary>
/// Represents a Splunk search job.
/// </summary>
public sealed record SearchJob
{
    public required string Sid { get; init; }
    public SearchJobState State { get; init; }
    public int EventCount { get; init; }
    public int ResultCount { get; init; }
    public double DoneProgress { get; init; }
    public string? FailureReason { get; init; }
}

public enum SearchJobState
{
    Queued,
    Parsing,
    Running,
    Finalizing,
    Done,
    Failed,
    Paused
}

public static class SearchJobStateExtensions
{
    public static SearchJobState Parse(string? value) => value?.ToUpperInvariant() switch
    {
        "QUEUED" => SearchJobState.Queued,
        "PARSING" => SearchJobState.Parsing,
        "RUNNING" => SearchJobState.Running,
        "FINALIZING" => SearchJobState.Finalizing,
        "DONE" => SearchJobState.Done,
        "FAILED" => SearchJobState.Failed,
        "PAUSED" => SearchJobState.Paused,
        _ => SearchJobState.Running // Default to running for unknown states
    };

    public static bool IsComplete(this SearchJobState state) =>
        state is SearchJobState.Done or SearchJobState.Failed;
}
