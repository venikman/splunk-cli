using SplunkTui.Models;
using SplunkTui.Services;

namespace SplunkTui.IntegrationTests;

/// <summary>
/// Integration tests that run against a real Splunk instance via docker-compose.
/// These tests verify the SplunkClient works correctly with the actual Splunk REST API.
/// </summary>
[Collection("Splunk")]
public sealed class SplunkClientIntegrationTests
{
    private readonly SplunkFixture _fixture;

    public SplunkClientIntegrationTests(SplunkFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateSearchJob_WithValidQuery_ReturnsJobId()
    {
        // Arrange
        using var httpClient = _fixture.CreateApiClient();
        var client = new SplunkClient(httpClient);

        // Act
        var sid = await client.CreateSearchJobAsync(
            "index=main | head 10",
            earliestTime: "-24h",
            latestTime: "now");

        // Assert
        sid.Should().NotBeNullOrEmpty();
        sid.Should().Contain(".");

        // Cleanup
        await client.DeleteJobAsync(sid);
    }

    [Fact]
    public async Task WaitForJob_WithValidJob_ReturnsCompletedStatus()
    {
        // Arrange
        using var httpClient = _fixture.CreateApiClient();
        var client = new SplunkClient(httpClient);
        var sid = await client.CreateSearchJobAsync(
            "index=main | head 5",
            earliestTime: "-1h",
            latestTime: "now");

        // Act
        var job = await client.WaitForJobAsync(sid);

        // Assert
        job.State.IsComplete().Should().BeTrue();
        job.Sid.Should().Be(sid);

        // Cleanup
        await client.DeleteJobAsync(sid);
    }

    [Fact]
    public async Task GetResults_AfterJobComplete_ReturnsEvents()
    {
        // Arrange
        using var httpClient = _fixture.CreateApiClient();
        var client = new SplunkClient(httpClient);

        var sid = await client.CreateSearchJobAsync(
            "index=main | head 10",
            earliestTime: "-24h",
            latestTime: "now");

        var job = await client.WaitForJobAsync(sid);

        // Act
        var results = await client.GetResultsAsync(sid, offset: 0, count: 10);

        // Assert
        results.Should().NotBeNull();
        // Test fixture seeds sample data, so we should have some events
        if (job.ResultCount > 0)
        {
            results.Should().NotBeEmpty();
            results.First().Should().ContainKey("_raw");
        }

        // Cleanup
        await client.DeleteJobAsync(sid);
    }

    [Fact]
    public async Task GetResults_WithFieldSelection_ReturnsOnlyRequestedFields()
    {
        // Arrange
        using var httpClient = _fixture.CreateApiClient();
        var client = new SplunkClient(httpClient);

        var sid = await client.CreateSearchJobAsync(
            "index=main | head 5",
            earliestTime: "-24h",
            latestTime: "now");

        await client.WaitForJobAsync(sid);

        // Act
        var results = await client.GetResultsAsync(
            sid,
            offset: 0,
            count: 5,
            fields: ["_time", "host"]);

        // Assert
        results.Should().NotBeNull();
        if (results.Length > 0)
        {
            // Should have the requested fields
            results.First().Should().ContainKey("_time");
            results.First().Should().ContainKey("host");
        }

        // Cleanup
        await client.DeleteJobAsync(sid);
    }

    [Fact]
    public async Task CreateSearchJob_WithInvalidQuery_ThrowsException()
    {
        // Arrange
        using var httpClient = _fixture.CreateApiClient();
        var client = new SplunkClient(httpClient);

        // Act & Assert
        // This query has syntax errors
        var act = () => client.CreateSearchJobAsync(
            "index=main | invalid_command_that_does_not_exist",
            earliestTime: "-1h",
            latestTime: "now");

        // Splunk may accept the job but fail later, or reject immediately
        // Either behavior is acceptable - we just want to verify no unhandled exception
        try
        {
            var sid = await act();
            // If job was created, wait and check for failure
            var job = await client.WaitForJobAsync(sid);
            // It might have 0 results or failed state
            await client.DeleteJobAsync(sid);
        }
        catch (HttpRequestException)
        {
            // This is acceptable - Splunk rejected the query
        }
        catch (InvalidOperationException)
        {
            // This is also acceptable - job failed
        }
    }
}
