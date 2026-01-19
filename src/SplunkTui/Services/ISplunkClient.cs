using SplunkTui.Models;

namespace SplunkTui.Services;

/// <summary>
/// Client for Splunk REST API operations.
/// </summary>
public interface ISplunkClient
{
    /// <summary>
    /// Creates a new search job.
    /// </summary>
    Task<string> CreateSearchJobAsync(
        string query,
        string earliestTime,
        string latestTime,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the status of a search job.
    /// </summary>
    Task<SearchJob> GetJobStatusAsync(string sid, CancellationToken ct = default);

    /// <summary>
    /// Waits for a search job to complete.
    /// </summary>
    Task<SearchJob> WaitForJobAsync(
        string sid,
        IProgress<SearchJob>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Fetches a batch of results from a completed search job.
    /// </summary>
    Task<SplunkEvent[]> GetResultsAsync(
        string sid,
        int offset,
        int count,
        string[]? fields = null,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a search job.
    /// </summary>
    Task DeleteJobAsync(string sid, CancellationToken ct = default);
}
