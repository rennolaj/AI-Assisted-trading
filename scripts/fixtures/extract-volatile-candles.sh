#!/usr/bin/env bash
# Extract candles with significant price movements from a specified date and time range
# FULLY GENERIC: Works for any date range and any time-of-day window

set -euo pipefail

FIXTURE_FILE="${1:-tests/fixtures/kraken-futures/pf_xbtusd_m5.json}"
MIN_MOVE_PCT="${2:-1.0}"       # Minimum price movement % (default: 1%)
START_DATE="${3:-}"            # ISO 8601: 2026-01-08T00:00:00Z (optional)
END_DATE="${4:-}"              # ISO 8601: 2026-01-12T23:59:59Z (optional)
START_TIME="${5:-04:30}"       # Time of day start (default: 04:30 UTC - morning session)
END_TIME="${6:-07:30}"         # Time of day end (default: 07:30 UTC - morning session)

# If dates not provided, use last 4 days of dataset
if [[ -z "$START_DATE" ]]; then
  # Calculate dates dynamically from fixture file
  LAST_TIMESTAMP=$(jq -r '.candles[-1].time' "$FIXTURE_FILE")
  START_DATE=$(date -u -d "$LAST_TIMESTAMP - 4 days" +%Y-%m-%dT00:00:00Z 2>/dev/null || \
               date -u -v-4d -j -f "%Y-%m-%dT%H:%M:%SZ" "$LAST_TIMESTAMP" +%Y-%m-%dT00:00:00Z)
  END_DATE="$LAST_TIMESTAMP"
fi

echo "Extracting volatile candles from $FIXTURE_FILE" >&2
echo "Date range: $START_DATE to $END_DATE" >&2
echo "Time window: $START_TIME - $END_TIME UTC" >&2
echo "Minimum movement: $MIN_MOVE_PCT%" >&2
echo "" >&2

jq -r --arg min_pct "$MIN_MOVE_PCT" --arg start_date "$START_DATE" --arg end_date "$END_DATE" \
      --arg start_time "$START_TIME" --arg end_time "$END_TIME" '
  .candles[] | 
  select(
    # Filter by date range
    (.time >= $start_date and .time <= $end_date)
  ) |
  select(
    # Filter by time of day (flexible - uses START_TIME and END_TIME variables)
    (.time | fromdateiso8601 | strftime("%H:%M") | 
     (. >= $start_time and . <= $end_time))
  ) |
  select(
    # Filter by minimum price movement
    (((.high - .low) / .low * 100) >= ($min_pct | tonumber))
  ) |
  [.time, .open, .high, .low, .close, 
   ((.high - .low) / .low * 100 | tostring + "%")] |
  @tsv
' "$FIXTURE_FILE"
