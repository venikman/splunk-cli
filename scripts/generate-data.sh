#!/bin/bash
set -e

# Configuration
HEC_URL="https://localhost:8088/services/collector/event"
HEC_TOKEN="dev-hec-token-12345"
EVENT_COUNT=${1:-1000}  # Default 1000 events, or pass as argument

echo "🎲 Generating $EVENT_COUNT sample events..."

# Sample data arrays
HOSTS=("web01" "web02" "web03" "api01" "api02" "db01" "cache01")
LEVELS=("DEBUG" "INFO" "INFO" "INFO" "WARN" "ERROR")
USERS=("alice" "bob" "charlie" "diana" "eve")
ENDPOINTS=("/api/users" "/api/orders" "/api/products" "/api/auth/login" "/api/health" "/api/search")
STATUS_CODES=("200" "200" "200" "200" "201" "400" "401" "404" "500")
METHODS=("GET" "GET" "GET" "POST" "PUT" "DELETE")

# Function to generate a random element from an array
random_element() {
    local arr=("$@")
    echo "${arr[$RANDOM % ${#arr[@]}]}"
}

# Function to generate a random response time (ms)
random_response_time() {
    echo $((50 + RANDOM % 450))
}

# Function to generate a random trace ID
random_trace_id() {
    printf '%08x%08x%08x%08x' $RANDOM $RANDOM $RANDOM $RANDOM
}

# Generate events in batches
BATCH_SIZE=100
SENT=0

echo "   Sending events to Splunk HEC..."

while [ $SENT -lt $EVENT_COUNT ]; do
    # Build a batch of events
    BATCH=""
    for i in $(seq 1 $BATCH_SIZE); do
        if [ $SENT -ge $EVENT_COUNT ]; then
            break
        fi

        HOST=$(random_element "${HOSTS[@]}")
        LEVEL=$(random_element "${LEVELS[@]}")
        USER=$(random_element "${USERS[@]}")
        ENDPOINT=$(random_element "${ENDPOINTS[@]}")
        STATUS=$(random_element "${STATUS_CODES[@]}")
        METHOD=$(random_element "${METHODS[@]}")
        RESPONSE_TIME=$(random_response_time)
        TRACE_ID=$(random_trace_id)

        # Calculate timestamp (spread over last 24 hours)
        OFFSET=$((RANDOM % 86400))
        TIMESTAMP=$(($(date +%s) - OFFSET))

        # Generate message based on level
        case $LEVEL in
            "ERROR")
                MESSAGE="Failed to process request: Connection refused to downstream service"
                ;;
            "WARN")
                MESSAGE="Slow response detected: ${RESPONSE_TIME}ms exceeds threshold"
                ;;
            *)
                MESSAGE="$METHOD $ENDPOINT completed in ${RESPONSE_TIME}ms"
                ;;
        esac

        # Build the event JSON
        EVENT=$(cat << EOF
{"time": $TIMESTAMP, "host": "$HOST", "sourcetype": "app:logs", "index": "main", "event": {"level": "$LEVEL", "message": "$MESSAGE", "user": "$USER", "endpoint": "$ENDPOINT", "method": "$METHOD", "status": $STATUS, "response_time_ms": $RESPONSE_TIME, "trace_id": "$TRACE_ID"}}
EOF
)
        BATCH="$BATCH$EVENT"
        SENT=$((SENT + 1))
    done

    # Send the batch
    RESPONSE=$(curl -sk -X POST "$HEC_URL" \
        -H "Authorization: Splunk $HEC_TOKEN" \
        -H "Content-Type: application/json" \
        -d "$BATCH" 2>&1)

    if echo "$RESPONSE" | grep -q '"code":0'; then
        echo "   ✓ Sent $SENT / $EVENT_COUNT events"
    else
        echo "   ✗ Error sending batch: $RESPONSE"
        exit 1
    fi
done

echo ""
echo "✅ Generated $EVENT_COUNT events!"
echo ""
echo "📊 Sample queries to try:"
echo ""
echo "   # All events from the last day"
echo "   dotnet run --project src/SplunkTui -- export -q 'index=main' --insecure"
echo ""
echo "   # Only errors"
echo "   dotnet run --project src/SplunkTui -- export -q 'index=main level=ERROR' --insecure"
echo ""
echo "   # Specific fields as JSON"
echo "   dotnet run --project src/SplunkTui -- export -q 'index=main' --fields '_time,host,level,message' -f json --insecure"
echo ""
echo "   # Export to file"
echo "   dotnet run --project src/SplunkTui -- export -q 'index=main' -o events.csv --insecure"
