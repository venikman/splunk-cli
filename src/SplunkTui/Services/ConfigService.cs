using System.Text.Json;
using SplunkTui.Models;

namespace SplunkTui.Services;

public sealed class ConfigService : IConfigService
{
    private static readonly string s_defaultConfigPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".splunk-tui.json");

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    // Environment variable names
    private const string EnvUrl = "SPLUNK_URL";
    private const string EnvToken = "SPLUNK_TOKEN";
    private const string EnvInsecure = "SPLUNK_INSECURE";

    public string DefaultConfigPath => s_defaultConfigPath;

    public async Task<AppConfig> LoadConfigAsync(string? configPath = null, CancellationToken ct = default)
    {
        var path = configPath ?? s_defaultConfigPath;

        if (!File.Exists(path))
            return AppConfig.Empty;

        try
        {
            await using var stream = File.OpenRead(path);
            var config = await JsonSerializer.DeserializeAsync<AppConfig>(stream, s_jsonOptions, ct);
            return config ?? AppConfig.Empty;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid config file at {path}: {ex.Message}", ex);
        }
    }

    public async Task SaveConfigAsync(AppConfig config, string? configPath = null, CancellationToken ct = default)
    {
        var path = configPath ?? s_defaultConfigPath;

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, config, s_jsonOptions, ct);
    }

    public async Task AddHistoryAsync(string query, string? configPath = null, int maxEntries = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return;

        var config = await LoadConfigAsync(configPath, ct);

        // Remove duplicates and add to front
        var history = config.History
            .Where(h => !string.Equals(h, query, StringComparison.Ordinal))
            .Prepend(query)
            .Take(maxEntries)
            .ToList();

        var updated = config with { History = history };
        await SaveConfigAsync(updated, configPath, ct);
    }

    public async Task SaveSearchAsync(SavedSearch search, string? configPath = null, CancellationToken ct = default)
    {
        var config = await LoadConfigAsync(configPath, ct);

        // Replace existing search with same name or add new
        var searches = config.SavedSearches
            .Where(s => !string.Equals(s.Name, search.Name, StringComparison.OrdinalIgnoreCase))
            .Append(search)
            .ToList();

        var updated = config with { SavedSearches = searches };
        await SaveConfigAsync(updated, configPath, ct);
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
