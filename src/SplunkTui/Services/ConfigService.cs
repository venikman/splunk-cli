using System.Text.Json;
using SplunkTui.Models;

namespace SplunkTui.Services;

public sealed class ConfigService : IConfigService
{
    private static readonly string DefaultConfigPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".splunk-tui.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    // Environment variable names
    private const string EnvUrl = "SPLUNK_URL";
    private const string EnvToken = "SPLUNK_TOKEN";
    private const string EnvInsecure = "SPLUNK_INSECURE";

    public async Task<AppConfig> LoadConfigAsync(string? configPath = null, CancellationToken ct = default)
    {
        var path = configPath ?? DefaultConfigPath;

        if (!File.Exists(path))
            return AppConfig.Empty;

        try
        {
            await using var stream = File.OpenRead(path);
            var config = await JsonSerializer.DeserializeAsync<AppConfig>(stream, JsonOptions, ct);
            return config ?? AppConfig.Empty;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid config file at {path}: {ex.Message}", ex);
        }
    }

    public string? ResolveUrl(string? cliUrl, AppConfig config)
    {
        // Priority: CLI > Env > Config
        if (!string.IsNullOrWhiteSpace(cliUrl))
            return NormalizeUrl(cliUrl);

        var envUrl = Environment.GetEnvironmentVariable(EnvUrl);
        if (!string.IsNullOrWhiteSpace(envUrl))
            return NormalizeUrl(envUrl);

        if (!string.IsNullOrWhiteSpace(config.Connection.Url))
            return NormalizeUrl(config.Connection.Url);

        return null;
    }

    public string? ResolveToken(string? cliToken, AppConfig config)
    {
        // Priority: CLI > Env > Config
        if (!string.IsNullOrWhiteSpace(cliToken))
            return cliToken;

        var envToken = Environment.GetEnvironmentVariable(EnvToken);
        if (!string.IsNullOrWhiteSpace(envToken))
            return envToken;

        return config.Connection.Token;
    }

    public bool ResolveInsecure(bool? cliInsecure, AppConfig config)
    {
        // Priority: CLI > Env > Config
        if (cliInsecure.HasValue)
            return cliInsecure.Value;

        var envInsecure = Environment.GetEnvironmentVariable(EnvInsecure);
        if (!string.IsNullOrWhiteSpace(envInsecure))
            return envInsecure.Equals("true", StringComparison.OrdinalIgnoreCase) ||
string.Equals(envInsecure, "1", StringComparison.Ordinal);

        return config.Connection.Insecure;
    }

    private static string NormalizeUrl(string url)
    {
        // Validate URL has scheme
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"URL must include scheme (https://): {url}");
        }

        // Remove trailing slash to avoid double slashes in requests
        return url.TrimEnd('/');
    }
}
