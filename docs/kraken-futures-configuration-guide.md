# Kraken Futures Configuration Guide (MVP)

Draft date: 2026-01-05

## Purpose
This guide removes ambiguity around Kraken Futures endpoints, symbols, and environment configuration.
All values here are non-secret; the only remaining setup step is adding API credentials for private endpoints.

## Supported environments
| Environment | REST base URL | WebSocket URL |
| --- | --- | --- |
| Demo (Sandbox) | https://demo-futures.kraken.com/derivatives/api/v3 | wss://demo-futures.kraken.com/ws/v1 |
| Production | https://futures.kraken.com/derivatives/api/v3 | wss://futures.kraken.com/ws/v1 |

## REST API structure
Kraken Futures uses two REST base paths:
- Market data and trading: `/derivatives/api/v3` (public + private endpoints)
- Auth and key validation: `/api/auth/v1` (private endpoints only)
Additionally, OHLC candles come from the public Charts API: `/api/charts/v1`.

## Public endpoints (no API key)
| Endpoint | Description |
| --- | --- |
| `/instruments` | List all available futures instruments |
| `/tickers` | Get current ticker data |
| `/history` | Trade history only (used to synthesize candles) |
| Charts API `/trade/{symbol}/{resolution}` | OHLC candles (preferred for indicators) |

## Instrument symbols
Instrument symbols must be discovered dynamically via the `/instruments` endpoint.
Example: `BTCUSD.P` (Bitcoin perpetual futures).

## Recommended MVP environment variables (demo values)
| Variable | Example |
| --- | --- |
| `KRAKEN_FUTURES_ENV` | `demo` |
| `KRAKEN_FUTURES_REST_BASE` | `https://demo-futures.kraken.com/derivatives/api/v3` |
| `KRAKEN_FUTURES_AUTH_BASE` | `https://demo-futures.kraken.com/api/auth/v1` |
| `KRAKEN_FUTURES_WS_URL` | `wss://demo-futures.kraken.com/ws/v1` |
| `KRAKEN_FUTURES_CHARTS_BASE` | `https://demo-futures.kraken.com/api/charts/v1` |
| `KRAKEN_FUTURES_CHARTS_TICK_TYPE` | `trade` |
| `KRAKEN_FUTURES_CHARTS_MAX_CANDLES` | `500` |
| `KRAKEN_FUTURES_CHARTS_MAX_BATCHES` | `10` |
| `KRAKEN_FUTURES_CHARTS_FALLBACK_TO_HISTORY` | `true` |
| `KRAKEN_FUTURES_TEST_SYMBOL` | `BTCUSD.P` |
| `KRAKEN_FUTURES_DEMO_API_KEY` | `...` |
| `KRAKEN_FUTURES_DEMO_API_SECRET` | `...` |
| `KRAKEN_FUTURES_PROD_API_KEY` | `...` |
| `KRAKEN_FUTURES_PROD_API_SECRET` | `...` |
| `KRAKEN_FUTURES_INTEGRATION_TESTS` | `1` |
| `TRADING_ENABLED` | `0` |
| `MARKETDATA_MODE` | `fixtures` |
| `MARKETDATA_FIXTURE_PATH` | `fixtures/kraken-futures` |
| `MARKETDATA_EXTEND_FIXTURES` | `true` |
| `INDICATOR_LOOKBACK_DAYS` | `1` |
| `INDICATOR_LOOKBACK_DAYS_M5` | `1` |
| `INDICATOR_LOOKBACK_DAYS_M15` | `1` |
| `INDICATOR_LOOKBACK_DAYS_M30` | `1` |
| `INDICATOR_LOOKBACK_DAYS_H1` | `2` |
| `INDICATOR_LOOKBACK_DAYS_H2` | `3` |

`INDICATOR_LOOKBACK_DAYS_*` overrides map to `Indicator:LookbackDaysByTimeframe` and take priority over the global `INDICATOR_LOOKBACK_DAYS`.

## Secrets (excluded)
`KRAKEN_FUTURES_API_KEY`, `KRAKEN_FUTURES_API_SECRET`, `KRAKEN_FUTURES_DEMO_API_KEY`, `KRAKEN_FUTURES_DEMO_API_SECRET`, `KRAKEN_FUTURES_PROD_API_KEY`, and `KRAKEN_FUTURES_PROD_API_SECRET` are required only for private endpoints and must never be committed to source control.

## Final notes
With this configuration complete, the MVP can validate Kraken Futures public integration.
Only secure API credentials remain to enable private trading operations.
For demo E2E validation, `MARKETDATA_MODE=fixtures` keeps execution on Kraken demo while using fixture OHLCV for indicators.
If you need per-timeframe overrides, use `INDICATOR_LOOKBACK_DAYS_M5`/`M15`/`M30`/`H1`/`H2`. The engine still enforces indicator minimum bars, so short windows may be expanded automatically to meet RSI/MACD/Stoch/volume requirements.
Kraken Futures v3 does not expose OHLC endpoints (`/ohlc`/`/candles` return 404). The `history` endpoint is capped to ~100 trades and is not enough for multi-timeframe indicators.
Use the Charts API for OHLC (`/api/charts/v1/trade/{symbol}/{resolution}`) and fetch valid symbols from `/api/charts/v1/trade`.
See `docs/kraken-futures-api-findings.md` for verified endpoint behavior and samples.
Charts requests are capped per call (default 500); pagination uses the `to` parameter (seconds). If charts cannot satisfy the requested bars, the provider can fall back to trade-history candles when enabled.
