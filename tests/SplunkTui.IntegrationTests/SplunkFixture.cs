using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SplunkTui.IntegrationTests;

/// <summary>
/// Shared fixture for integration tests.
/// Expects Splunk to be running via docker-compose before tests start.
/// Run: docker compose up -d, then ./scripts/setup-tests.sh
/// </summary>
public sealed class SplunkFixture : IAsyncLifetime
{
    private HttpClient? _adminClient;

    public string SplunkApiUrl => "https://localhost:8089";
    public string SplunkHecUrl => "https://localhost:8088";
    public string ApiToken { get; private set; } = null!;
    public string HecToken => "dev-hec-token-12345";
    public string Password => "DevPassword123!";

    public async Task InitializeAsync()
    {
        _adminClient = CreateAdminClient();

        // Wait for Splunk to be ready
        await WaitForSplunkAsync();

        // Create API token for tests
        ApiToken = await CreateApiTokenAsync();

        // Seed test data
        await SeedTestDataAsync();
    }

    public Task DisposeAsync()
    {
        _adminClient?.Dispose();
        return Task.CompletedTask;
    }

    private async Task WaitForSplunkAsync()
    {
        var maxAttempts = 60;
        for (var i = 0; i < maxAttempts; i++)
        {
            try
            {
                using var response = await _adminClient!.GetAsync("services/server/health/splunkd/details");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // Splunk not ready yet
            }
            await Task.Delay(2000);
        }
        throw new TimeoutException("Splunk did not become healthy within 2 minutes");
    }

    private async Task<string> CreateApiTokenAsync()
    {
        // Splunk token API: "name" is the username, "audience" is a unique identifier
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
(StringComparer.Ordinal)
        {
            ["name"] = "admin",
            ["audience"] = $"integration-test-{Guid.NewGuid():N}"
        });

        using var response = await _adminClient!.PostAsync(
            "services/authorization/tokens?output_mode=json",
            content);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var token = doc.RootElement
            .GetProperty("entry")[0]
            .GetProperty("content")
            .GetProperty("token")
            .GetString();

        return token ?? throw new InvalidOperationException("Failed to create API token");
    }

    private async Task SeedTestDataAsync()
    {
        using var hecClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        });

        hecClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Splunk", HecToken);

        var events = new StringBuilder();
        var hosts = new[] { "web01", "web02", "api01", "db01" };
        var levels = new[] { "DEBUG", "INFO", "INFO", "INFO", "WARN", "ERROR" };

        var random = new Random(42); // Fixed seed for reproducibility
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        for (var i = 0; i < 100; i++)
        {
            var host = hosts[random.Next(hosts.Length)];
            var level = levels[random.Next(levels.Length)];
            var timestamp = now - random.Next(86400);
            var requestId = Guid.NewGuid().ToString("N");

            events.AppendLine(
                $$$"""{"time": {{{timestamp}}}, "host": "{{{host}}}", "sourcetype": "app:logs", "index": "main", "event": {"level": "{{{level}}}", "message": "Test event {{{i}}}", "request_id": "{{{requestId}}}"}}""");
        }

        using var content = new StringContent(events.ToString(), Encoding.UTF8, "application/json");
        using var response = await hecClient.PostAsync(
            $"{SplunkHecUrl}/services/collector/event",
            content);

        response.EnsureSuccessStatusCode();
    }

    private HttpClient CreateAdminClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(SplunkApiUrl + "/")
        };

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"admin:{Password}"));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        return client;
    }

    /// <summary>
    /// Creates an HttpClient configured for Splunk API access using the test token.
    /// </summary>
    public HttpClient CreateApiClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        var client = new HttpClient(handler);
        Services.SplunkClient.ConfigureHttpClient(client, SplunkApiUrl, ApiToken);
        return client;
    }
}

/// <summary>
/// Collection definition so all tests share the same Splunk instance.
/// </summary>
[CollectionDefinition("Splunk")]
public sealed class SplunkCollection : ICollectionFixture<SplunkFixture>
{
}
