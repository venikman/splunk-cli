using SplunkTui.Models;

namespace SplunkTui.Services;

/// <summary>
/// Service for orchestrating Splunk data exports.
/// </summary>
public interface IExportService
{
    /// <summary>
    /// Exports events based on the provided options.
    /// </summary>
    /// <param name="options">Export configuration</param>
    /// <param name="progress">Progress reporter for batch completion</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Total number of events exported</returns>
    Task<int> ExportAsync(
        ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken ct = default);
}

/// <summary>
/// Progress information for export operations.
/// </summary>
public sealed record ExportProgress
{
    public ExportPhase Phase { get; init; }
    public int EventsFetched { get; init; }
    public int TotalEvents { get; init; }
    public int CurrentBatch { get; init; }
    public int TotalBatches { get; init; }
    public double JobProgress { get; init; }
}

public enum ExportPhase
{
    CreatingJob,
    WaitingForJob,
    FetchingResults,
    Formatting,
    Complete
}
