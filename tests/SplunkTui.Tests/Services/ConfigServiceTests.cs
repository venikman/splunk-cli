using SplunkTui.Models;
using SplunkTui.Services;

namespace SplunkTui.Tests.Services;

public class ConfigServiceTests
{
    private readonly ConfigService _configService = new();

    #region ResolveUrl Tests

    [Fact]
    public void ResolveUrl_CliProvided_UsesCli()
    {
        var config = new AppConfig
        {
            Connection = new ConnectionConfig { Url = "https://config.example.com:8089" }
        };

        // Simulate env var by not setting it - CLI should still win
        var result = _configService.ResolveUrl("https://cli.example.com:8089", config);

        result.Should().Be("https://cli.example.com:8089");
    }

    [Fact]
    public void ResolveUrl_OnlyConfig_UsesConfig()
    {
        var originalEnv = Environment.GetEnvironmentVariable("SPLUNK_URL");
        try
        {
            Environment.SetEnvironmentVariable("SPLUNK_URL", null);

            var config = new AppConfig
            {
                Connection = new ConnectionConfig { Url = "https://config.example.com:8089" }
            };

            var result = _configService.ResolveUrl(null, config);

            result.Should().Be("https://config.example.com:8089");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPLUNK_URL", originalEnv);
        }
    }

    [Fact]
    public void ResolveUrl_TrailingSlash_Normalizes()
    {
        var config = AppConfig.Empty;

        var result = _configService.ResolveUrl("https://example.com:8089/", config);

        result.Should().Be("https://example.com:8089");
    }

    [Fact]
    public void ResolveUrl_NoScheme_Throws()
    {
        var config = AppConfig.Empty;

        var act = () => _configService.ResolveUrl("example.com:8089", config);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*must include scheme*");
    }

    [Fact]
    public void ResolveUrl_NoSources_ReturnsNull()
    {
        // Clear any env vars that might interfere
        var originalEnv = Environment.GetEnvironmentVariable("SPLUNK_URL");
        try
        {
            Environment.SetEnvironmentVariable("SPLUNK_URL", null);
            var config = AppConfig.Empty;

            var result = _configService.ResolveUrl(null, config);

            result.Should().BeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPLUNK_URL", originalEnv);
        }
    }

    #endregion

    #region ResolveToken Tests

    [Fact]
    public void ResolveToken_CliProvided_UsesCli()
    {
        var config = new AppConfig
        {
            Connection = new ConnectionConfig { Token = "config-token" }
        };

        var result = _configService.ResolveToken("cli-token", config);

        result.Should().Be("cli-token");
    }

    [Fact]
    public void ResolveToken_OnlyConfig_UsesConfig()
    {
        var originalEnv = Environment.GetEnvironmentVariable("SPLUNK_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("SPLUNK_TOKEN", null);

            var config = new AppConfig
            {
                Connection = new ConnectionConfig { Token = "config-token" }
            };

            var result = _configService.ResolveToken(null, config);

            result.Should().Be("config-token");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPLUNK_TOKEN", originalEnv);
        }
    }

    [Fact]
    public void ResolveToken_EnvOverridesConfig()
    {
        var originalEnv = Environment.GetEnvironmentVariable("SPLUNK_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("SPLUNK_TOKEN", "env-token");

            var config = new AppConfig
            {
                Connection = new ConnectionConfig { Token = "config-token" }
            };

            var result = _configService.ResolveToken(null, config);

            result.Should().Be("env-token");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPLUNK_TOKEN", originalEnv);
        }
    }

    [Fact]
    public void ResolveToken_CliOverridesEnv()
    {
        var originalEnv = Environment.GetEnvironmentVariable("SPLUNK_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("SPLUNK_TOKEN", "env-token");

            var config = AppConfig.Empty;

            var result = _configService.ResolveToken("cli-token", config);

            result.Should().Be("cli-token");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPLUNK_TOKEN", originalEnv);
        }
    }

    #endregion

    #region ResolveInsecure Tests

    [Fact]
    public void ResolveInsecure_CliTrue_ReturnsTrue()
    {
        var config = new AppConfig
        {
            Connection = new ConnectionConfig { Insecure = false }
        };

        var result = _configService.ResolveInsecure(true, config);

        result.Should().BeTrue();
    }

    [Fact]
    public void ResolveInsecure_CliFalse_ReturnsFalse()
    {
        var config = new AppConfig
        {
            Connection = new ConnectionConfig { Insecure = true }
        };

        var result = _configService.ResolveInsecure(false, config);

        result.Should().BeFalse();
    }

    [Fact]
    public void ResolveInsecure_EnvTrue_ReturnsTrue()
    {
        var originalEnv = Environment.GetEnvironmentVariable("SPLUNK_INSECURE");
        try
        {
            Environment.SetEnvironmentVariable("SPLUNK_INSECURE", "true");

            var config = new AppConfig
            {
                Connection = new ConnectionConfig { Insecure = false }
            };

            var result = _configService.ResolveInsecure(null, config);

            result.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPLUNK_INSECURE", originalEnv);
        }
    }

    [Fact]
    public void ResolveInsecure_Env1_ReturnsTrue()
    {
        var originalEnv = Environment.GetEnvironmentVariable("SPLUNK_INSECURE");
        try
        {
            Environment.SetEnvironmentVariable("SPLUNK_INSECURE", "1");

            var config = AppConfig.Empty;

            var result = _configService.ResolveInsecure(null, config);

            result.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPLUNK_INSECURE", originalEnv);
        }
    }

    [Fact]
    public void ResolveInsecure_ConfigOnly_UsesConfig()
    {
        var originalEnv = Environment.GetEnvironmentVariable("SPLUNK_INSECURE");
        try
        {
            Environment.SetEnvironmentVariable("SPLUNK_INSECURE", null);

            var config = new AppConfig
            {
                Connection = new ConnectionConfig { Insecure = true }
            };

            var result = _configService.ResolveInsecure(null, config);

            result.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPLUNK_INSECURE", originalEnv);
        }
    }

    #endregion

    #region LoadConfigAsync Tests

    [Fact]
    public async Task LoadConfigAsync_FileNotFound_ReturnsEmpty()
    {
        var result = await _configService.LoadConfigAsync("/nonexistent/path/config.json");

        result.Should().NotBeNull();
        result.Connection.Url.Should().BeNull();
        result.Connection.Token.Should().BeNull();
    }

    [Fact]
    public async Task LoadConfigAsync_ValidJson_ParsesCorrectly()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, """
                {
                    "connection": {
                        "url": "https://test.example.com:8089",
                        "token": "test-token",
                        "insecure": true
                    },
                    "defaults": {
                        "timeRange": "-7d",
                        "maxResults": 50000,
                        "batchSize": 5000,
                        "format": "json"
                    }
                }
                """);

            var result = await _configService.LoadConfigAsync(tempFile);

            result.Connection.Url.Should().Be("https://test.example.com:8089");
            result.Connection.Token.Should().Be("test-token");
            result.Connection.Insecure.Should().BeTrue();
            result.Defaults.TimeRange.Should().Be("-7d");
            result.Defaults.MaxResults.Should().Be(50000);
            result.Defaults.BatchSize.Should().Be(5000);
            result.Defaults.Format.Should().Be("json");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadConfigAsync_InvalidJson_ThrowsWithMessage()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "{ invalid json }");

            var act = async () => await _configService.LoadConfigAsync(tempFile);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Invalid config file*");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion
}
