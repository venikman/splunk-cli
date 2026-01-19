using SplunkTui.Models;

namespace SplunkTui.Services;

/// <summary>
/// Service for loading and merging configuration from multiple sources.
/// </summary>
public interface IConfigService
{
    /// <summary>
    /// Loads configuration from the config file.
    /// </summary>
    Task<AppConfig> LoadConfigAsync(string? configPath = null, CancellationToken ct = default);

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
}
