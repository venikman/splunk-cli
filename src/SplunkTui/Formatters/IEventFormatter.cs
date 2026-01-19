using SplunkTui.Models;

namespace SplunkTui.Formatters;

/// <summary>
/// Interface for formatting Splunk events to output streams.
/// </summary>
public interface IEventFormatter
{
    /// <summary>
    /// Writes events to the output stream.
    /// </summary>
    /// <param name="writer">The output text writer</param>
    /// <param name="events">Stream of event batches</param>
    /// <param name="fields">Optional list of fields to include (null = all fields)</param>
    /// <param name="metadata">Metadata for formats that support it (e.g., JSON)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Total number of events written</returns>
    Task<int> WriteAsync(
        TextWriter writer,
        IAsyncEnumerable<SplunkEvent[]> events,
        string[]? fields,
        ExportMetadata metadata,
        CancellationToken ct = default);
}
