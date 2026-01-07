# Kraken Futures API Findings (Candles vs Trades)

Draft date: 2026-01-07

## Summary
Kraken Futures v3 `/history` returns trade fills (not OHLC candles) and is capped at ~100 trades per request.
The `last`, `interval`, and `since` parameters do not increase the returned trade count, so it is not possible to fetch enough OHLC bars for multi-timeframe indicators from this endpoint alone.

Kraken provides a separate **Charts API** (`/api/charts/v1`) that does return OHLC candles and supports `count`, `from`, and `to`.
This is the correct source for futures OHLC in the MVP.

## Endpoints Tested
Base URLs (v3 history):
- Demo: `https://demo-futures.kraken.com/derivatives/api/v3`
- Prod: `https://futures.kraken.com/derivatives/api/v3`

Charts API base:
- Demo: `https://demo-futures.kraken.com/api/charts/v1`
- Prod: `https://futures.kraken.com/api/charts/v1`

Requests:
- `/history?symbol=PI_XBTUSD&interval=5&last=10`
- `/history?symbol=PI_XBTUSD&interval=5&last=1000`
- `/history?symbol=PI_XBTUSD&since=2026-01-06T00:00:00Z`
- `/history?symbol=PI_XBTUSD&since=0`
- `/ohlc?symbol=PI_XBTUSD&interval=5&last=10` (404)
- `/candles?symbol=PI_XBTUSD&interval=5&last=10` (404)

Charts requests:
- `/` (tick types: `mark`, `spot`, `trade`)
- `/trade` (market symbols, includes `PI_XBTUSD`)
- `/trade/PI_XBTUSD` (resolutions, includes `1m`, `5m`, `15m`, `30m`, `1h`, `4h`, `12h`, `1d`, `1w`)
- `/trade/PI_XBTUSD/5m?count=500` (returns `candles` array, `more_candles=false`)

Results (demo and prod):
- `/history` responds with `history` array (trade fills only).
- Response length remains ~100 trades regardless of `last`, `interval`, `since`.
- `/ohlc` and `/candles` return 404.
- Charts API returns OHLC candles with `count` respected (tested `count=500` on demo + prod).
 - Charts pagination works with `to` (seconds) to walk back in time; `count` is capped per request (500).

## Current Behavior in the Codebase
- `KrakenFuturesMarketDataProvider` falls back to `BuildCandlesFromHistory` when the response contains `history`.
- Candles are synthesized from the limited trade window.
- With ~100 trades total, the resulting OHLC series is too short for RSI/MACD/Stoch/Volume to initialize across M5/M15/M30/H1/H2.

## Impact
- Indicator snapshots end in `INSUFFICIENT_DATA` for every timeframe.
- LLM adjudication correctly fails closed (`REJECT`) due to `dataIntegrity=false`.
- Lookback configuration changes alone cannot fix this because the upstream data is limited.

## Options to Resolve
1) **Charts API integration** for OHLC (`/api/charts/v1/{tick_type}/{symbol}/{resolution}`).
2) **Trade pagination** (if supported): fetch multiple pages of trade history and aggregate enough OHLC bars.
3) **Alternate OHLC source** for indicators (e.g., Kraken Spot OHLC or another market data provider).
4) **Low-data mode**: reduce indicators/periods + timeframes, explicitly accept lower confidence.

## Notes
- Any resolution should add explicit exchange-level limits and fallback behavior to avoid silent `INSUFFICIENT_DATA`.
