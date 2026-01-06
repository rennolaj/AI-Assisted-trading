# Alert Dataflow Overview

## Purpose
This note captures what happens after an alert is received, what data is collected, and what is persisted so we can revisit it during upgrades.

## Alert Flow (High Level)
1) API receives TradingView webhook and stores:
   - raw payload
   - normalized `AlertEvent`
2) Alert is queued to Redis.
3) Worker dequeues alert, computes indicators, generates Elliott candidates, then adjudicates.
4) If approved, a trade plan is built and execution is attempted.

## Stored Artifacts
- `alerts` table:
  - raw webhook payload
  - normalized `AlertEvent` (ticker, exchange, interval, optional close/volume, intent)
- `indicator_snapshots` table:
  - multi-timeframe indicator snapshot (RSI/Stoch/MACD/Volume states, score, risk)
- `elliott_candidates` table:
  - Elliott candidate set + invalidation levels
- Trade plan and execution:
  - `trade_plan`, `execution_intent`, `order_receipt`, `fill_receipt`, `execution_heartbeat`

## Data Collected Per Alert
Indicators (`IndicatorEngine`):
- Requests OHLCV per configured timeframe.
- Uses `Indicator:LookbackDays` (default = 1 day) when set.
- For scalping defaults:
  - M5: 288 bars
  - M15: 96 bars
  - M30: 48 bars
  - H1: 24 bars
  - H2: 12 bars

Elliott (`ElliottEngine`):
- Requests OHLCV for the base timeframe.
- Uses `Elliott:LookbackDays` (default = 1 day).
- With base `M1`, that is ~1440 bars (clamped by min/max).

## What We Do Not Store Yet
- Raw candle series per alert is not persisted; only derived snapshots/candidates.
- If deeper auditing is needed, add a candle snapshot table keyed by `alert_id`.

## Tuning Knobs
- `Indicator:LookbackDays`
- `Indicator:LookbackBars`
- `Elliott:LookbackDays`
- `Elliott:BaseTimeframe`
