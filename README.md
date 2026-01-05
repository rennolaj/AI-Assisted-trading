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

Health checks:
- `http://localhost:8080/health`
- `http://localhost:8080/health/dependencies`

## Runtime configuration
- `TradingView:WebhookSecret`
- `Postgres:ConnectionString`
- `Redis:ConnectionString`
- `Redis:AlertQueueKey` (default: `mvp:alerts`)
 - `Worker:PollIntervalMs`

## Local services
`./scripts/build.sh` will install and start Postgres/Redis and create the `ai-trading-db` database by default. To disable, set `DEV_BOOTSTRAP=0`.
On Linux, `scripts/dev/bootstrap.sh` uses `apt-get` and may require `sudo`. If your Postgres uses a non-default superuser, set `PG_USER`.

## Container notes
When running in containers, set connection strings via environment variables so the API can reach sibling Postgres/Redis containers on the same network, for example:
`Postgres__ConnectionString=Host=postgres;Port=5432;Database=ai-trading-db;Username=postgres;Password=postgres`
`Redis__ConnectionString=redis:6379`
