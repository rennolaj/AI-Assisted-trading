# Local Development

## Prerequisites

- .NET SDK 10.0.x (via `brew install --cask dotnet-sdk`)
- Docker Desktop
- (Optional) ngrok account for TradingView webhook testing

## Build and Test

```bash
./scripts/restore.sh
./scripts/build.sh
./scripts/test.sh
```

Note: `build.sh` installs and starts PostgreSQL + Redis locally via Homebrew (macOS) or apt-get (Linux) unless `DEV_BOOTSTRAP=0` is set. Set `DEV_BOOTSTRAP=0` if you prefer to manage these services yourself.

## Docker (Local)

```bash
cp .env.example .env
docker compose up --build
```

Services started: API (8080), Worker, PostgreSQL (5432), Redis (6379), Prometheus (9090), Grafana (3000).
Default credentials in `.env.example` are for local development only — never use in production.

## Docker with TradingView Webhooks (ngrok)

```bash
cp .env.demo .env.demo.local
nano .env.demo.local  # Add NGROK_AUTHTOKEN and Kraken demo credentials
docker compose --env-file .env.demo.local --profile ngrok up --build -d
./scripts/get-ngrok-url.sh  # Prints your public webhook URL
```

See [ngrok Quick Start](../integrations/ngrok/ngrok-quickstart.md) for full setup.

## Production Deployment

See [Production Deployment Guide](../operations/deployment/production-deployment-guide.md).

## Environment Switching

```bash
./scripts/switch-env.sh demo     # Demo/sandbox
./scripts/switch-env.sh simulated  # Fixture-based (no live API)
CONFIRM=yes ./scripts/switch-env.sh prod  # Production (requires explicit confirmation)
```

See [Environment Switching Guide](../configuration/environment-switching.md).

## Smoke Test

```bash
# Create local smoke env (gitignored)
cp .env.example .env.smoke
nano .env.smoke  # Add TRADINGVIEW_WEBHOOK_SECRET, Kraken demo keys
# Start services
docker compose --env-file .env.smoke up -d --build api worker
# Run smoke test
./scripts/smoke.sh
```

## Fixture Capture (Kraken history)

```bash
SYMBOL=PF_XBTUSD INTERVAL_MINUTES=15 DURATION_SECONDS=3600 \
  ./scripts/fixtures/capture-futures-history.sh
```

Output: `tests/fixtures/kraken-futures/<symbol>_m<interval>.json`

## Kraken Integration Tests

Disabled by default. To enable against Kraken demo:

```bash
export KRAKEN_FUTURES_INTEGRATION_TESTS=1
export KRAKEN_FUTURES_REST_BASE=https://demo-futures.kraken.com/derivatives/api/v3
export KRAKEN_FUTURES_TEST_SYMBOL=BTCUSD.P
./scripts/test.sh
```
