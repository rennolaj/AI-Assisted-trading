#!/usr/bin/env bash
# Extract candles with significant price movements from Kraken Charts API format (array-based)
# FULLY GENERIC: Works for any date range and any time-of-day window

set -euo pipefail

FIXTURE_FILE="${1:-tests/fixtures/historical/pf_xbtusd_5m.json}"
MIN_MOVE_PCT="${2:-1.0}"       # Minimum price movement % (default: 1%)
START_DATE="${3:-}"            # ISO 8601: 2026-01-08T00:00:00Z (optional)
END_DATE="${4:-}"              # ISO 8601: 2026-01-12T23:59:59Z (optional)
START_TIME="${5:-04:30}"       # Time of day start (default: 04:30 UTC - morning session)
END_TIME="${6:-07:30}"         # Time of day end (default: 07:30 UTC - morning session)

# If dates not provided, use last 4 days of dataset
if [[ -z "$START_DATE" ]]; then
  # Get start/end from file metadata
  START_DATE=$(jq -r '.startTime' "$FIXTURE_FILE")
  END_DATE=$(jq -r '.endTime' "$FIXTURE_FILE")
  echo "Using file date range: $START_DATE to $END_DATE" >&2
fi

echo "Extracting volatile candles from $FIXTURE_FILE" >&2
echo "Date range: $START_DATE to $END_DATE" >&2
echo "Time window: $START_TIME - $END_TIME UTC" >&2
echo "Minimum movement: $MIN_MOVE_PCT%" >&2
echo "" >&2

# Convert dates to timestamps
START_TS=$(date -j -u -f "%Y-%m-%dT%H:%M:%S" "${START_DATE%+*}" "+%s" 2>/dev/null || echo "0")
END_TS=$(date -j -u -f "%Y-%m-%dT%H:%M:%S" "${END_DATE%+*}" "+%s" 2>/dev/null || echo "9999999999")

jq -r --arg min_pct "$MIN_MOVE_PCT" --arg start_ts "$START_TS" --arg end_ts "$END_TS" \
      --arg start_time "$START_TIME" --arg end_time "$END_TIME" '
  .candles[] |
  # Array format: [timestamp, open, high, low, close, vwap, volume, count]
  select(
    # Filter by timestamp range
    (.[0] >= ($start_ts | tonumber) and .[0] <= ($end_ts | tonumber))
  ) |
  # Convert timestamp to time string for filtering
  (.[0] | strftime("%H:%M")) as $time |
  select(
    # Filter by time of day
    ($time >= $start_time and $time <= $end_time)
  ) |
  # Calculate price movement
  (((.[2] - .[3]) / .[3] * 100)) as $move_pct |
  select(
    # Filter by minimum price movement
    ($move_pct >= ($min_pct | tonumber))
  ) |
  # Output: timestamp (ISO), open, high, low, close, movement%
  [(.[0] | strftime("%Y-%m-%dT%H:%M:%SZ")), .[1], .[2], .[3], .[4], ($move_pct | tostring + "%")] |
  @tsv
' "$FIXTURE_FILE"
