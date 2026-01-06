#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root_dir="$(cd "$script_dir/.." && pwd)"
env_file="${SMOKE_ENV_FILE:-$root_dir/.env.smoke}"

ngrok_pid=""
ngrok_log=""
timeout_pid=""
parent_pid="$$"

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

cleanup() {
  if [ -n "${timeout_pid:-}" ]; then
    kill "$timeout_pid" 2>/dev/null || true
  fi

  if [ -n "${ngrok_pid:-}" ]; then
    kill "$ngrok_pid" 2>/dev/null || true
  fi

  if [ -n "${ngrok_log:-}" ] && [ -f "$ngrok_log" ]; then
    rm -f "$ngrok_log"
  fi
}

trap cleanup EXIT INT TERM

load_env_file "$env_file"

BASE_URL="${BASE_URL:-http://localhost:8080}"
SECRET="${TRADINGVIEW_WEBHOOK_SECRET:-changeme}"
SYMBOL_HINT="${SYMBOL_HINT:-PI_XBTUSD}"
TICKER="${TICKER:-XBTUSD}"
EXCHANGE="${EXCHANGE:-krakenfutures}"
INTERVAL="${INTERVAL:-1}"
SLEEP_SECONDS="${SLEEP_SECONDS:-3}"
NGROK_AUTOSTART="${NGROK_AUTOSTART:-1}"
SMOKE_TIMEOUT_SECONDS="${SMOKE_TIMEOUT_SECONDS:-300}"
STATUS_TIMEOUT_SECONDS="${STATUS_TIMEOUT_SECONDS:-30}"
STATUS_POLL_SECONDS="${STATUS_POLL_SECONDS:-2}"
SMOKE_LOOP_SECONDS="${SMOKE_LOOP_SECONDS:-0}"
SMOKE_LOOP_INTERVAL="${SMOKE_LOOP_INTERVAL:-5}"

start_timeout() {
  if [ "$SMOKE_TIMEOUT_SECONDS" -le 0 ]; then
    return
  fi

  (
    sleep "$SMOKE_TIMEOUT_SECONDS"
    echo "Smoke test timed out after ${SMOKE_TIMEOUT_SECONDS}s; stopping." >&2
    kill -TERM "$parent_pid" 2>/dev/null || true
  ) &
  timeout_pid=$!
}

get_ngrok_url() {
  local json
  json="$(curl -s --max-time 1 http://127.0.0.1:4040/api/tunnels 2>/dev/null || true)"
  if [ -z "$json" ]; then
    return 0
  fi

  NGROK_JSON="$json" python3 - <<'PY'
import json, os, sys
try:
    data = json.loads(os.environ.get("NGROK_JSON", ""))
except Exception:
    sys.exit(0)
for tunnel in data.get("tunnels", []):
    if tunnel.get("proto") == "https" and tunnel.get("public_url"):
        print(tunnel["public_url"])
        sys.exit(0)
for tunnel in data.get("tunnels", []):
    if tunnel.get("public_url"):
        print(tunnel["public_url"])
        sys.exit(0)
PY
}

start_ngrok() {
  if [ "$NGROK_AUTOSTART" = "0" ]; then
    return
  fi

  if ! command -v ngrok >/dev/null 2>&1; then
    echo "ngrok is not installed. Install it or set NGROK_AUTOSTART=0." >&2
    exit 1
  fi

  local existing_url
  existing_url="$(get_ngrok_url)"
  if [ -n "$existing_url" ]; then
    BASE_URL="$existing_url"
    echo "ngrok already running at $BASE_URL"
    return
  fi

  ngrok_log="$(mktemp -t mvp-ngrok.XXXXXX)"
  ngrok http 8080 --log=stdout >"$ngrok_log" 2>&1 &
  ngrok_pid=$!

  for _ in {1..30}; do
    local_url="$(get_ngrok_url)"
    if [ -n "$local_url" ]; then
      BASE_URL="$local_url"
      echo "ngrok started at $BASE_URL"
      return
    fi
    sleep 0.5
  done

  echo "Unable to determine ngrok URL. See $ngrok_log" >&2
  exit 1
}

start_timeout
start_ngrok

last_status=""
last_alert_id=""
last_id=""

run_once() {
  local status_body=""
  local status_code=""
  local status=""
  local end_ts=""
  local alert_id=""

  last_id="smoke-$(date +%s)"

  payload=$(cat <<EOF
{
  "idempotencyKey": "$last_id",
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

  echo "Posting $last_id to $BASE_URL..."
  response=$(curl -sS -w "\n%{http_code}\n" -H "Content-Type: application/json" -d "$payload" "$BASE_URL/webhooks/tradingview/$SECRET")
  body=$(printf "%s" "$response" | sed '$d')
  code=$(printf "%s" "$response" | tail -n 1)
  echo "$body"

  if [ "$code" != "202" ]; then
    echo "Expected 202 from webhook, got $code" >&2
    return 1
  fi

  sleep "$SLEEP_SECONDS"

  last_status=""
  end_ts=$(( $(date +%s) + STATUS_TIMEOUT_SECONDS ))

  while true; do
    status_response=$(curl -sS -w "\n%{http_code}\n" "$BASE_URL/alerts/status/$last_id")
    status_body=$(printf "%s" "$status_response" | sed '$d')
    status_code=$(printf "%s" "$status_response" | tail -n 1)

    if [ "$status_code" = "200" ]; then
      status=$(printf "%s" "$status_body" | sed -n 's/.*"status":"\([^"]*\)".*/\1/p')
      if [ "$status" != "$last_status" ]; then
        echo "$status_body"
        last_status="$status"
      fi
      if [ -n "$status" ] && [ "$status" != "processing" ]; then
        break
      fi
    elif [ "$status_code" != "404" ]; then
      echo "Expected 200 from status, got $status_code" >&2
      return 1
    fi

    if [ "$STATUS_TIMEOUT_SECONDS" -le 0 ]; then
      break
    fi

    if [ "$(date +%s)" -ge "$end_ts" ]; then
      echo "Timed out waiting for status to leave processing." >&2
      last_status="processing_timeout"
      break
    fi

    sleep "$STATUS_POLL_SECONDS"
  done

  if [ "$status_code" != "200" ]; then
    echo "Expected 200 from status, got $status_code" >&2
    return 1
  fi

  alert_id=$(printf "%s" "$status_body" | sed -n 's/.*"alertId":"\([^"]*\)".*/\1/p')
  if [ -z "$alert_id" ]; then
    echo "Unable to parse alertId from status response." >&2
    return 1
  fi

  last_alert_id="$alert_id"

  snapshot_response=$(curl -sS -w "\n%{http_code}\n" "$BASE_URL/alerts/$alert_id/indicator-snapshot")
  snapshot_body=$(printf "%s" "$snapshot_response" | sed '$d')
  snapshot_code=$(printf "%s" "$snapshot_response" | tail -n 1)
  echo "$snapshot_body"

  if [ "$snapshot_code" != "200" ]; then
    echo "Expected 200 from indicator snapshot, got $snapshot_code" >&2
    return 1
  fi

  echo "Smoke test succeeded for alert $alert_id."
  return 0
}

if [ "$SMOKE_LOOP_SECONDS" -gt 0 ]; then
  end_ts=$(( $(date +%s) + SMOKE_LOOP_SECONDS ))
  total_runs=0
  accepted=0
  rejected=0
  adjudication_failed=0
  other=0

  while [ "$(date +%s)" -lt "$end_ts" ]; do
    total_runs=$((total_runs + 1))
    if run_once; then
      case "$last_status" in
        executed|execution_failed|plan_failed)
          accepted=$((accepted + 1))
          ;;
        rejected)
          rejected=$((rejected + 1))
          ;;
        adjudication_failed)
          adjudication_failed=$((adjudication_failed + 1))
          ;;
        *)
          other=$((other + 1))
          ;;
      esac
    else
      other=$((other + 1))
    fi

    sleep "$SMOKE_LOOP_INTERVAL"
  done

  echo "Smoke loop summary: runs=$total_runs accepted=$accepted rejected=$rejected adjudication_failed=$adjudication_failed other=$other"
  exit 0
fi

run_once
