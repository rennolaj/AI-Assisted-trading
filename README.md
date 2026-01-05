# AI-Assisted-trading
This repository contains everything about my own AI assisted trading server.

## Tooling
- .NET SDK: 10.0.x (installed via Homebrew cask `dotnet-sdk`)
- Scripts: `scripts/restore.sh`, `scripts/build.sh`, `scripts/test.sh`

## Quick start
```bash
brew install --cask dotnet-sdk
./scripts/restore.sh
./scripts/build.sh
./scripts/test.sh
```

## Docker
```bash
cp .env.example .env
docker compose up --build
```
Set `KRAKEN_FUTURES_ENV=demo` (sandbox) or `KRAKEN_FUTURES_ENV=prod` (live) in `.env` to switch Kraken Futures environments.
Endpoint defaults live in `config/kraken-futures.json`.

Health checks:
- `http://localhost:8080/health`
- `http://localhost:8080/health/dependencies`

Trade monitoring seed:
- `POST http://localhost:8080/trades/open`
Example:
```bash
curl -X POST http://localhost:8080/trades/open \
  -H "Content-Type: application/json" \
  -d '{"exchangeId":"kraken-futures","symbol":"PI_XBTUSD","side":"LONG","entryPrice":70000,"invalidationPrice":68000}'
```

## Kraken integration tests
These are disabled by default. To run them against demo endpoints:
```bash
export KRAKEN_FUTURES_INTEGRATION_TESTS=1
export KRAKEN_FUTURES_REST_BASE=https://demo-futures.kraken.com/derivatives/api/v3
export KRAKEN_FUTURES_TEST_SYMBOL=PI_XBTUSD
./scripts/test.sh
```

## Runtime configuration
- `TradingView:WebhookSecret`
- `Postgres:ConnectionString`
- `Redis:ConnectionString`
- `Redis:AlertQueueKey` (default: `mvp:alerts`)
- `Worker:PollIntervalMs`
- `KrakenFutures:Environment`
- `KrakenFutures:BaseUrl`
- `KrakenFutures:AuthBaseUrl`
- `KrakenFutures:WebSocketUrl`
- `KrakenFutures:TestSymbol`
- `KrakenFutures:ApiKey`
- `KrakenFutures:ApiSecret`
- `KrakenFutures:TimeoutSeconds`
- `KrakenFutures:Cache:InstrumentsTtlSeconds`
- `KrakenFutures:Cache:TickersTtlSeconds`
- `KrakenFutures:Cache:CandlesTtlSeconds`
- `KrakenFutures:RateLimit:MaxCostPerWindow`
- `KrakenFutures:RateLimit:WindowSeconds`
- `KrakenFutures:RateLimit:InstrumentsCost`
- `KrakenFutures:RateLimit:TickersCost`
- `KrakenFutures:RateLimit:CandlesCost`

## Local services
`./scripts/build.sh` will install and start Postgres/Redis and create the `ai-trading-db` database by default. To disable, set `DEV_BOOTSTRAP=0`.
On Linux, `scripts/dev/bootstrap.sh` uses `apt-get` and may require `sudo`. If your Postgres uses a non-default superuser, set `PG_USER`.

## Container notes
When running in containers, set connection strings via environment variables so the API can reach sibling Postgres/Redis containers on the same network, for example:
`Postgres__ConnectionString=Host=postgres;Port=5432;Database=ai-trading-db;Username=postgres;Password=postgres`
`Redis__ConnectionString=redis:6379`
