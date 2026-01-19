using System.CommandLine;
using System.CommandLine.Invocation;
using Hex1b;
using Hex1b.Widgets;
using SplunkTui.Models;
using SplunkTui.Services;

namespace SplunkTui.Commands;

public static class TuiCommand
{
    public static Command Create()
    {
        var command = new Command("tui", "Interactive TUI for exploring Splunk data");

        // Connection options
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

        command.AddOption(urlOption);
        command.AddOption(tokenOption);
        command.AddOption(insecureOption);
        command.AddOption(configOption);

        command.SetHandler(async (InvocationContext ctx) =>
        {
            var url = ctx.ParseResult.GetValueForOption(urlOption);
            var token = ctx.ParseResult.GetValueForOption(tokenOption);
            var insecure = ctx.ParseResult.GetValueForOption(insecureOption);
            var configPath = ctx.ParseResult.GetValueForOption(configOption);

            var exitCode = await ExecuteAsync(url, token, insecure, configPath, ctx.GetCancellationToken());
            ctx.ExitCode = exitCode;
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(
        string? cliUrl,
        string? cliToken,
        bool? cliInsecure,
        string? configPath,
        CancellationToken ct)
    {
        try
        {
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

            // Create HTTP client with SSL handling
            var handler = new HttpClientHandler();
            if (insecure)
            {
                handler.ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            using var httpClient = new HttpClient(handler);
            SplunkClient.ConfigureHttpClient(httpClient, url, token);

            var splunkClient = new SplunkClient(httpClient);

            // Run the TUI
            await RunTuiAsync(splunkClient, url, ct);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0; // Clean exit
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task RunTuiAsync(ISplunkClient client, string serverUrl, CancellationToken ct)
    {
        // Application state
        var state = new TuiState
        {
            ServerUrl = serverUrl,
            Query = "index=main",
            Status = "Ready. Press Enter to search, Ctrl+C to quit."
        };

        await using var terminal = Hex1bTerminal.CreateBuilder()
            .WithHex1bApp((app, options) => ctx =>
                ctx.VStack(v =>
                [
                    // Header
                    v.Border(b =>
                    [
                        b.Text($"  Splunk TUI  |  Connected: {state.ServerUrl}")
                    ], "Header").FixedHeight(3),

                    // Search bar
                    v.Border(b =>
                    [
                        b.HStack(h =>
                        [
                            h.Text(" Query: ").FixedWidth(10),
                            h.TextBox(state.Query)
                                .OnTextChanged(e => state.Query = e.NewText)
                                .OnSubmit(e => _ = ExecuteSearchAsync(state, client, ct))
                        ])
                    ], "Search").FixedHeight(3),

                    // Results area
                    v.Border(b =>
                    [
                        BuildResultsContent(b, state)
                    ], "Results").Fill(),

                    // Status bar
                    v.HStack(h =>
                    [
                        h.Text($" {state.Status}"),
                        h.Text(" | Ctrl+C: Quit | Enter: Search | ↑↓: Navigate ")
                    ]).FixedHeight(1)
                ])
            )
            .WithMouse()
            .WithRenderOptimization()
            .Build();

        await terminal.RunAsync(ct);
    }

    private static Hex1bWidget BuildResultsContent<TParent>(WidgetContext<TParent> ctx, TuiState state)
        where TParent : Hex1bWidget
    {
        if (state.Events.Count == 0)
        {
            return ctx.Text(state.IsSearching ? "Searching..." : "No results. Enter a query and press Enter.");
        }

        var items = state.Events.Select(FormatEventForList).ToList();
        return ctx.List(items)
            .OnSelectionChanged(e => state.SelectedIndex = e.SelectedIndex);
    }

    private static async Task ExecuteSearchAsync(TuiState state, ISplunkClient client, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(state.Query))
        {
            state.Status = "Please enter a search query.";
            return;
        }

        state.IsSearching = true;
        state.Status = "Creating search job...";
        state.Events.Clear();
        state.SelectedIndex = 0;

        try
        {
            // Create search job with last 24 hours
            var sid = await client.CreateSearchJobAsync(state.Query, "-24h", "now", ct);
            state.Status = $"Job created: {sid}. Waiting for results...";

            // Wait for job to complete
            var job = await client.WaitForJobAsync(
                sid,
                new Progress<SearchJob>(j => state.Status = $"Searching... {j.DoneProgress:P0}"),
                ct);

            if (job.State == SearchJobState.Failed)
            {
                state.Status = $"Search failed: {job.FailureReason ?? "Unknown error"}";
                return;
            }

            state.Status = $"Fetching results ({job.ResultCount:N0} events)...";

            // Fetch first batch of results (up to 100 for TUI display)
            var maxResults = Math.Min(job.ResultCount, 100);
            var events = await client.GetResultsAsync(sid, 0, maxResults, null, ct);

            state.Events.AddRange(events);
            state.Status = $"Found {job.ResultCount:N0} events. Showing first {events.Length}.";

            // Cleanup job
            await client.DeleteJobAsync(sid, ct);
        }
        catch (OperationCanceledException)
        {
            state.Status = "Search cancelled.";
        }
        catch (Exception ex)
        {
            state.Status = $"Error: {ex.Message}";
        }
        finally
        {
            state.IsSearching = false;
        }
    }

    private static string FormatEventForList(SplunkEvent evt)
    {
        var time = evt.Time ?? "";
        var host = evt.Host ?? "";
        var raw = evt.Raw ?? "";

        // Truncate raw message for display
        var maxRawLen = 80;
        if (raw.Length > maxRawLen)
        {
            raw = string.Concat(raw.AsSpan(0, maxRawLen - 3), "...");
        }

        return $"{time} [{host}] {raw}";
    }

    private sealed class TuiState
    {
        public string ServerUrl { get; set; } = "";
        public string Query { get; set; } = "";
        public string Status { get; set; } = "";
        public bool IsSearching { get; set; }
        public int SelectedIndex { get; set; }
        public List<SplunkEvent> Events { get; } = [];
    }
}
