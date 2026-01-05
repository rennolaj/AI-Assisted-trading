#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root_dir="$(cd "$script_dir/.." && pwd)"
env_file="${SMOKE_ENV_FILE:-$root_dir/.env.smoke}"

load_env_file() {
  local file="$1"
  if [ ! -f "$file" ]; then
    return
  fi

  # Load KEY=VALUE pairs without overriding already-set environment variables.
  while IFS='=' read -r key value; do
    case "$key" in
      ""|\#*) continue ;;
    esac

    key="${key#"${key%%[![:space:]]*}"}"
    key="${key%"${key##*[![:space:]]}"}"
    value="${value#"${value%%[![:space:]]*}"}"
    value="${value%"${value##*[![:space:]]}"}"

    if [ "${value:0:1}" = "\"" ] && [ "${value: -1}" = "\"" ]; then
      value="${value:1:${#value}-2}"
    elif [ "${value:0:1}" = "'" ] && [ "${value: -1}" = "'" ]; then
      value="${value:1:${#value}-2}"
    fi

    if [ -z "${!key+x}" ]; then
      export "$key=$value"
    fi
  done < "$file"
}

load_env_file "$env_file"

BASE_URL="${BASE_URL:-http://localhost:8080}"
SECRET="${TRADINGVIEW_WEBHOOK_SECRET:-changeme}"
SYMBOL_HINT="${SYMBOL_HINT:-PI_XBTUSD}"
TICKER="${TICKER:-XBTUSD}"
EXCHANGE="${EXCHANGE:-krakenfutures}"
INTERVAL="${INTERVAL:-1}"
SLEEP_SECONDS="${SLEEP_SECONDS:-3}"

id="smoke-$(date +%s)"

payload=$(cat <<EOF
{
  "idempotencyKey": "$id",
  "ticker": "$TICKER",
  "exchange": "$EXCHANGE",
  "interval": "$INTERVAL",
  "close": 65000,
  "volume": 1200,
  "directionHint": "long",
  "symbolHint": "$SYMBOL_HINT",
  "reason": "smoke-test"
}
EOF
)

echo "Posting $id to $BASE_URL..."
response=$(curl -sS -w "\n%{http_code}\n" -H "Content-Type: application/json" -d "$payload" "$BASE_URL/webhooks/tradingview/$SECRET")
body=$(printf "%s" "$response" | sed '$d')
code=$(printf "%s" "$response" | tail -n 1)
echo "$body"

if [ "$code" != "202" ]; then
  echo "Expected 202 from webhook, got $code" >&2
  exit 1
fi

sleep "$SLEEP_SECONDS"

status_response=$(curl -sS -w "\n%{http_code}\n" "$BASE_URL/alerts/status/$id")
status_body=$(printf "%s" "$status_response" | sed '$d')
status_code=$(printf "%s" "$status_response" | tail -n 1)
echo "$status_body"

if [ "$status_code" != "200" ]; then
  echo "Expected 200 from status, got $status_code" >&2
  exit 1
fi

alert_id=$(printf "%s" "$status_body" | sed -n 's/.*"alertId":"\([^"]*\)".*/\1/p')
if [ -z "$alert_id" ]; then
  echo "Unable to parse alertId from status response." >&2
  exit 1
fi

snapshot_response=$(curl -sS -w "\n%{http_code}\n" "$BASE_URL/alerts/$alert_id/indicator-snapshot")
snapshot_body=$(printf "%s" "$snapshot_response" | sed '$d')
snapshot_code=$(printf "%s" "$snapshot_response" | tail -n 1)
echo "$snapshot_body"

if [ "$snapshot_code" != "200" ]; then
  echo "Expected 200 from indicator snapshot, got $snapshot_code" >&2
  exit 1
fi

echo "Smoke test succeeded for alert $alert_id."
