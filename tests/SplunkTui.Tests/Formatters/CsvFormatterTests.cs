using SplunkTui.Formatters;
using SplunkTui.Models;

namespace SplunkTui.Tests.Formatters;

public class CsvFormatterTests
{
    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void EscapeField_SimpleValues_ReturnsUnescaped(string? input, string expected)
    {
        var result = CsvFormatter.EscapeField(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void EscapeField_WithComma_WrapsInQuotes()
    {
        var result = CsvFormatter.EscapeField("hello,world");
        result.Should().Be("\"hello,world\"");
    }

    [Fact]
    public void EscapeField_WithDoubleQuote_DoublesAndWraps()
    {
        var result = CsvFormatter.EscapeField("say \"hello\"");
        result.Should().Be("\"say \"\"hello\"\"\"");
    }

    [Fact]
    public void EscapeField_WithNewline_WrapsInQuotes()
    {
        var result = CsvFormatter.EscapeField("line1\nline2");
        result.Should().Be("\"line1\nline2\"");
    }

    [Fact]
    public void EscapeField_WithCarriageReturn_WrapsInQuotes()
    {
        var result = CsvFormatter.EscapeField("line1\rline2");
        result.Should().Be("\"line1\rline2\"");
    }

    [Fact]
    public void EscapeField_WithMultipleSpecialChars_HandlesAll()
    {
        var result = CsvFormatter.EscapeField("he said \"hi,\nbye\"");
        result.Should().Be("\"he said \"\"hi,\nbye\"\"\"");
    }

    [Fact]
    public async Task WriteAsync_WithEvents_WritesHeaderAndRows()
    {
        var formatter = new CsvFormatter();
        var writer = new StringWriter();

        var events = CreateEventBatches([
            new SplunkEvent { ["_time"] = "2024-01-17T10:00:00Z", ["host"] = "web01", ["level"] = "INFO" },
            new SplunkEvent { ["_time"] = "2024-01-17T10:00:01Z", ["host"] = "web02", ["level"] = "ERROR" }
        ]);

        var metadata = CreateMetadata();

        var count = await formatter.WriteAsync(writer, events, null, metadata);

        count.Should().Be(2);
        var output = writer.ToString();
        output.Should().Contain("_time");
        output.Should().Contain("host");
        output.Should().Contain("level");
        output.Should().Contain("web01");
        output.Should().Contain("web02");
    }

    [Fact]
    public async Task WriteAsync_WithSpecificFields_OnlyIncludesThoseFields()
    {
        var formatter = new CsvFormatter();
        var writer = new StringWriter();

        var events = CreateEventBatches([
            new SplunkEvent { ["_time"] = "2024-01-17T10:00:00Z", ["host"] = "web01", ["level"] = "INFO", ["extra"] = "data" }
        ]);

        var fields = new[] { "_time", "host" };
        var metadata = CreateMetadata();

        await formatter.WriteAsync(writer, events, fields, metadata);

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be("_time,host");
        lines[1].Should().NotContain("extra");
    }

    [Fact]
    public async Task WriteAsync_EmptyResults_ReturnsZero()
    {
        var formatter = new CsvFormatter();
        var writer = new StringWriter();

        var events = CreateEventBatches([]);
        var metadata = CreateMetadata();

        var count = await formatter.WriteAsync(writer, events, null, metadata);

        count.Should().Be(0);
    }

    [Fact]
    public async Task WriteAsync_WithMissingFields_WritesEmptyValues()
    {
        var formatter = new CsvFormatter();
        var writer = new StringWriter();

        var events = CreateEventBatches([
            new SplunkEvent { ["_time"] = "2024-01-17T10:00:00Z" }
        ]);

        var fields = new[] { "_time", "missing_field" };
        var metadata = CreateMetadata();

        await formatter.WriteAsync(writer, events, fields, metadata);

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[1].Should().Be("2024-01-17T10:00:00Z,");
    }

    private static async IAsyncEnumerable<SplunkEvent[]> CreateEventBatches(SplunkEvent[] events)
    {
        if (events.Length > 0)
            yield return events;
        await Task.CompletedTask;
    }

    private static ExportMetadata CreateMetadata() => new()
    {
        Query = "test query",
        From = "-1d",
        To = "now",
        Count = 0,
        ExportedAt = DateTimeOffset.UtcNow
    };
}
