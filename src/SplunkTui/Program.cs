using System.CommandLine;
using SplunkTui.Commands;

// Build CLI
// Note: System.CommandLine handles Ctrl+C internally via ctx.GetCancellationToken()
var rootCommand = new RootCommand("Splunk TUI CLI - Export and explore Splunk data")
{
    Name = "splunk-tui"
};

// Add commands
rootCommand.AddCommand(ExportCommand.Create());

// Run
return await rootCommand.InvokeAsync(args);
