using System.CommandLine;
using SplunkTui.Commands;

// Set up Ctrl+C handling
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// Build CLI
var rootCommand = new RootCommand("Splunk TUI CLI - Export and explore Splunk data")
{
    Name = "splunk-tui"
};

// Add commands
rootCommand.AddCommand(ExportCommand.Create());

// Run
return await rootCommand.InvokeAsync(args);
