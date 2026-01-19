# Splunk CLI

A .NET CLI tool for exporting data from Splunk.

## Installation

Requires [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
# Clone and build
git clone https://github.com/venikman/splunk-cli.git
cd splunk-cli
dotnet build
```

## Usage

### Quick Start

```bash
# Set your Splunk connection
export SPLUNK_URL=https://your-splunk-server:8089
export SPLUNK_TOKEN=your-api-token

# Export search results
dotnet run --project src/SplunkTui -- export -q 'index=main | head 100'
```

### Getting a Splunk API Token

1. Log into Splunk Web
2. Go to **Settings → Tokens** (or Settings → Users → Tokens)
3. Click **New Token**
4. Set audience and expiration as needed
5. Copy the generated token

### Command Options

```bash
dotnet run --project src/SplunkTui -- export [options]

Options:
  -q, --query <query>        Splunk search query (required)
  -o, --output <file>        Output file (default: stdout)
  -f, --format <format>      Output format: csv, json, jsonl (default: csv)
  --fields <fields>          Comma-separated list of fields to export
  --max <count>              Maximum number of results (default: 10000)
  --batch-size <size>        Results per batch (default: 1000)
  --insecure                 Skip SSL certificate validation
  -?, -h, --help             Show help
```

### Examples

```bash
# Export to CSV file
dotnet run --project src/SplunkTui -- export \
  -q 'index=web_logs status>=400 | head 1000' \
  -o errors.csv

# Export as JSON with specific fields
dotnet run --project src/SplunkTui -- export \
  -q 'index=app_logs level=ERROR' \
  -f json \
  --fields '_time,host,message'

# Large export with batching
dotnet run --project src/SplunkTui -- export \
  -q 'index=metrics earliest=-1d' \
  --max 100000 \
  --batch-size 5000 \
  -o metrics.csv

# Self-signed certificate (common in enterprise)
dotnet run --project src/SplunkTui -- export \
  -q 'index=main' \
  --insecure
```

## Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `SPLUNK_URL` | Splunk REST API URL | `https://localhost:8089` |
| `SPLUNK_TOKEN` | API authentication token | (required) |
| `SPLUNK_INSECURE` | Skip SSL validation (`true`/`false`) | `false` |

### Config File

Create `~/.splunk-tui.json`:

```json
{
  "connection": {
    "url": "https://your-splunk-server:8089",
    "token": "your-api-token",
    "insecure": false
  }
}
```

Priority: CLI flags > Environment variables > Config file

## Output Formats

| Format | Description |
|--------|-------------|
| `csv` | Comma-separated values (default) |
| `json` | JSON array of objects |
| `jsonl` | JSON Lines (one object per line) |

## Development

See [DEV.md](DEV.md) for local development setup with Docker.

## License

MIT
