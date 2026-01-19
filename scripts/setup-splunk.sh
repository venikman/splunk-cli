#!/bin/bash
set -e

# Configuration
SPLUNK_URL="https://localhost:8089"
SPLUNK_USER="admin"
SPLUNK_PASSWORD="DevPassword123!"
HEC_URL="https://localhost:8088"
HEC_TOKEN="dev-hec-token-12345"

echo "🚀 Setting up Splunk development environment..."

# Wait for Splunk to be ready
echo "⏳ Waiting for Splunk to start (this may take 1-2 minutes)..."
until curl -sk "$SPLUNK_URL/services/server/info?output_mode=json" -u "$SPLUNK_USER:$SPLUNK_PASSWORD" 2>/dev/null | grep -q '"serverName"'; do
    echo "   Splunk not ready yet, waiting..."
    sleep 5
done
echo "✅ Splunk is running!"

# Create an authentication token for the CLI
echo "🔑 Creating API token for CLI..."
TOKEN_RESPONSE=$(curl -sk -X POST "$SPLUNK_URL/services/authorization/tokens" \
    -u "$SPLUNK_USER:$SPLUNK_PASSWORD" \
    -d "name=splunk-tui-dev" \
    -d "audience=splunk-tui" \
    -d "output_mode=json" 2>/dev/null || echo '{}')

# Extract token (handle both new creation and existing token scenarios)
API_TOKEN=$(echo "$TOKEN_RESPONSE" | grep -o '"token":"[^"]*"' | head -1 | cut -d'"' -f4)

if [ -z "$API_TOKEN" ]; then
    echo "⚠️  Token might already exist. Listing existing tokens..."
    # Get existing token
    API_TOKEN=$(curl -sk "$SPLUNK_URL/services/authorization/tokens?output_mode=json" \
        -u "$SPLUNK_USER:$SPLUNK_PASSWORD" | \
        grep -o '"token":"[^"]*"' | head -1 | cut -d'"' -f4)
fi

if [ -z "$API_TOKEN" ]; then
    echo "❌ Could not create or retrieve API token."
    echo "   You can create one manually in Splunk Web: Settings → Tokens"
    echo "   Or use basic auth for testing (not recommended for production)"
    API_TOKEN="<create-token-in-splunk-web>"
fi

# Enable HEC
echo "🔌 Enabling HTTP Event Collector..."
curl -sk -X POST "$SPLUNK_URL/servicesNS/admin/splunk_httpinput/data/inputs/http/http" \
    -u "$SPLUNK_USER:$SPLUNK_PASSWORD" \
    -d "disabled=0" > /dev/null 2>&1 || true

# Create HEC token if it doesn't exist
curl -sk -X POST "$SPLUNK_URL/servicesNS/admin/splunk_httpinput/data/inputs/http" \
    -u "$SPLUNK_USER:$SPLUNK_PASSWORD" \
    -d "name=dev-input" \
    -d "token=$HEC_TOKEN" > /dev/null 2>&1 || true

echo ""
echo "═══════════════════════════════════════════════════════════════"
echo "✅ Splunk is ready!"
echo ""
echo "📋 Connection Details:"
echo "   Splunk Web:     http://localhost:8000"
echo "   REST API:       https://localhost:8089"
echo "   HEC Endpoint:   https://localhost:8088"
echo ""
echo "🔐 Credentials:"
echo "   Username:       admin"
echo "   Password:       DevPassword123!"
echo "   API Token:      $API_TOKEN"
echo "   HEC Token:      $HEC_TOKEN"
echo ""
echo "📝 To configure the CLI, either:"
echo ""
echo "   Option 1: Environment variables"
echo "   export SPLUNK_URL=https://localhost:8089"
echo "   export SPLUNK_TOKEN=$API_TOKEN"
echo "   export SPLUNK_INSECURE=true"
echo ""
echo "   Option 2: Config file (~/.splunk-tui.json)"
cat << EOF
   {
     "connection": {
       "url": "https://localhost:8089",
       "token": "$API_TOKEN",
       "insecure": true
     }
   }
EOF
echo ""
echo "═══════════════════════════════════════════════════════════════"
echo ""
echo "🎲 Next: Run './scripts/generate-data.sh' to add sample events"
