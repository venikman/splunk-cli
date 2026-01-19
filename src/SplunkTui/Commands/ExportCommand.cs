using System.CommandLine;
using System.CommandLine.Invocation;
using SplunkTui.Models;
using SplunkTui.Services;

namespace SplunkTui.Commands;

public static class ExportCommand
{
    public static Command Create()
    {
        var command = new Command("export", "Export events from Splunk to a file");

        // Query options
        var queryOption = new Option<string>(
            aliases: ["-q", "--query"],
            description: "Splunk search query (required)")
        {
            IsRequired = true
        };

        // Time range options
        var daysOption = new Option<int?>(
            aliases: ["-d", "--days"],
            description: "Days back from now (default: 1)");

        var fromOption = new Option<string?>(
            aliases: ["--from"],
            description: "Start time (ISO 8601 or relative like \"-2h\")");

        var toOption = new Option<string?>(
            aliases: ["--to"],
            description: "End time (default: now)");

        // Size control options
        var maxOption = new Option<int>(
            aliases: ["--max"],
            getDefaultValue: () => 10_000,
            description: "Max total events to export (0 = unlimited)");

        var batchSizeOption = new Option<int>(
            aliases: ["--batch-size"],
            getDefaultValue: () => 10_000,
            description: "Events per API request (max: 50000)");

        // Output options
        var formatOption = new Option<string>(
            aliases: ["-f", "--format"],
            getDefaultValue: () => "csv",
            description: "Output format: csv, json, jsonl");

        var outputOption = new Option<string?>(
            aliases: ["-o", "--output"],
            description: "Output file path (default: stdout)");

        var fieldsOption = new Option<string?>(
            aliases: ["--fields"],
            description: "Comma-separated list of fields to include");

        var progressOption = new Option<bool>(
            aliases: ["--progress"],
            description: "Show progress bar");

        // Connection options (also available globally)
        var urlOption = new Option<string?>(
            aliases: ["--url"],
            description: "Splunk server URL (e.g., https://host:8089)");

        var tokenOption = new Option<string?>(
            aliases: ["--token"],
            description: "Splunk auth token");

        var insecureOption = new Option<bool?>(
            aliases: ["--insecure"],
            description: "Skip SSL certificate verification")
        {
            Arity = ArgumentArity.ZeroOrOne
        };

        var configOption = new Option<string?>(
            aliases: ["--config"],
            description: "Config file path (default: ~/.splunk-tui.json)");

        // Add all options to command
        command.AddOption(queryOption);
        command.AddOption(daysOption);
        command.AddOption(fromOption);
        command.AddOption(toOption);
        command.AddOption(maxOption);
        command.AddOption(batchSizeOption);
        command.AddOption(formatOption);
        command.AddOption(outputOption);
        command.AddOption(fieldsOption);
        command.AddOption(progressOption);
        command.AddOption(urlOption);
        command.AddOption(tokenOption);
        command.AddOption(insecureOption);
        command.AddOption(configOption);

        command.SetHandler(async (InvocationContext ctx) =>
        {
            var query = ctx.ParseResult.GetValueForOption(queryOption)!;
            var days = ctx.ParseResult.GetValueForOption(daysOption);
            var from = ctx.ParseResult.GetValueForOption(fromOption);
            var to = ctx.ParseResult.GetValueForOption(toOption);
            var max = ctx.ParseResult.GetValueForOption(maxOption);
            var batchSize = ctx.ParseResult.GetValueForOption(batchSizeOption);
            var format = ctx.ParseResult.GetValueForOption(formatOption)!;
            var output = ctx.ParseResult.GetValueForOption(outputOption);
            var fieldsStr = ctx.ParseResult.GetValueForOption(fieldsOption);
            var showProgress = ctx.ParseResult.GetValueForOption(progressOption);
            var url = ctx.ParseResult.GetValueForOption(urlOption);
            var token = ctx.ParseResult.GetValueForOption(tokenOption);
            var insecure = ctx.ParseResult.GetValueForOption(insecureOption);
            var configPath = ctx.ParseResult.GetValueForOption(configOption);

            var exitCode = await ExecuteAsync(
                query, days, from, to, max, batchSize, format, output,
                fieldsStr, showProgress, url, token, insecure, configPath,
                ctx.GetCancellationToken());

            ctx.ExitCode = exitCode;
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(
        string query,
        int? days,
        string? from,
        string? to,
        int max,
        int batchSize,
        string format,
        string? output,
        string? fieldsStr,
        bool showProgress,
        string? cliUrl,
        string? cliToken,
        bool? cliInsecure,
        string? configPath,
        CancellationToken ct)
    {
        try
        {
            // Validate options
            ValidateOptions(days, from, to, batchSize);

            // Load config and resolve values
            var configService = new ConfigService();
            var config = await configService.LoadConfigAsync(configPath, ct);

            var url = configService.ResolveUrl(cliUrl, config);
            var token = configService.ResolveToken(cliToken, config);
            var insecure = configService.ResolveInsecure(cliInsecure, config);

            if (string.IsNullOrEmpty(url))
            {
                Console.Error.WriteLine("Error: Splunk URL is required. Use --url, SPLUNK_URL, or config file.");
                return 1;
            }

            if (string.IsNullOrEmpty(token))
            {
                Console.Error.WriteLine("Error: Splunk token is required. Use --token, SPLUNK_TOKEN, or config file.");
                return 1;
            }

            // Resolve time range
            var (earliestTime, latestTime) = ResolveTimeRange(days, from, to);

            // Parse fields
            var fields = string.IsNullOrWhiteSpace(fieldsStr)
                ? null
                : fieldsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // Determine if progress should be shown
            var showProgressBar = showProgress || !string.IsNullOrEmpty(output);

            // Build export options
            var exportOptions = new ExportOptions
            {
                Url = url,
                Token = token,
                Insecure = insecure,
                Query = query,
                EarliestTime = earliestTime,
                LatestTime = latestTime,
                MaxResults = max,
                BatchSize = batchSize,
                Format = OutputFormatExtensions.Parse(format),
                OutputPath = output,
                Fields = fields,
                ShowProgress = showProgressBar
            };

            // Create HTTP client with SSL handling
            var handler = new HttpClientHandler();
            if (insecure)
            {
                handler.ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            using var httpClient = new HttpClient(handler);
            SplunkClient.ConfigureHttpClient(httpClient, url, token, insecure);

            var splunkClient = new SplunkClient(httpClient);
            var exportService = new ExportService(splunkClient);

            // Set up progress reporting
            IProgress<ExportProgress>? progress = showProgressBar
                ? new Progress<ExportProgress>(ReportProgress)
                : null;

            // Execute export
            var count = await exportService.ExportAsync(exportOptions, progress, ct);

            // Clear progress line and show summary to stderr
            if (showProgressBar)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"Exported {count:N0} events.");
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("\nExport cancelled.");
            return 130; // Standard exit code for Ctrl+C
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void ValidateOptions(int? days, string? from, string? to, int batchSize)
    {
        // Check for conflicting time options
        if (days.HasValue && (from != null || to != null))
        {
            throw new ArgumentException("Cannot use --days with --from/--to. Use one or the other.");
        }

        if (days.HasValue && days.Value < 1)
        {
            throw new ArgumentException("--days must be at least 1.");
        }

        if (batchSize < 1)
        {
            throw new ArgumentException("--batch-size must be at least 1.");
        }

        if (batchSize > 50_000)
        {
            throw new ArgumentException("--batch-size cannot exceed 50000 (Splunk limit).");
        }
    }

    private static (string earliestTime, string latestTime) ResolveTimeRange(
        int? days, string? from, string? to)
    {
        if (days.HasValue)
        {
            return ($"-{days.Value}d", "now");
        }

        if (from != null || to != null)
        {
            // Use provided values or defaults
            var earliest = from ?? "-1d";
            var latest = to ?? "now";

            // If it looks like a date, format it for Splunk
            if (DateTime.TryParse(earliest, out var fromDate))
            {
                earliest = fromDate.ToString("yyyy-MM-ddTHH:mm:ss");
            }

            if (DateTime.TryParse(latest, out var toDate))
            {
                latest = toDate.ToString("yyyy-MM-ddTHH:mm:ss");
            }

            return (earliest, latest);
        }

        // Default: last 1 day
        return ("-1d", "now");
    }

    private static void ReportProgress(ExportProgress p)
    {
        var message = p.Phase switch
        {
            ExportPhase.CreatingJob => "Creating search job...",
            ExportPhase.WaitingForJob => $"Waiting for job... {p.JobProgress:P0}",
            ExportPhase.FetchingResults => FormatFetchingProgress(p),
            ExportPhase.Complete => $"Complete: {p.EventsFetched:N0} events",
            _ => ""
        };

        // Write to stderr so it doesn't interfere with stdout output
        Console.Error.Write($"\r{message.PadRight(60)}");
    }

    private static string FormatFetchingProgress(ExportProgress p)
    {
        var percent = p.TotalEvents > 0 ? (double)p.EventsFetched / p.TotalEvents : 0;
        var barWidth = 30;
        var filled = (int)(percent * barWidth);
        var bar = new string('█', filled) + new string('░', barWidth - filled);

        return $"[{bar}] {p.EventsFetched:N0} / {p.TotalEvents:N0}";
    }
}
