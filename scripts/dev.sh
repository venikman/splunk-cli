#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# Colors
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
RED='\033[0;31m'
NC='\033[0m'

echo -e "${BLUE}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${BLUE}  Splunk TUI - Development Environment${NC}"
echo -e "${BLUE}═══════════════════════════════════════════════════════════════${NC}"
echo ""

# Check Docker is running
echo -e "${YELLOW}Checking Docker...${NC}"
if ! docker info > /dev/null 2>&1; then
    echo -e "${RED}✗ Docker is not running. Please start Docker Desktop first.${NC}"
    exit 1
fi
echo -e "${GREEN}✓ Docker is running${NC}"

cd "$PROJECT_ROOT"

# Check if Splunk is already running
if curl -sk --connect-timeout 2 https://localhost:8089/services/server/info > /dev/null 2>&1; then
    echo -e "${GREEN}✓ Splunk already running${NC}"
    SPLUNK_ALREADY_RUNNING=true
else
    SPLUNK_ALREADY_RUNNING=false
    echo -e "${YELLOW}Splunk not running - will start with docker-compose${NC}"
fi

# Start Splunk if not running
if [ "$SPLUNK_ALREADY_RUNNING" = false ]; then
    echo ""
    echo -e "${YELLOW}Starting Splunk container...${NC}"
    docker compose up -d

    echo -e "${YELLOW}Waiting for Splunk to be ready (this takes ~60 seconds)...${NC}"
    for i in {1..60}; do
        if curl -sk --connect-timeout 2 https://localhost:8089/services/server/info > /dev/null 2>&1; then
            echo -e "${GREEN}✓ Splunk is ready${NC}"
            break
        fi
        echo -n "."
        sleep 2
    done
    echo ""
fi

# Setup Splunk
echo ""
echo -e "${YELLOW}Setting up Splunk...${NC}"
"$SCRIPT_DIR/setup-splunk.sh" 2>&1 | grep -E "(✅|✓|Token:|HEC Token:|API Token:)" || echo -e "${GREEN}✓ Setup complete${NC}"

echo ""
echo -e "${YELLOW}Generating sample data...${NC}"
"$SCRIPT_DIR/generate-data.sh" 500 2>&1 | grep -E "(✓|✅|Generated)" || echo -e "${GREEN}✓ Data ready${NC}"

# Get or create API token
TOKEN_FILE="/tmp/splunk-tui-token"
if [ -f "$TOKEN_FILE" ]; then
    CACHED_TOKEN=$(cat "$TOKEN_FILE")
    if curl -sk "https://localhost:8089/services/server/info" \
        -H "Authorization: Splunk $CACHED_TOKEN" 2>/dev/null | grep -q "server-info"; then
        API_TOKEN="$CACHED_TOKEN"
    fi
fi

if [ -z "$API_TOKEN" ]; then
    echo -e "${YELLOW}Creating API token...${NC}"
    API_TOKEN=$(curl -sk -X POST "https://localhost:8089/services/authorization/tokens?output_mode=json" \
        -u "admin:DevPassword123!" \
        -d "name=admin" \
        -d "audience=splunk-tui-$(date +%s)" 2>/dev/null | \
        grep -o '"token":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "")

    if [ -n "$API_TOKEN" ]; then
        echo "$API_TOKEN" > "$TOKEN_FILE"
        echo -e "${GREEN}✓ Token created${NC}"
    fi
fi

echo ""
echo -e "${BLUE}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${GREEN}✅ Development environment ready!${NC}"
echo ""
echo -e "  Splunk Web:       ${BLUE}http://localhost:8000${NC}"
echo -e "  Credentials:      admin / DevPassword123!"
if [ -n "$API_TOKEN" ]; then
    echo -e "  API Token:        ${API_TOKEN:0:50}..."
    echo ""
    echo "Run exports with:"
    echo "  export SPLUNK_TOKEN=\"$API_TOKEN\""
    echo "  dotnet run --project src/SplunkTui -- export -q 'index=main' --insecure"
fi
echo ""
echo -e "${BLUE}═══════════════════════════════════════════════════════════════${NC}"
echo ""
echo -e "${YELLOW}To stop: docker compose down${NC}"
