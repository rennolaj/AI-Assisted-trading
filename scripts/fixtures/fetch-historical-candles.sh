#!/usr/bin/env bash
# Fetch historical OHLC data from Kraken Futures Charts API for backtesting

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

Fetch historical OHLC candles from Kraken Futures Charts API for backtesting.

OPTIONS:
    -h, --help                  Show this help message
    -s, --symbol TEXT           Symbol to fetch (default: PF_XBTUSD)
    -f, --from TEXT             Start date/time (ISO 8601 or YYYY-MM-DD)
    -t, --to TEXT               End date/time (ISO 8601 or YYYY-MM-DD)
    -r, --resolutions TEXT      Comma-separated resolutions (default: 1m,5m,15m)
                                Available: 1m,5m,15m,30m,1h,4h,12h,1d,1w
    -o, --output TEXT           Output directory (default: tests/fixtures/historical)
    -b, --batch-size NUM        Candles per API request (default: 500, max: 500)
    -d, --delay NUM             Delay between API calls in seconds (default: 1.0)
    --base-url TEXT             Charts API base URL (default: production)

EXAMPLES:
    # Fetch BTC data for August 2025 (multiple timeframes)
    $0 --symbol PF_XBTUSD --from 2025-08-01 --to 2025-08-31 --resolutions "1m,5m,15m"

    # Fetch ETH data for specific week
    $0 --symbol PF_ETHUSD --from "2025-08-01T00:00:00Z" --to "2025-08-07T23:59:59Z"

    # Fetch from demo environment
    $0 --symbol PF_XBTUSD --from 2025-08-01 --to 2025-08-15 --base-url https://demo-futures.kraken.com/api/charts/v1

NOTES:
    - Dates can be ISO 8601 or simple YYYY-MM-DD (assumes UTC midnight)
    - Fetches all timeframes in parallel for efficiency
    - Validates timestamp continuity and reports gaps
    - Saves in FixtureMarketDataProvider format
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
SYMBOL="PF_XBTUSD"
FROM_DATE=""
TO_DATE=""
RESOLUTIONS="1m,5m,15m"
OUTPUT_DIR="tests/fixtures/historical"
BATCH_SIZE=500
DELAY=1.0
BASE_URL="https://futures.kraken.com/api/charts/v1"

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -h|--help) usage ;;
        -s|--symbol) SYMBOL="$2"; shift 2 ;;
        -f|--from) FROM_DATE="$2"; shift 2 ;;
        -t|--to) TO_DATE="$2"; shift 2 ;;
        -r|--resolutions) RESOLUTIONS="$2"; shift 2 ;;
        -o|--output) OUTPUT_DIR="$2"; shift 2 ;;
        -b|--batch-size) BATCH_SIZE="$2"; shift 2 ;;
        -d|--delay) DELAY="$2"; shift 2 ;;
        --base-url) BASE_URL="$2"; shift 2 ;;
        *) error "Unknown option: $1" ;;
    esac
done

# Validate required arguments
if [[ -z "$FROM_DATE" ]]; then
    error "Missing required argument: --from"
fi

if [[ -z "$TO_DATE" ]]; then
    error "Missing required argument: --to"
fi

# Validate batch size
if [[ "$BATCH_SIZE" -gt 500 ]]; then
    warn "Batch size capped at 500 (Kraken API limit)"
    BATCH_SIZE=500
fi

info "Fetching historical data for $SYMBOL"
info "Period: $FROM_DATE to $TO_DATE"
info "Resolutions: $RESOLUTIONS"
info "Output: $PROJECT_ROOT/$OUTPUT_DIR"

# Create output directory
OUTPUT_PATH="$PROJECT_ROOT/$OUTPUT_DIR"
mkdir -p "$OUTPUT_PATH"

# Convert resolutions to array
IFS=',' read -ra RESOLUTION_ARRAY <<< "$RESOLUTIONS"

# Fetch each resolution
for RESOLUTION in "${RESOLUTION_ARRAY[@]}"; do
    RESOLUTION=$(echo "$RESOLUTION" | xargs) # trim whitespace
    
    info "Fetching $RESOLUTION candles..."
    
    # Build output filename
    SYMBOL_LOWER=$(echo "$SYMBOL" | tr '[:upper:]' '[:lower:]')
    OUTPUT_FILE="$OUTPUT_PATH/${SYMBOL_LOWER}_${RESOLUTION}.json"
    
    # Call Python script to fetch and process
    SYMBOL="$SYMBOL" \
    FROM_DATE="$FROM_DATE" \
    TO_DATE="$TO_DATE" \
    RESOLUTION="$RESOLUTION" \
    BASE_URL="$BASE_URL" \
    BATCH_SIZE="$BATCH_SIZE" \
    DELAY="$DELAY" \
    OUTPUT_FILE="$OUTPUT_FILE" \
    python3 - <<'PY'
import json
import os
import sys
import time
from datetime import datetime, timezone
from urllib.request import urlopen, Request
from urllib.error import HTTPError, URLError

def parse_date(date_str):
    """Parse ISO 8601 or YYYY-MM-DD format to unix timestamp."""
    try:
        # Try ISO 8601 with timezone
        dt = datetime.fromisoformat(date_str.replace('Z', '+00:00'))
    except ValueError:
        try:
            # Try YYYY-MM-DD (assume UTC midnight)
            dt = datetime.strptime(date_str, '%Y-%m-%d')
            dt = dt.replace(tzinfo=timezone.utc)
        except ValueError:
            raise ValueError(f"Invalid date format: {date_str}. Use ISO 8601 or YYYY-MM-DD")
    
    return int(dt.timestamp())

def resolution_to_minutes(resolution):
    """Convert resolution string to minutes."""
    mapping = {
        '1m': 1, '5m': 5, '15m': 15, '30m': 30,
        '1h': 60, '4h': 240, '12h': 720, '1d': 1440, '1w': 10080
    }
    return mapping.get(resolution.lower(), 1)

def fetch_candles(base_url, symbol, resolution, from_ts, to_ts, batch_size, delay):
    """Fetch historical candles with pagination."""
    candles = []
    current_to = to_ts
    tick_type = "trade"  # Kraken uses "trade" for actual traded prices
    
    url_template = f"{base_url.rstrip('/')}/{tick_type}/{symbol}/{resolution}"
    
    print(f"  Fetching from {datetime.fromtimestamp(from_ts, tz=timezone.utc)} to {datetime.fromtimestamp(to_ts, tz=timezone.utc)}")
    
    batch_num = 0
    while True:
        batch_num += 1
        url = f"{url_template}?count={batch_size}&to={current_to}"
        
        try:
            req = Request(url)
            req.add_header('User-Agent', 'Mvp.Trading/1.0')
            
            with urlopen(req, timeout=10) as resp:
                data = json.load(resp)
            
            batch = data.get('candles', [])
            
            if not batch:
                print(f"  No more data (batch {batch_num})")
                break
            
            print(f"  Batch {batch_num}: Received {len(batch)} candles")
            
            # Convert from Kraken format to array format [timestamp, open, high, low, close, vwap, volume, count]
            for candle in batch:
                ts = int(candle['time']) // 1000  # Convert milliseconds to seconds
                open_price = float(candle['open'])
                high = float(candle['high'])
                low = float(candle['low'])
                close = float(candle['close'])
                volume = float(candle['volume'])
                vwap = (open_price + high + low + close) / 4  # Approximate VWAP
                
                candles.append([ts, open_price, high, low, close, vwap, volume, 1])
            
            # Get earliest timestamp
            earliest_ts = min(int(c['time']) // 1000 for c in batch)
            
            # Check if we've reached the start date
            if earliest_ts <= from_ts:
                print(f"  Reached start date (earliest: {datetime.fromtimestamp(earliest_ts, tz=timezone.utc)})")
                break
            
            # Update cursor for next batch
            current_to = earliest_ts - 1
            
            # Check if more data available
            if not data.get('more_candles', False):
                print(f"  No more candles available")
                break
            
            # Rate limiting
            time.sleep(delay)
            
        except HTTPError as e:
            print(f"  HTTP Error {e.code}: {e.reason}", file=sys.stderr)
            if e.code == 429:
                print(f"  Rate limited, waiting 5 seconds...", file=sys.stderr)
                time.sleep(5)
                continue
            break
        except URLError as e:
            print(f"  URL Error: {e.reason}", file=sys.stderr)
            break
        except Exception as e:
            print(f"  Unexpected error: {e}", file=sys.stderr)
            break
    
    # Filter to exact date range and sort
    candles = [c for c in candles if from_ts <= c[0] <= to_ts]
    candles.sort(key=lambda x: x[0])
    
    print(f"  Total candles: {len(candles)}")
    return candles

def validate_candles(candles, resolution):
    """Validate timestamp continuity and detect gaps."""
    if len(candles) < 2:
        return []
    
    interval_minutes = resolution_to_minutes(resolution)
    interval_seconds = interval_minutes * 60
    
    gaps = []
    for i in range(1, len(candles)):
        prev_ts = candles[i-1][0]
        curr_ts = candles[i][0]
        expected_ts = prev_ts + interval_seconds
        
        if curr_ts != expected_ts:
            gap_seconds = curr_ts - prev_ts
            gap_candles = gap_seconds / interval_seconds - 1
            if gap_candles > 0:
                gaps.append({
                    'after': datetime.fromtimestamp(prev_ts, tz=timezone.utc).isoformat(),
                    'before': datetime.fromtimestamp(curr_ts, tz=timezone.utc).isoformat(),
                    'missing_candles': int(gap_candles)
                })
    
    if gaps:
        print(f"  ⚠️  Found {len(gaps)} gap(s) in data:")
        for gap in gaps[:5]:  # Show first 5 gaps
            print(f"     {gap['missing_candles']} candle(s) missing between {gap['after']} and {gap['before']}")
        if len(gaps) > 5:
            print(f"     ... and {len(gaps) - 5} more gaps")
    
    return gaps

def save_fixture(symbol, resolution, candles, output_file):
    """Save candles in FixtureMarketDataProvider format."""
    interval_minutes = resolution_to_minutes(resolution)
    
    # Build fixture
    fixture = {
        "source": "kraken-futures-charts-api",
        "symbol": symbol,
        "intervalMinutes": interval_minutes,
        "resolution": resolution,
        "capturedAtUtc": datetime.now(timezone.utc).isoformat(),
        "startTime": datetime.fromtimestamp(candles[0][0], tz=timezone.utc).isoformat() if candles else None,
        "endTime": datetime.fromtimestamp(candles[-1][0], tz=timezone.utc).isoformat() if candles else None,
        "candleCount": len(candles),
        "candles": candles
    }
    
    with open(output_file, 'w', encoding='utf-8') as f:
        json.dump(fixture, f, indent=2)
        f.write('\n')
    
    print(f"  ✓ Saved {len(candles)} candles to {output_file}")

# Main execution
symbol = os.environ['SYMBOL']
from_date = os.environ['FROM_DATE']
to_date = os.environ['TO_DATE']
resolution = os.environ['RESOLUTION']
base_url = os.environ['BASE_URL']
batch_size = int(os.environ['BATCH_SIZE'])
delay = float(os.environ['DELAY'])
output_file = os.environ['OUTPUT_FILE']

try:
    from_ts = parse_date(from_date)
    to_ts = parse_date(to_date)
    
    if from_ts >= to_ts:
        print(f"ERROR: from_date must be before to_date", file=sys.stderr)
        sys.exit(1)
    
    # Fetch candles
    candles = fetch_candles(base_url, symbol, resolution, from_ts, to_ts, batch_size, delay)
    
    if not candles:
        print(f"  ⚠️  No candles fetched", file=sys.stderr)
        sys.exit(1)
    
    # Validate
    gaps = validate_candles(candles, resolution)
    
    # Save
    save_fixture(symbol, resolution, candles, output_file)
    
    # Summary
    start_dt = datetime.fromtimestamp(candles[0][0], tz=timezone.utc)
    end_dt = datetime.fromtimestamp(candles[-1][0], tz=timezone.utc)
    duration = end_dt - start_dt
    
    print(f"  Summary:")
    print(f"    - Period: {start_dt.date()} to {end_dt.date()} ({duration.days} days)")
    print(f"    - Candles: {len(candles)}")
    print(f"    - Gaps: {len(gaps)}")
    print(f"    - File: {output_file}")
    
except ValueError as e:
    print(f"ERROR: {e}", file=sys.stderr)
    sys.exit(1)
except Exception as e:
    print(f"ERROR: Unexpected error: {e}", file=sys.stderr)
    import traceback
    traceback.print_exc()
    sys.exit(1)

PY

    if [[ $? -eq 0 ]]; then
        info "✓ Completed $RESOLUTION"
    else
        warn "✗ Failed to fetch $RESOLUTION"
    fi
    
    echo ""
done

info "All resolutions completed!"
info "Fixtures saved to: $OUTPUT_PATH"
echo ""
echo "Next steps:"
echo "1. Review fixtures for data quality: jq '.candleCount' $OUTPUT_PATH/*.json"
echo "2. Test with FixtureMarketDataProvider: MARKETDATA_MODE=fixtures"
echo "3. Use simulate-alert-at-time.sh to generate test scenarios"
