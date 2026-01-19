using System.Net.Http.Headers;
using System.Text.Json;
using SplunkTui.Models;

namespace SplunkTui.Services;

public sealed class SplunkClient : ISplunkClient
{
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(500);

    public SplunkClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Configures an HttpClient for Splunk API access.
    /// Note: SSL certificate validation should be configured on the HttpClientHandler before creating the HttpClient.
    /// </summary>
    public static void ConfigureHttpClient(HttpClient client, string baseUrl, string token)
    {
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

        // CRITICAL: Splunk uses "Splunk" prefix, NOT "Bearer"
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Splunk", token);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<string> CreateSearchJobAsync(
        string query,
        string earliestTime,
        string latestTime,
        CancellationToken ct = default)
    {
        // Splunk expects the search to start with "search " if it's a raw search
        var searchQuery = query.TrimStart();
        if (!searchQuery.StartsWith("search ", StringComparison.OrdinalIgnoreCase) &&
            !searchQuery.StartsWith('|'))
        {
            searchQuery = $"search {searchQuery}";
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
(StringComparer.Ordinal)
        {
            ["search"] = searchQuery,
            ["earliest_time"] = earliestTime,
            ["latest_time"] = latestTime,
            ["output_mode"] = "json"
        });

        using var response = await _httpClient.PostAsync("services/search/jobs", content, ct);
        await EnsureSuccessAsync(response, "create search job", ct);

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        // Response format: { "sid": "1234567890.123" }
        if (doc.RootElement.TryGetProperty("sid", out var sidElement))
        {
            return sidElement.GetString()
                ?? throw new InvalidOperationException("Search job creation returned null SID");
        }

        throw new InvalidOperationException($"Unexpected response format when creating search job: {json}");
    }

    public async Task<SearchJob> GetJobStatusAsync(string sid, CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync($"services/search/jobs/{sid}?output_mode=json", ct);
        await EnsureSuccessAsync(response, "get job status", ct);

        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseJobStatus(json, sid);
    }

    public async Task<SearchJob> WaitForJobAsync(
        string sid,
        IProgress<SearchJob>? progress = null,
        CancellationToken ct = default)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var job = await GetJobStatusAsync(sid, ct);
            progress?.Report(job);

            if (job.State.IsComplete())
            {
                if (job.State == SearchJobState.Failed)
                {
                    throw new InvalidOperationException(
                        $"Search job failed: {job.FailureReason ?? "Unknown error"}");
                }
                return job;
            }

            await Task.Delay(_pollInterval, ct);
        }
    }

    public async Task<SplunkEvent[]> GetResultsAsync(
        string sid,
        int offset,
        int count,
        string[]? fields = null,
        CancellationToken ct = default)
    {
        var url = $"services/search/jobs/{sid}/results?output_mode=json&offset={offset}&count={count}";

        if (fields is { Length: > 0 })
        {
            url += $"&f={string.Join("&f=", fields.Select(Uri.EscapeDataString))}";
        }

        using var response = await _httpClient.GetAsync(url, ct);
        await EnsureSuccessAsync(response, "get results", ct);

        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseResults(json);
    }

    public async Task DeleteJobAsync(string sid, CancellationToken ct = default)
    {
        using var response = await _httpClient.DeleteAsync($"services/search/jobs/{sid}", ct);
        // Don't throw on delete failure - it's cleanup
        if (!response.IsSuccessStatusCode)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Warning] Failed to delete Splunk job {sid}. Status: {response.StatusCode}");
        }
    }

    private static SearchJob ParseJobStatus(string json, string sid)
    {
        using var doc = JsonDocument.Parse(json);

        // Splunk returns: { "entry": [{ "content": { "dispatchState": "...", ... } }] }
        var entry = doc.RootElement
            .GetProperty("entry")
            .EnumerateArray()
            .FirstOrDefault();

        if (entry.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException($"Job {sid} not found");
        }

        var content = entry.GetProperty("content");

        var stateStr = content.TryGetProperty("dispatchState", out var state)
            ? state.GetString()
            : null;

        var eventCount = content.TryGetProperty("eventCount", out var ec)
            ? ec.GetInt32()
            : 0;

        var resultCount = content.TryGetProperty("resultCount", out var rc)
            ? rc.GetInt32()
            : 0;

        var doneProgress = content.TryGetProperty("doneProgress", out var dp)
            ? dp.GetDouble()
            : 0.0;

        var failureReason = content.TryGetProperty("messages", out var messages)
            ? ExtractFailureMessage(messages)
            : null;

        return new SearchJob
        {
            Sid = sid,
            State = SearchJobStateExtensions.Parse(stateStr),
            EventCount = eventCount,
            ResultCount = resultCount,
            DoneProgress = doneProgress,
            FailureReason = failureReason
        };
    }

    private static string? ExtractFailureMessage(JsonElement messages)
    {
        foreach (var msg in messages.EnumerateArray())
        {
            if (msg.TryGetProperty("type", out var type) &&
                type.GetString()?.Equals("ERROR", StringComparison.OrdinalIgnoreCase) == true &&
                msg.TryGetProperty("text", out var text))
            {
                return text.GetString();
            }
        }
        return null;
    }

    private static SplunkEvent[] ParseResults(string json)
    {
        using var doc = JsonDocument.Parse(json);

        // Response format: { "results": [ { "field1": "value1", ... }, ... ] }
        if (!doc.RootElement.TryGetProperty("results", out var results))
        {
            return [];
        }

        var events = new List<SplunkEvent>();

        foreach (var result in results.EnumerateArray())
        {
            var evt = new SplunkEvent();

            foreach (var prop in result.EnumerateObject())
            {
                // Handle both string and array values (Splunk can return multi-value fields)
                var value = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Array => string.Join(", ", prop.Value.EnumerateArray()
                        .Select(v => v.GetString() ?? "")),
                    JsonValueKind.Number => prop.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => null,
                    _ => prop.Value.GetRawText()
                };

                evt[prop.Name] = value;
            }

            events.Add(evt);
        }

        return [.. events];
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(ct);

        var message = response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => "Authentication failed. Check your Splunk token.",
            System.Net.HttpStatusCode.Forbidden => "Permission denied. Token may lack required capabilities.",
            System.Net.HttpStatusCode.NotFound => $"Resource not found during {operation}.",
            System.Net.HttpStatusCode.BadRequest => TryExtractSplunkError(body) ?? $"Invalid request: {body}",
            _ => $"HTTP {(int)response.StatusCode} during {operation}: {body}"
        };

        throw new HttpRequestException(message);
    }

    private static string? TryExtractSplunkError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("messages", out var messages))
            {
                foreach (var msg in messages.EnumerateArray())
                {
                    if (msg.TryGetProperty("text", out var text))
                        return text.GetString();
                }
            }
        }
        catch
        {
            // Best-effort Splunk error extraction: body may be non-JSON or in an unexpected format
        }
        return null;
    }
}
