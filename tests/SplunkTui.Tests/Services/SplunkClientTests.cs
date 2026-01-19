using System.Net;
using System.Net.Http.Headers;
using SplunkTui.Services;

namespace SplunkTui.Tests.Services;

public class SplunkClientTests
{
    #region Authentication Header Tests

    [Fact]
    public void ConfigureHttpClient_SetsAuthHeader_WithSplunkPrefix()
    {
        // This is the CRITICAL test - Splunk uses "Splunk" prefix, NOT "Bearer"
        using var client = new HttpClient();

        SplunkClient.ConfigureHttpClient(client, "https://splunk.example.com:8089", "test-token-123", false);

        client.DefaultRequestHeaders.Authorization.Should().NotBeNull();
        client.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Splunk");
        client.DefaultRequestHeaders.Authorization.Parameter.Should().Be("test-token-123");
    }

    [Fact]
    public void ConfigureHttpClient_AuthHeaderIsNotBearer()
    {
        using var client = new HttpClient();

        SplunkClient.ConfigureHttpClient(client, "https://splunk.example.com:8089", "my-token", false);

        client.DefaultRequestHeaders.Authorization!.Scheme.Should().NotBe("Bearer");
    }

    [Fact]
    public void ConfigureHttpClient_SetsBaseAddress()
    {
        using var client = new HttpClient();

        SplunkClient.ConfigureHttpClient(client, "https://splunk.example.com:8089", "token", false);

        client.BaseAddress.Should().NotBeNull();
        client.BaseAddress!.Host.Should().Be("splunk.example.com");
        client.BaseAddress.Port.Should().Be(8089);
    }

    [Fact]
    public void ConfigureHttpClient_NormalizesTrailingSlash()
    {
        using var client = new HttpClient();

        SplunkClient.ConfigureHttpClient(client, "https://splunk.example.com:8089/", "token", false);

        // Should have a trailing slash for proper path resolution
        client.BaseAddress!.AbsoluteUri.Should().EndWith("/");
    }

    [Fact]
    public void ConfigureHttpClient_SetsJsonAcceptHeader()
    {
        using var client = new HttpClient();

        SplunkClient.ConfigureHttpClient(client, "https://splunk.example.com:8089", "token", false);

        client.DefaultRequestHeaders.Accept.Should().Contain(
            h => h.MediaType == "application/json");
    }

    #endregion

    #region CreateSearchJobAsync Tests (with mocked HTTP)

    [Fact]
    public async Task CreateSearchJobAsync_SendsCorrectRequest()
    {
        var handler = new MockHttpMessageHandler(request =>
        {
            // Verify the request
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.PathAndQuery.Should().Be("/services/search/jobs");

            // Verify auth header
            request.Headers.Authorization!.Scheme.Should().Be("Splunk");

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{ "sid": "1234567890.123" }""")
            };
        });

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://splunk.example.com:8089/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Splunk", "test-token");

        var splunkClient = new SplunkClient(client);
        var sid = await splunkClient.CreateSearchJobAsync("index=main", "-1d", "now");

        sid.Should().Be("1234567890.123");
    }

    [Fact]
    public async Task CreateSearchJobAsync_PrependsSearchKeyword()
    {
        string? capturedBody = null;
        var handler = new MockHttpMessageHandler(async request =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{ "sid": "123" }""")
            };
        });

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://splunk.example.com:8089/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Splunk", "token");

        var splunkClient = new SplunkClient(client);
        await splunkClient.CreateSearchJobAsync("index=main", "-1d", "now");

        capturedBody.Should().Contain("search=search+index");
    }

    [Fact]
    public async Task CreateSearchJobAsync_DoesNotDoublePrependSearch()
    {
        string? capturedBody = null;
        var handler = new MockHttpMessageHandler(async request =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{ "sid": "123" }""")
            };
        });

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://splunk.example.com:8089/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Splunk", "token");

        var splunkClient = new SplunkClient(client);
        await splunkClient.CreateSearchJobAsync("search index=main", "-1d", "now");

        // Should not have "search search"
        capturedBody.Should().NotContain("search+search");
    }

    [Fact]
    public async Task CreateSearchJobAsync_AuthFailure_ThrowsWithMessage()
    {
        var handler = new MockHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized));

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://splunk.example.com:8089/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Splunk", "bad-token");

        var splunkClient = new SplunkClient(client);

        var act = async () => await splunkClient.CreateSearchJobAsync("index=main", "-1d", "now");

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Authentication failed*");
    }

    [Fact]
    public async Task CreateSearchJobAsync_Forbidden_ThrowsWithMessage()
    {
        var handler = new MockHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Forbidden));

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://splunk.example.com:8089/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Splunk", "token");

        var splunkClient = new SplunkClient(client);

        var act = async () => await splunkClient.CreateSearchJobAsync("index=main", "-1d", "now");

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Permission denied*");
    }

    #endregion

    #region GetResultsAsync Tests

    [Fact]
    public async Task GetResultsAsync_ParsesResults()
    {
        var handler = new MockHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                        "results": [
                            { "_time": "2024-01-17T10:00:00Z", "host": "web01", "level": "INFO" },
                            { "_time": "2024-01-17T10:00:01Z", "host": "web02", "level": "ERROR" }
                        ]
                    }
                    """)
            });

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://splunk.example.com:8089/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Splunk", "token");

        var splunkClient = new SplunkClient(client);
        var results = await splunkClient.GetResultsAsync("sid123", 0, 10);

        results.Should().HaveCount(2);
        results[0]["_time"].Should().Be("2024-01-17T10:00:00Z");
        results[0]["host"].Should().Be("web01");
        results[1]["level"].Should().Be("ERROR");
    }

    [Fact]
    public async Task GetResultsAsync_UsesOffsetAndCount()
    {
        string? requestUri = null;
        var handler = new MockHttpMessageHandler(request =>
        {
            requestUri = request.RequestUri!.PathAndQuery;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "results": [] }""")
            };
        });

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://splunk.example.com:8089/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Splunk", "token");

        var splunkClient = new SplunkClient(client);
        await splunkClient.GetResultsAsync("sid123", 1000, 500);

        requestUri.Should().Contain("offset=1000");
        requestUri.Should().Contain("count=500");
    }

    [Fact]
    public async Task GetResultsAsync_EmptyResults_ReturnsEmptyArray()
    {
        var handler = new MockHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "results": [] }""")
            });

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://splunk.example.com:8089/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Splunk", "token");

        var splunkClient = new SplunkClient(client);
        var results = await splunkClient.GetResultsAsync("sid123", 0, 10);

        results.Should().BeEmpty();
    }

    #endregion

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>>? _asyncHandler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> asyncHandler)
        {
            _asyncHandler = asyncHandler;
            _handler = _ => throw new NotImplementedException();
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_asyncHandler != null)
                return await _asyncHandler(request);
            return _handler(request);
        }
    }
}
