# Development Setup

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (preview)
- [Docker](https://www.docker.com/)
- A terminal with bash support

## Quick Start

**One command (recommended):**
```bash
./scripts/dev.sh
```
This starts Splunk, waits for it, configures HEC, seeds 500 events, and displays your API token.

**Without seed data:**
```bash
docker compose up -d
./scripts/setup-splunk.sh   # wait ~60 seconds for Splunk first
```

**Custom seed data amount:**
```bash
docker compose up -d
./scripts/setup-splunk.sh
./scripts/generate-data.sh 1000   # any number of events
```

Once running:
- **Splunk Web**: http://localhost:8000 (admin / DevPassword123!)

### Using the CLI

After running `./scripts/dev.sh`, set up your environment:

```bash
export SPLUNK_URL=https://localhost:8089
export SPLUNK_TOKEN=$(cat /tmp/splunk-tui-token)
```

Then run queries:

```bash
# Basic export (CSV to stdout)
dotnet run --project src/SplunkTui -- export -q 'index=main' --insecure

# Export to file
dotnet run --project src/SplunkTui -- export -q 'index=main' -o events.csv --insecure

# JSON format
dotnet run --project src/SplunkTui -- export -q 'index=main' -f json --insecure
```

**Tip:** Create an alias for convenience:
```bash
alias splunk-tui='dotnet run --project src/SplunkTui --'
splunk-tui export -q 'index=main' --insecure
```

---

## Configuration

### Environment Variables

```bash
export SPLUNK_URL=https://localhost:8089
export SPLUNK_TOKEN=<your-token>
export SPLUNK_INSECURE=true
```

### Config File (`~/.splunk-tui.json`)

```json
{
  "connection": {
    "url": "https://localhost:8089",
    "token": "<your-token>",
    "insecure": true
  }
}
```

---

## More Query Examples

```bash
# Filter by log level
dotnet run --project src/SplunkTui -- export -q 'index=main level=ERROR' --insecure

# Select specific fields
dotnet run --project src/SplunkTui -- export \
  -q 'index=main' \
  --fields '_time,host,level,message' \
  --insecure

# Large export with batching
dotnet run --project src/SplunkTui -- export \
  -q 'index=main' \
  --max 50000 \
  --batch-size 5000 \
  -o large.csv \
  --insecure
```

---

## Splunk Web UI

Access at http://localhost:8000
- Username: `admin`
- Password: `DevPassword123!`

From there you can:
- Run searches manually
- Create API tokens (Settings → Tokens)
- View indexed data

---

## Running Tests

### Unit Tests

```bash
dotnet test tests/SplunkTui.Tests
```

### Integration Tests

Integration tests run against a real Splunk instance via docker-compose.

```bash
# Start Splunk
docker compose up -d

# Run integration tests (waits for Splunk, creates token, seeds data)
dotnet test tests/SplunkTui.IntegrationTests
```

The test fixture automatically:
- Waits for Splunk to be healthy
- Creates an API token for tests
- Seeds 100 test events

---

## Cleanup

Stop containers with `docker compose down`. Data persists in Docker volumes.

```bash
# Remove volumes to reset all Splunk data
docker volume rm splunk-explore_splunk-data splunk-explore_splunk-etc
```

---

## Troubleshooting

### "Certificate validation failed"

Use `--insecure` flag or `SPLUNK_INSECURE=true`. Dev Splunk uses self-signed cert.

### "Authentication failed"

1. Verify token is correct
2. Ensure token has `search` capability
3. Create new token in Splunk Web: Settings → Tokens

### HEC not accepting events

```bash
curl -sk https://localhost:8088/services/collector/health
```

### Splunk won't start

```bash
# Check logs
docker logs splunk-dev

# Reset
docker compose down
docker volume rm splunk-explore_splunk-data splunk-explore_splunk-etc
docker compose up -d
```

---

## Project Structure

```
splunk-tui/
├── docker-compose.yml          # Local Splunk container
├── src/
│   └── SplunkTui/              # Main CLI
├── tests/
│   ├── SplunkTui.Tests/        # Unit tests (fast, no dependencies)
│   └── SplunkTui.IntegrationTests/  # Integration tests (requires Splunk)
├── scripts/
│   ├── setup-splunk.sh         # Configure Splunk (token, HEC)
│   └── generate-data.sh        # Create sample events
└── DEV.md                      # This file
```
