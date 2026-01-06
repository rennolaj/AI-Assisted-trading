#!/usr/bin/env bash
set -euo pipefail

SYMBOL="${SYMBOL:-PF_ETHUSD}"
INTERVAL_MINUTES="${INTERVAL_MINUTES:-1}"
DURATION_SECONDS="${DURATION_SECONDS:-600}"
SLEEP_SECONDS="${SLEEP_SECONDS:-1}"
BASE_URL="${BASE_URL:-https://futures.kraken.com/derivatives/api/v3}"
OUTPUT="${OUTPUT:-tests/fixtures/kraken-futures/${SYMBOL,,}_m${INTERVAL_MINUTES}.json}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
OUT_DIR="$(dirname "$REPO_ROOT/$OUTPUT")"

mkdir -p "$OUT_DIR"

SYMBOL="$SYMBOL" \
INTERVAL_MINUTES="$INTERVAL_MINUTES" \
DURATION_SECONDS="$DURATION_SECONDS" \
SLEEP_SECONDS="$SLEEP_SECONDS" \
BASE_URL="$BASE_URL" \
OUTPUT="$REPO_ROOT/$OUTPUT" \
python3 - <<'PY'
import json
import os
import time
from datetime import datetime
from urllib.request import urlopen

symbol = os.environ["SYMBOL"]
interval_minutes = int(os.environ["INTERVAL_MINUTES"])
duration_seconds = int(os.environ["DURATION_SECONDS"])
sleep_seconds = float(os.environ["SLEEP_SECONDS"])
base_url = os.environ["BASE_URL"].rstrip("/")
output_path = os.environ["OUTPUT"]

url = f"{base_url}/history?symbol={symbol}&last=100"
end_time = time.time() + duration_seconds

unique = {}
while time.time() < end_time:
    try:
        with urlopen(url) as resp:
            payload = json.load(resp)
    except Exception:
        time.sleep(sleep_seconds)
        continue

    for trade in payload.get("history", []):
        uid = trade.get("uid")
        if not uid:
            uid = f"{trade.get('time')}|{trade.get('price')}|{trade.get('size')}|{trade.get('side')}|{trade.get('trade_id')}"
        unique[uid] = trade

    time.sleep(sleep_seconds)

trades = list(unique.values())

def parse_time(value: str) -> int:
    return int(datetime.fromisoformat(value.replace("Z", "+00:00")).timestamp())

trades.sort(key=lambda t: parse_time(t["time"]))

interval_seconds = interval_minutes * 60
buckets = {}
for trade in trades:
    ts = trade.get("time")
    if not ts:
        continue
    price = float(trade.get("price", 0) or 0)
    size = float(trade.get("size", 0) or 0)
    epoch = parse_time(ts)
    bucket_start = (epoch // interval_seconds) * interval_seconds
    b = buckets.get(bucket_start)
    if b is None:
        b = {
            "open": price,
            "high": price,
            "low": price,
            "close": price,
            "volume": 0.0,
            "count": 0,
            "vwap_sum": 0.0,
            "vwap_size": 0.0,
            "first_epoch": epoch,
            "last_epoch": epoch,
        }
        buckets[bucket_start] = b
    else:
        b["high"] = max(b["high"], price)
        b["low"] = min(b["low"], price)
        if epoch >= b["last_epoch"]:
            b["close"] = price
            b["last_epoch"] = epoch
        if epoch <= b["first_epoch"]:
            b["open"] = price
            b["first_epoch"] = epoch

    b["volume"] += size
    b["count"] += 1
    b["vwap_sum"] += price * size
    b["vwap_size"] += size

candles = []
for bucket_start in sorted(buckets.keys()):
    b = buckets[bucket_start]
    vwap = b["vwap_sum"] / b["vwap_size"] if b["vwap_size"] else b["close"]
    candles.append([
        bucket_start,
        round(b["open"], 5),
        round(b["high"], 5),
        round(b["low"], 5),
        round(b["close"], 5),
        round(vwap, 5),
        round(b["volume"], 8),
        b["count"],
    ])

output = {
    "source": "kraken-futures",
    "symbol": symbol,
    "intervalMinutes": interval_minutes,
    "capturedAtUtc": datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ"),
    "captureDurationSeconds": duration_seconds,
    "tradeCount": len(trades),
    "candles": candles,
}

with open(output_path, "w", encoding="utf-8") as f:
    json.dump(output, f, indent=2)
    f.write("\n")

print(f"Wrote {len(candles)} candles from {len(trades)} trades to {output_path}")
PY
