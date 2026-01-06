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

## Public endpoints (no API key)
| Endpoint | Description |
| --- | --- |
| `/instruments` | List all available futures instruments |
| `/tickers` | Get current ticker data |

## Instrument symbols
Instrument symbols must be discovered dynamically via the `/instruments` endpoint.
Example: `PI_XBTUSD` (Bitcoin perpetual futures).

## Recommended MVP environment variables (demo values)
| Variable | Example |
| --- | --- |
| `KRAKEN_FUTURES_ENV` | `demo` |
| `KRAKEN_FUTURES_REST_BASE` | `https://demo-futures.kraken.com/derivatives/api/v3` |
| `KRAKEN_FUTURES_AUTH_BASE` | `https://demo-futures.kraken.com/api/auth/v1` |
| `KRAKEN_FUTURES_WS_URL` | `wss://demo-futures.kraken.com/ws/v1` |
| `KRAKEN_FUTURES_TEST_SYMBOL` | `PI_XBTUSD` |
| `KRAKEN_FUTURES_INTEGRATION_TESTS` | `1` |
| `TRADING_ENABLED` | `0` |
| `MARKETDATA_MODE` | `fixtures` |
| `MARKETDATA_FIXTURE_PATH` | `fixtures/kraken-futures` |
| `MARKETDATA_EXTEND_FIXTURES` | `true` |

## Secrets (excluded)
`KRAKEN_FUTURES_API_KEY` and `KRAKEN_FUTURES_API_SECRET` are required only for private endpoints and must never be committed to source control.

## Final notes
With this configuration complete, the MVP can validate Kraken Futures public integration.
Only secure API credentials remain to enable private trading operations.
For demo E2E validation, `MARKETDATA_MODE=fixtures` keeps execution on Kraken demo while using fixture OHLCV for indicators.
