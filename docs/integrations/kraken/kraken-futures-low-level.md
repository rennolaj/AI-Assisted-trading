# Kraken Futures Low-Level Requirements

## Sources
- SRS and LLD references to Kraken Futures v3 REST docs
- Tools/Credentials Manual (API key/secret storage rules)

## Base URLs
- Prod: https://futures.kraken.com/derivatives/api/v3
- Demo: https://demo-futures.kraken.com/derivatives/api/v3

## Authentication (Private Endpoints)
- Kraken Futures v3 uses `apiKey` and `authent` headers.
- Private endpoints require request signing; nonce rules are per v3 docs.
- Only the ExecutionService should hold trading keys.

## Rate Limits
- Derivatives endpoints use a cost budget of 500 per 10 seconds.
- Public endpoints cost 0 (per referenced docs).
- When budget is exhausted, fail closed and do not execute trades.

## Caching (M2)
- Cache instruments for ~5 minutes.
- Cache tickers for ~2 seconds.
- Cache candles for ~5 seconds per (symbol, interval, lookback) tuple.

## Required Endpoints (MVP)
### Public market data
- Instruments list
- Tickers snapshot
- Charts API candles (preferred source for OHLC)
- Trade history (fallback only; capped to ~100 trades per request)

### Private trading (later in MVP)
- Check API key permissions
- Send order
- Get open orders
- Cancel order
- Dead-man's switch (Cancel all orders after)

## Safety Rules
- Cache public endpoints where possible.
- Use the Charts API for OHLC. Do not rely on `history` for indicator lookbacks.
- If charts is unavailable, use fixtures or an alternate OHLC provider until a pagination strategy exists.
- Dead-man's switch must be set and refreshed while trading is enabled.

## Configuration Keys
- `KrakenFutures:Environment`
- `KrakenFutures:BaseUrl`
- `KrakenFutures:AuthBaseUrl`
- `KrakenFutures:WebSocketUrl`
- `KrakenFutures:ChartsBaseUrl`
- `KrakenFutures:ChartsTickType`
- `KrakenFutures:ChartsMaxCandlesPerRequest`
- `KrakenFutures:ChartsMaxBatches`
- `KrakenFutures:ChartsFallbackToHistory`
- `KrakenFutures:TestSymbol`
- `KrakenFutures:ApiKey`
- `KrakenFutures:ApiSecret`
- `KrakenFutures:DemoApiKey`
- `KrakenFutures:DemoApiSecret`
- `KrakenFutures:ProdApiKey`
- `KrakenFutures:ProdApiSecret`
- `KrakenFutures:TimeoutSeconds`
