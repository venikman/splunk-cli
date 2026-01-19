using System.Runtime.CompilerServices;
using SplunkTui.Formatters;
using SplunkTui.Models;

namespace SplunkTui.Services;

public sealed class ExportService : IExportService
{
    private readonly ISplunkClient _splunkClient;

    public ExportService(ISplunkClient splunkClient)
    {
        _splunkClient = splunkClient;
    }

    public async Task<int> ExportAsync(
        ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken ct = default)
    {
        string? sid = null;

        try
        {
            // Phase 1: Create search job
            progress?.Report(new ExportProgress { Phase = ExportPhase.CreatingJob });

            sid = await _splunkClient.CreateSearchJobAsync(
                options.Query,
                options.EarliestTime,
                options.LatestTime,
                ct);

            // Phase 2: Wait for job completion
            var jobProgress = new Progress<SearchJob>(job =>
            {
                progress?.Report(new ExportProgress
                {
                    Phase = ExportPhase.WaitingForJob,
                    JobProgress = job.DoneProgress
                });
            });

            var completedJob = await _splunkClient.WaitForJobAsync(sid, jobProgress, ct);

            var totalEvents = completedJob.ResultCount;
            var maxToFetch = options.MaxResults == 0
                ? totalEvents
                : Math.Min(options.MaxResults, totalEvents);

            var totalBatches = (int)Math.Ceiling((double)maxToFetch / options.BatchSize);

            // Phase 3: Fetch and format results
            var formatter = GetFormatter(options.Format);
            var metadata = new ExportMetadata
            {
                Query = options.Query,
                From = options.EarliestTime,
                To = options.LatestTime,
                Count = maxToFetch,
                ExportedAt = DateTimeOffset.UtcNow
            };

            // Create batch progress reporter
            var batchProgress = new BatchProgress(progress, totalEvents, totalBatches);

            // Set up output writer
            await using var writer = await CreateWriterAsync(options.OutputPath);

            var eventBatches = FetchBatchesAsync(
                sid,
                options.BatchSize,
                maxToFetch,
                options.Fields,
                batchProgress,
                ct);

            var count = await formatter.WriteAsync(writer, eventBatches, options.Fields, metadata, ct);

            // Phase 4: Complete
            progress?.Report(new ExportProgress
            {
                Phase = ExportPhase.Complete,
                EventsFetched = count,
                TotalEvents = totalEvents
            });

            return count;
        }
        finally
        {
            // Cleanup: Delete job (best effort)
            if (sid != null)
            {
                try
                {
                    await _splunkClient.DeleteJobAsync(sid, CancellationToken.None);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    private async IAsyncEnumerable<SplunkEvent[]> FetchBatchesAsync(
        string sid,
        int batchSize,
        int maxEvents,
        string[]? fields,
        BatchProgress progress,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var offset = 0;
        var fetched = 0;
        var batchNumber = 0;

        while (fetched < maxEvents)
        {
            ct.ThrowIfCancellationRequested();

            var count = Math.Min(batchSize, maxEvents - fetched);
            var batch = await _splunkClient.GetResultsAsync(sid, offset, count, fields, ct);

            if (batch.Length == 0)
                yield break;

            batchNumber++;
            fetched += batch.Length;
            offset += batch.Length;

            progress.Report(fetched, batchNumber);

            yield return batch;
        }
    }

    private static IEventFormatter GetFormatter(OutputFormat format) => format switch
    {
        OutputFormat.Csv => new CsvFormatter(),
        OutputFormat.Json => new JsonFormatter(),
        OutputFormat.Jsonl => new JsonlFormatter(),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown format")
    };

    private static async Task<TextWriter> CreateWriterAsync(string? outputPath)
    {
        if (string.IsNullOrEmpty(outputPath))
        {
            // Write to stdout
            return Console.Out;
        }

        // Ensure directory exists
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Open file (overwrites silently per design decision)
        return new StreamWriter(outputPath, append: false, encoding: System.Text.Encoding.UTF8);
    }

    private sealed class BatchProgress
    {
        private readonly IProgress<ExportProgress>? _progress;
        private readonly int _totalEvents;
        private readonly int _totalBatches;

        public BatchProgress(IProgress<ExportProgress>? progress, int totalEvents, int totalBatches)
        {
            _progress = progress;
            _totalEvents = totalEvents;
            _totalBatches = totalBatches;
        }

        public void Report(int fetched, int batchNumber)
        {
            _progress?.Report(new ExportProgress
            {
                Phase = ExportPhase.FetchingResults,
                EventsFetched = fetched,
                TotalEvents = _totalEvents,
                CurrentBatch = batchNumber,
                TotalBatches = _totalBatches
            });
        }
    }
}
