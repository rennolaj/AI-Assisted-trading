#!/usr/bin/env bash
# Inject a fake alert with historical timestamp for testing
# Sends POST request to API webhook endpoint

set -euo pipefail

# Required parameters
TIMESTAMP="${1:?Error: Timestamp required (ISO 8601 format)}"
SYMBOL="${2:-BTCUSD.P}"
DIRECTION="${3:-LONG}"
CLOSE_PRICE="${4:?Error: Close price required}"
INTERVAL="${5:-5}"
EXCHANGE="${6:-krakenfutures}"

# Optional parameters
API_URL="${7:-http://localhost:8080/api/v1/tradingview/webhook}"
WEBHOOK_SECRET="${8:-${TRADINGVIEW_WEBHOOK_SECRET:-}}"

if [[ -z "$WEBHOOK_SECRET" ]]; then
  echo "Error: Webhook secret not provided. Set TRADINGVIEW_WEBHOOK_SECRET env var or pass as 8th argument." >&2
  exit 1
fi

echo "Injecting historical alert:" >&2
echo "  Timestamp: $TIMESTAMP" >&2
echo "  Symbol: $SYMBOL" >&2
echo "  Direction: $DIRECTION" >&2
echo "  Close Price: $CLOSE_PRICE" >&2
echo "  Interval: ${INTERVAL}min" >&2
echo "  Exchange: $EXCHANGE" >&2
echo "" >&2

# Send the alert
RESPONSE=$(curl -s -w "\n%{http_code}" -X POST "$API_URL" \
  -H "Content-Type: application/json" \
  -d "{
    \"secret\": \"$WEBHOOK_SECRET\",
    \"symbol\": \"$SYMBOL\",
    \"exchange\": \"$EXCHANGE\",
    \"interval\": \"$INTERVAL\",
    \"direction\": \"$DIRECTION\",
    \"close\": $CLOSE_PRICE,
    \"timestamp\": \"$TIMESTAMP\"
  }")

# Extract HTTP status code (last line)
HTTP_CODE=$(echo "$RESPONSE" | tail -n1)
BODY=$(echo "$RESPONSE" | head -n -1)

if [[ "$HTTP_CODE" == "200" ]]; then
  echo "✅ Alert queued successfully" >&2
  echo "$BODY" | jq '.' 2>/dev/null || echo "$BODY"
else
  echo "❌ Alert failed with HTTP $HTTP_CODE" >&2
  echo "$BODY"
  exit 1
fi
