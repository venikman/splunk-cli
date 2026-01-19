using SplunkTui.Models;

namespace SplunkTui.Services;

/// <summary>
/// Service for loading, saving, and merging configuration from multiple sources.
/// </summary>
public interface IConfigService
{
    /// <summary>
    /// Gets the default config file path (~/.splunk-tui.json).
    /// </summary>
    string DefaultConfigPath { get; }

    /// <summary>
    /// Loads configuration from the config file.
    /// </summary>
    Task<AppConfig> LoadConfigAsync(string? configPath = null, CancellationToken ct = default);

    /// <summary>
    /// Saves configuration to the config file.
    /// </summary>
    Task SaveConfigAsync(AppConfig config, string? configPath = null, CancellationToken ct = default);

    /// <summary>
    /// Gets a connection URL, applying priority: CLI > Env > Config.
    /// </summary>
    string? ResolveUrl(string? cliUrl, AppConfig config);

    /// <summary>
    /// Gets an auth token, applying priority: CLI > Env > Config.
    /// </summary>
    string? ResolveToken(string? cliToken, AppConfig config);

    /// <summary>
    /// Gets the insecure flag, applying priority: CLI > Env > Config.
    /// </summary>
    bool ResolveInsecure(bool? cliInsecure, AppConfig config);

    /// <summary>
    /// Adds a query to history, persisting to config file.
    /// Maintains last N entries (default 50).
    /// </summary>
    Task AddHistoryAsync(string query, string? configPath = null, int maxEntries = 50, CancellationToken ct = default);

    /// <summary>
    /// Saves a search to the config file.
    /// </summary>
    Task SaveSearchAsync(SavedSearch search, string? configPath = null, CancellationToken ct = default);
}
