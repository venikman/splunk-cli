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
            await RunTuiAsync(splunkClient, configService, configPath, url, config, ct);
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

    private static async Task RunTuiAsync(
        ISplunkClient client,
        IConfigService configService,
        string? configPath,
        string serverUrl,
        AppConfig config,
        CancellationToken ct)
    {
        // Application state with history from config
        var state = new TuiState
        {
            ServerUrl = serverUrl,
            Query = config.History.Count > 0 ? config.History[0] : "index=main",
            Status = "Ready. Press Enter to search, Ctrl+C to quit.",
            TimeRange = config.Defaults.TimeRange,
            ConfigService = configService,
            ConfigPath = configPath
        };
        state.History.AddRange(config.History);
        state.SavedSearches.AddRange(config.SavedSearches);

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

                    // Progress bar (visible during search)
                    state.IsSearching
                        ? v.Progress(state.SearchProgress).FixedHeight(1)
                        : v.Text("").FixedHeight(0),

                    // Results/Detail area
                    v.Border(b =>
                    [
                        BuildMainContent(b, state)
                    ], GetMainPanelTitle(state)).Fill(),

                    // Status bar using InfoBar
                    v.InfoBar(state.Status, GetKeyboardHints(state)).FixedHeight(1)
                ])
            )
            .WithMouse()
            .WithRenderOptimization()
            .Build();

        await terminal.RunAsync(ct);
    }

    private static string GetMainPanelTitle(TuiState state) => state.Mode switch
    {
        TuiMode.EventDetail => "Event Detail (Enter to go back)",
        _ => "Results"
    };

    private static string GetKeyboardHints(TuiState state) => state.Mode switch
    {
        TuiMode.EventDetail => "Enter: Back | ↑↓: Scroll",
        _ => "Ctrl+C: Quit | Enter: Search/Detail | ↑↓: Navigate"
    };

    private static Hex1bWidget BuildMainContent<TParent>(WidgetContext<TParent> ctx, TuiState state)
        where TParent : Hex1bWidget
    {
        return state.Mode switch
        {
            TuiMode.EventDetail => BuildEventDetailContent(ctx, state),
            _ => BuildResultsContent(ctx, state)
        };
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
            .OnSelectionChanged(e => state.SelectedIndex = e.SelectedIndex)
            .OnItemActivated(e =>
            {
                state.SelectedIndex = e.ActivatedIndex;
                state.Mode = TuiMode.EventDetail;
            });
    }

    private static Hex1bWidget BuildEventDetailContent<TParent>(WidgetContext<TParent> ctx, TuiState state)
        where TParent : Hex1bWidget
    {
        if (state.SelectedIndex < 0 || state.SelectedIndex >= state.Events.Count)
        {
            state.Mode = TuiMode.Results;
            return ctx.Text("No event selected.");
        }

        var evt = state.Events[state.SelectedIndex];

        // Build field list with header showing navigation hint
        var lines = new List<string>
        {
            "Press Enter to return to results",
            new('─', 50)
        };

        lines.AddRange(evt
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key}: {kv.Value ?? "(null)"}"));

        return ctx.List(lines)
            .OnItemActivated(_ =>
            {
                // Pressing Enter in detail view goes back to results
                state.Mode = TuiMode.Results;
            });
    }

    private static async Task ExecuteSearchAsync(TuiState state, ISplunkClient client, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(state.Query))
        {
            state.Status = "Please enter a search query.";
            return;
        }

        state.IsSearching = true;
        state.SearchProgress = 0;
        state.Status = "Creating search job...";
        state.Events.Clear();
        state.SelectedIndex = 0;
        state.Mode = TuiMode.Results;

        string? sid = null;
        try
        {
            // Create search job with configured time range
            sid = await client.CreateSearchJobAsync(state.Query, state.TimeRange, "now", ct);
            state.Status = $"Job created: {sid}. Waiting for results...";

            // Wait for job to complete
            var job = await client.WaitForJobAsync(
                sid,
                new Progress<SearchJob>(j =>
                {
                    state.SearchProgress = j.DoneProgress * 100;
                    state.Status = $"Searching... {j.DoneProgress:P0}";
                }),
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
            state.Status = $"Found {job.ResultCount:N0} events. Showing first {events.Length}. Press Enter on an event for details.";

            // Update local history state immediately (on main thread for thread safety)
            state.History.Remove(state.Query);
            state.History.Insert(0, state.Query);
            if (state.History.Count > 50)
                state.History.RemoveAt(state.History.Count - 1);

            // Persist to config file in background (fire and forget, don't block UI)
            if (state.ConfigService != null)
            {
                var queryToSave = state.Query; // Capture for closure
                var configServiceRef = state.ConfigService;
                var configPathRef = state.ConfigPath;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await configServiceRef.AddHistoryAsync(queryToSave, configPathRef, 50, CancellationToken.None);
                    }
                    catch
                    {
                        // History save failure is non-critical
                    }
                }, CancellationToken.None);
            }
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

            // Always cleanup the job if it was created
            if (!string.IsNullOrEmpty(sid))
            {
                try
                {
                    await client.DeleteJobAsync(sid, CancellationToken.None);
                }
                catch
                {
                    // Swallow cleanup exceptions to not mask original error
                }
            }
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
        public double SearchProgress { get; set; }
        public int SelectedIndex { get; set; }
        public List<SplunkEvent> Events { get; } = [];

        // Config defaults
        public string TimeRange { get; set; } = "-24h";

        // History and saved searches (persisted but not displayed in separate UI modes)
        public List<string> History { get; } = [];
        public List<SavedSearch> SavedSearches { get; } = [];
        public IConfigService? ConfigService { get; set; }
        public string? ConfigPath { get; set; }

        // UI mode
        public TuiMode Mode { get; set; } = TuiMode.Results;
    }

    private enum TuiMode
    {
        Results,
        EventDetail
    }
}
