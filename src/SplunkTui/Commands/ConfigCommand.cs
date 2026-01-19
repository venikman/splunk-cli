using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using SplunkTui.Models;
using SplunkTui.Services;

namespace SplunkTui.Commands;

public static class ConfigCommand
{
    private static readonly JsonSerializerOptions s_displayOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Command Create()
    {
        var command = new Command("config", "Manage configuration settings");

        command.AddCommand(CreateShowCommand());
        command.AddCommand(CreateSetCommand());
        command.AddCommand(CreateInitCommand());

        return command;
    }

    private static Command CreateShowCommand()
    {
        var command = new Command("show", "Display current configuration");

        var configOption = new Option<string?>(
            aliases: ["--config"],
            description: "Config file path (default: ~/.splunk-tui.json)");

        var jsonOption = new Option<bool>(
            aliases: ["--json"],
            description: "Output as JSON");

        command.AddOption(configOption);
        command.AddOption(jsonOption);

        command.SetHandler(async (InvocationContext ctx) =>
        {
            var configPath = ctx.ParseResult.GetValueForOption(configOption);
            var asJson = ctx.ParseResult.GetValueForOption(jsonOption);

            var exitCode = await ExecuteShowAsync(configPath, asJson, ctx.GetCancellationToken());
            ctx.ExitCode = exitCode;
        });

        return command;
    }

    private static Command CreateSetCommand()
    {
        var command = new Command("set", "Set a configuration value");

        var keyArg = new Argument<string>("key", "Config key (e.g., connection.url, defaults.format)");
        var valueArg = new Argument<string>("value", "Value to set");

        var configOption = new Option<string?>(
            aliases: ["--config"],
            description: "Config file path (default: ~/.splunk-tui.json)");

        command.AddArgument(keyArg);
        command.AddArgument(valueArg);
        command.AddOption(configOption);

        command.SetHandler(async (InvocationContext ctx) =>
        {
            var key = ctx.ParseResult.GetValueForArgument(keyArg);
            var value = ctx.ParseResult.GetValueForArgument(valueArg);
            var configPath = ctx.ParseResult.GetValueForOption(configOption);

            var exitCode = await ExecuteSetAsync(key, value, configPath, ctx.GetCancellationToken());
            ctx.ExitCode = exitCode;
        });

        return command;
    }

    private static Command CreateInitCommand()
    {
        var command = new Command("init", "Create a new configuration file");

        var configOption = new Option<string?>(
            aliases: ["--config"],
            description: "Config file path (default: ~/.splunk-tui.json)");

        var forceOption = new Option<bool>(
            aliases: ["--force", "-f"],
            description: "Overwrite existing config file");

        command.AddOption(configOption);
        command.AddOption(forceOption);

        command.SetHandler(async (InvocationContext ctx) =>
        {
            var configPath = ctx.ParseResult.GetValueForOption(configOption);
            var force = ctx.ParseResult.GetValueForOption(forceOption);

            var exitCode = await ExecuteInitAsync(configPath, force, ctx.GetCancellationToken());
            ctx.ExitCode = exitCode;
        });

        return command;
    }

    private static async Task<int> ExecuteShowAsync(string? configPath, bool asJson, CancellationToken ct)
    {
        var configService = new ConfigService();
        var path = configPath ?? configService.DefaultConfigPath;

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Config file not found: {path}");
            Console.Error.WriteLine("Run 'splunk-tui config init' to create one.");
            return 1;
        }

        var config = await configService.LoadConfigAsync(configPath, ct);

        if (asJson)
        {
            var json = JsonSerializer.Serialize(config, s_displayOptions);
            Console.WriteLine(json);
        }
        else
        {
            DisplayConfigTable(config, path);
        }

        return 0;
    }

    private static void DisplayConfigTable(AppConfig config, string path)
    {
        Console.WriteLine($"Config file: {path}");
        Console.WriteLine();
        Console.WriteLine("Connection:");
        Console.WriteLine($"  url:      {config.Connection.Url ?? "(not set)"}");
        Console.WriteLine($"  token:    {MaskToken(config.Connection.Token)}");
        Console.WriteLine($"  insecure: {config.Connection.Insecure}");
        Console.WriteLine();
        Console.WriteLine("Defaults:");
        Console.WriteLine($"  timeRange:  {config.Defaults.TimeRange}");
        Console.WriteLine($"  maxResults: {config.Defaults.MaxResults:N0}");
        Console.WriteLine($"  batchSize:  {config.Defaults.BatchSize:N0}");
        Console.WriteLine($"  format:     {config.Defaults.Format}");
        Console.WriteLine();
        Console.WriteLine($"Saved searches: {config.SavedSearches.Count}");
        Console.WriteLine($"History entries: {config.History.Count}");
    }

    private static string MaskToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return "(not set)";

        if (token.Length <= 8)
            return "****";

        return string.Concat(token.AsSpan(0, 4), "****", token.AsSpan(token.Length - 4));
    }

    private static async Task<int> ExecuteSetAsync(string key, string value, string? configPath, CancellationToken ct)
    {
        var configService = new ConfigService();
        var config = await configService.LoadConfigAsync(configPath, ct);

        var updated = ApplyConfigUpdate(config, key, value);
        if (updated == null)
        {
            Console.Error.WriteLine($"Unknown config key: {key}");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Valid keys:");
            Console.Error.WriteLine("  connection.url      - Splunk server URL");
            Console.Error.WriteLine("  connection.token    - Auth token");
            Console.Error.WriteLine("  connection.insecure - Skip SSL verification (true/false)");
            Console.Error.WriteLine("  defaults.timeRange  - Default time range (e.g., -1d, -24h)");
            Console.Error.WriteLine("  defaults.maxResults - Default max results");
            Console.Error.WriteLine("  defaults.batchSize  - Default batch size");
            Console.Error.WriteLine("  defaults.format     - Default output format (csv/json/jsonl)");
            return 1;
        }

        await configService.SaveConfigAsync(updated, configPath, ct);
        Console.WriteLine($"Set {key} = {(key.Contains("token", StringComparison.OrdinalIgnoreCase) ? "****" : value)}");
        return 0;
    }

    private static AppConfig? ApplyConfigUpdate(AppConfig config, string key, string value)
    {
        return key.ToLowerInvariant() switch
        {
            "connection.url" => config with
            {
                Connection = config.Connection with { Url = value }
            },
            "connection.token" => config with
            {
                Connection = config.Connection with { Token = value }
            },
            "connection.insecure" => config with
            {
                Connection = config.Connection with
                {
                    Insecure = value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(value, "1", StringComparison.Ordinal)
                }
            },
            "defaults.timerange" => config with
            {
                Defaults = config.Defaults with { TimeRange = value }
            },
            "defaults.maxresults" => int.TryParse(value, out var max)
                ? config with { Defaults = config.Defaults with { MaxResults = max } }
                : null,
            "defaults.batchsize" => int.TryParse(value, out var batch)
                ? config with { Defaults = config.Defaults with { BatchSize = batch } }
                : null,
            "defaults.format" => config with
            {
                Defaults = config.Defaults with { Format = value }
            },
            _ => null
        };
    }

    private static async Task<int> ExecuteInitAsync(string? configPath, bool force, CancellationToken ct)
    {
        var configService = new ConfigService();
        var path = configPath ?? configService.DefaultConfigPath;

        if (File.Exists(path) && !force)
        {
            Console.Error.WriteLine($"Config file already exists: {path}");
            Console.Error.WriteLine("Use --force to overwrite.");
            return 1;
        }

        var config = new AppConfig
        {
            Connection = new ConnectionConfig
            {
                Url = "https://localhost:8089",
                Insecure = true
            },
            Defaults = new DefaultsConfig()
        };

        await configService.SaveConfigAsync(config, path, ct);
        Console.WriteLine($"Created config file: {path}");
        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine("  1. Set your Splunk URL:   splunk-tui config set connection.url https://your-splunk:8089");
        Console.WriteLine("  2. Set your auth token:   splunk-tui config set connection.token YOUR_TOKEN");
        Console.WriteLine("  3. Test connection:       splunk-tui tui");

        return 0;
    }
}
