#!/usr/bin/env bash
# Simulate a TradingView alert at a specific timestamp using fixture data

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# Colors
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

usage() {
    cat <<EOF
Usage: $0 [OPTIONS]

Simulate a TradingView alert at a specific historical timestamp using fixture data.
The system must be configured to use FixtureMarketDataProvider (MARKETDATA_MODE=fixtures).

OPTIONS:
    -h, --help                  Show this help message
    -f, --fixture TEXT          Path to fixture JSON file (required)
    -t, --timestamp TEXT        Target timestamp (Unix seconds or ISO 8601)
                                Or use --candle-index instead
    -i, --candle-index NUM      Use Nth candle from fixture (0-based, alternative to --timestamp)
    -d, --direction TEXT        LONG or SHORT (required)
    -r, --reason TEXT           Reason/description for the alert
    --symbol TEXT               Override symbol (default: from fixture)
    --exchange TEXT             Exchange name (default: KRAKEN)
    --interval TEXT             Timeframe interval (default: from fixture)
    --dry-run                   Show webhook payload without sending

EXAMPLES:
    # Simulate alert at candle index 3600 (middle of 5-day fixture)
    $0 -f tests/fixtures/kraken-futures/btcusd_p_m1_varied.json -i 3600 -d LONG -r "Testing impulse wave"

    # Simulate alert at specific timestamp
    $0 -f tests/fixtures/kraken-futures/btcusd_p_m1_varied.json -t 1767400000 -d SHORT -r "Wave 5 completion"

    # Dry run to preview webhook
    $0 -f tests/fixtures/kraken-futures/btcusd_p_m1_varied.json -i 1000 -d LONG --dry-run

NOTES:
    - System must be running with MARKETDATA_MODE=fixtures
    - Fixture must contain the target timestamp
    - Will POST webhook to local API via ngrok
    - Alert will be processed with fixture data (no real API calls)
    - Use capture-llm-decision.sh afterwards to save the result
EOF
    exit 0
}

info() {
    echo -e "${GREEN}INFO: $1${NC}"
}

warn() {
    echo -e "${YELLOW}WARN: $1${NC}"
}

error() {
    echo -e "\033[0;31mERROR: $1${NC}" >&2
    exit 1
}

# Defaults
FIXTURE_FILE=""
TIMESTAMP=""
CANDLE_INDEX=""
DIRECTION=""
REASON="Historical backtest simulation"
SYMBOL_OVERRIDE=""
EXCHANGE="KRAKEN"
INTERVAL_OVERRIDE=""
DRY_RUN=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -h|--help) usage ;;
        -f|--fixture) FIXTURE_FILE="$2"; shift 2 ;;
        -t|--timestamp) TIMESTAMP="$2"; shift 2 ;;
        -i|--candle-index) CANDLE_INDEX="$2"; shift 2 ;;
        -d|--direction) DIRECTION="$2"; shift 2 ;;
        -r|--reason) REASON="$2"; shift 2 ;;
        --symbol) SYMBOL_OVERRIDE="$2"; shift 2 ;;
        --exchange) EXCHANGE="$2"; shift 2 ;;
        --interval) INTERVAL_OVERRIDE="$2"; shift 2 ;;
        --dry-run) DRY_RUN=true; shift ;;
        *) error "Unknown option: $1" ;;
    esac
done

# Validate required arguments
if [[ -z "$FIXTURE_FILE" ]]; then
    error "Missing required argument: --fixture"
fi

if [[ ! -f "$FIXTURE_FILE" ]]; then
    error "Fixture file not found: $FIXTURE_FILE"
fi

if [[ -z "$DIRECTION" ]]; then
    error "Missing required argument: --direction (LONG or SHORT)"
fi

if [[ "$DIRECTION" != "LONG" && "$DIRECTION" != "SHORT" ]]; then
    error "Direction must be LONG or SHORT"
fi

if [[ -z "$TIMESTAMP" && -z "$CANDLE_INDEX" ]]; then
    error "Must specify either --timestamp or --candle-index"
fi

if [[ -n "$TIMESTAMP" && -n "$CANDLE_INDEX" ]]; then
    error "Cannot specify both --timestamp and --candle-index"
fi

info "Loading fixture: $FIXTURE_FILE"

# Extract candle data using Python
TIMESTAMP="$TIMESTAMP" \
CANDLE_INDEX="$CANDLE_INDEX" \
FIXTURE_FILE="$FIXTURE_FILE" \
SYMBOL_OVERRIDE="$SYMBOL_OVERRIDE" \
EXCHANGE="$EXCHANGE" \
INTERVAL_OVERRIDE="$INTERVAL_OVERRIDE" \
DIRECTION="$DIRECTION" \
REASON="$REASON" \
DRY_RUN="$DRY_RUN" \
python3 - <<'PY'
import json
import os
import sys
from datetime import datetime, timezone
from urllib.request import urlopen, Request
from urllib.error import HTTPError

def parse_timestamp(ts_str):
    """Parse timestamp (Unix seconds or ISO 8601)."""
    try:
        # Try as Unix timestamp
        return int(ts_str)
    except ValueError:
        # Try as ISO 8601
        dt = datetime.fromisoformat(ts_str.replace('Z', '+00:00'))
        return int(dt.timestamp())

def interval_minutes_to_string(minutes):
    """Convert interval minutes to string format."""
    if minutes == 1:
        return "1m"
    elif minutes == 5:
        return "5m"
    elif minutes == 15:
        return "15m"
    elif minutes == 30:
        return "30m"
    elif minutes == 60:
        return "1h"
    elif minutes == 240:
        return "4h"
    elif minutes == 1440:
        return "1d"
    else:
        return f"{minutes}m"

# Load environment variables
fixture_file = os.environ['FIXTURE_FILE']
timestamp_str = os.environ.get('TIMESTAMP', '')
candle_index_str = os.environ.get('CANDLE_INDEX', '')
symbol_override = os.environ.get('SYMBOL_OVERRIDE', '')
exchange = os.environ['EXCHANGE']
interval_override = os.environ.get('INTERVAL_OVERRIDE', '')
direction = os.environ['DIRECTION']
reason = os.environ['REASON']
dry_run = os.environ['DRY_RUN'] == 'true'

# Load fixture
with open(fixture_file, 'r') as f:
    fixture = json.load(f)

symbol = symbol_override if symbol_override else fixture.get('symbol', 'UNKNOWN')
interval_minutes = fixture.get('intervalMinutes', 1)
interval_str = interval_override if interval_override else interval_minutes_to_string(interval_minutes)
candles = fixture.get('candles', [])

if not candles:
    print(f"ERROR: Fixture has no candles", file=sys.stderr)
    sys.exit(1)

print(f"  Fixture: {symbol}, {len(candles)} candles, {interval_str} interval")

# Find target candle
target_candle = None
target_index = None

if candle_index_str:
    # Use candle index
    index = int(candle_index_str)
    if index < 0 or index >= len(candles):
        print(f"ERROR: Candle index {index} out of range (0-{len(candles)-1})", file=sys.stderr)
        sys.exit(1)
    target_candle = candles[index]
    target_index = index
    print(f"  Using candle index: {index}")
else:
    # Use timestamp
    target_ts = parse_timestamp(timestamp_str)
    
    # Find closest candle
    for i, candle in enumerate(candles):
        candle_ts = candle[0]
        if candle_ts == target_ts:
            target_candle = candle
            target_index = i
            break
    
    if target_candle is None:
        # Find closest
        closest_idx = min(range(len(candles)), key=lambda i: abs(candles[i][0] - target_ts))
        closest_ts = candles[closest_idx][0]
        diff = abs(closest_ts - target_ts)
        
        print(f"  ⚠️  Exact timestamp {target_ts} not found in fixture", file=sys.stderr)
        print(f"  Closest candle: index {closest_idx}, timestamp {closest_ts} (diff: {diff}s)", file=sys.stderr)
        
        if diff > interval_minutes * 60:
            print(f"  ERROR: Closest candle is {diff}s away (> {interval_minutes * 60}s)", file=sys.stderr)
            sys.exit(1)
        
        target_candle = candles[closest_idx]
        target_index = closest_idx
        print(f"  Using closest candle at index {closest_idx}")

# Extract candle data [timestamp, open, high, low, close, vwap, volume, count]
candle_ts = target_candle[0]
candle_open = target_candle[1]
candle_high = target_candle[2]
candle_low = target_candle[3]
candle_close = target_candle[4]
candle_volume = target_candle[6] if len(target_candle) > 6 else 0

candle_dt = datetime.fromtimestamp(candle_ts, tz=timezone.utc)

print(f"  Target candle:")
print(f"    - Index: {target_index}")
print(f"    - Time: {candle_dt.isoformat()}")
print(f"    - OHLC: {candle_open:.2f} / {candle_high:.2f} / {candle_low:.2f} / {candle_close:.2f}")
print(f"    - Volume: {candle_volume:.2f}")

# Generate idempotency key
idempotency_key = f"backtest-{candle_ts}"

# Build webhook payload
webhook = {
    "idempotencyKey": idempotency_key,
    "ticker": "BTC/USD",  # TradingView format
    "exchange": exchange,
    "interval": interval_str,
    "close": float(candle_close),
    "volume": float(candle_volume),
    "directionHint": direction,
    "symbolHint": symbol,  # Kraken format
    "reason": f"{reason} (timestamp: {candle_ts}, index: {target_index})"
}

print(f"")
print(f"  Webhook payload:")
print(f"    - Idempotency: {idempotency_key}")
print(f"    - Symbol: {webhook['ticker']} ({webhook['symbolHint']})")
print(f"    - Direction: {direction}")
print(f"    - Close: ${candle_close:.2f}")
print(f"    - Reason: {webhook['reason']}")

if dry_run:
    print(f"")
    print(f"  DRY RUN - Payload:")
    print(json.dumps(webhook, indent=2))
    print(f"")
    print(f"  To send this webhook, remove --dry-run flag")
    sys.exit(0)

# Get ngrok URL
print(f"")
print(f"  Fetching ngrok URL...")

try:
    with urlopen('http://localhost:4040/api/tunnels', timeout=5) as resp:
        ngrok_data = json.load(resp)
    
    tunnels = ngrok_data.get('tunnels', [])
    if not tunnels:
        print(f"ERROR: No ngrok tunnels found. Is ngrok running?", file=sys.stderr)
        sys.exit(1)
    
    ngrok_url = tunnels[0].get('public_url', '')
    if not ngrok_url:
        print(f"ERROR: Could not get ngrok public URL", file=sys.stderr)
        sys.exit(1)
    
    print(f"  Ngrok URL: {ngrok_url}")

except Exception as e:
    print(f"ERROR: Failed to get ngrok URL: {e}", file=sys.stderr)
    print(f"  Make sure ngrok is running: docker compose up ngrok", file=sys.stderr)
    sys.exit(1)

# Send webhook
webhook_url = f"{ngrok_url}/webhooks/tradingview/REDACTED_WEBHOOK_SECRET"

print(f"")
print(f"  Sending webhook to API...")

try:
    req = Request(webhook_url, method='POST')
    req.add_header('Content-Type', 'application/json')
    
    payload_bytes = json.dumps(webhook).encode('utf-8')
    
    with urlopen(req, data=payload_bytes, timeout=10) as resp:
        status = resp.status
        response_text = resp.read().decode('utf-8')
    
    print(f"  ✓ Response: {status}")
    if response_text:
        print(f"  Response body: {response_text[:200]}")
    
    print(f"")
    print(f"  ✓ Alert submitted successfully!")
    print(f"")
    print(f"  Next steps:")
    print(f"    1. Wait 30-60 seconds for processing")
    print(f"    2. Check worker logs: docker logs ai-assisted-worker-1 --tail 50")
    print(f"    3. Capture fixture: ./scripts/fixtures/capture-llm-decision.sh {idempotency_key}")
    print(f"")

except HTTPError as e:
    print(f"ERROR: HTTP {e.code}: {e.reason}", file=sys.stderr)
    try:
        error_body = e.read().decode('utf-8')
        print(f"  Response: {error_body}", file=sys.stderr)
    except:
        pass
    sys.exit(1)
except Exception as e:
    print(f"ERROR: Failed to send webhook: {e}", file=sys.stderr)
    sys.exit(1)

PY
