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
Set `OPENAI_API_KEY` in `.env` for MCP adjudication (keep it local, never commit secrets).
Set `MCP_PROVIDER=openai|local|auto` to choose OpenAI, a local LLM, or OpenAI with local fallback on 429.
When using a local LLM, configure `LOCAL_LLM_BASE_URL` and optionally `LOCAL_LLM_MODEL_OVERRIDE`.
For LM Studio, use `LOCAL_LLM_BASE_URL=http://localhost:1234/v1/` and `LOCAL_LLM_MODE=chat`.
See `docs/local-llm-options.md` for local runtime/model notes.

### Environment Switching for Docker
For safe environment management (simulated/demo/prod), use the environment switcher:

```bash
# Switch to simulated environment
./scripts/switch-env.sh simulated

# Update .env to match (docker-compose environment variable)
# For simulated, set: KRAKEN_FUTURES_ENV=demo (uses simulated mode internally)
# Then rebuild containers
docker compose up --build
```

```bash
# Switch to demo environment
./scripts/switch-env.sh demo

# Update .env to match
# Set: KRAKEN_FUTURES_ENV=demo
docker compose up --build
```

```bash
# Switch to production (requires CONFIRM=yes)
CONFIRM=yes ./scripts/switch-env.sh prod

# Update .env to match
# Set: KRAKEN_FUTURES_ENV=prod
docker compose up --build
```

⚠️ **Important**: The switch-env.sh script updates `config/execution.json` which is copied into the Docker image at build time. Always rebuild containers after switching environments. See `docs/environment-switching.md` for detailed guidance and safety checklists.

## Smoke test (ngrok)
This uses `scripts/smoke.sh` plus a local `.env.smoke` file (not committed).

1) Create `.env.smoke` with your TradingView secret and smoke inputs:
```bash
TRADINGVIEW_WEBHOOK_SECRET=your-secret-here
SYMBOL_HINT=BTCUSD.P
TICKER=BTCUSD.P
EXCHANGE=krakenfutures
INTERVAL=1
KRAKEN_FUTURES_ENV=demo
KRAKEN_FUTURES_DEMO_API_KEY=your-demo-key
KRAKEN_FUTURES_DEMO_API_SECRET=your-demo-secret
SLEEP_SECONDS=3
SMOKE_TIMEOUT_SECONDS=300
NGROK_AUTOSTART=1
```

2) Start API + worker using the smoke env file (does not touch dev config):
```bash
docker compose --env-file .env.smoke up -d --build api worker
```

3) The script will start ngrok automatically (port 8080) and stop it after the timeout.

4) In TradingView, load `tests/pineScript/mvp-smoke-test-alerts.pine` and create an alert:
- Webhook URL: `https://your-ngrok-host.ngrok-free.dev/webhooks/tradingview/your-secret-here`
- Message: `{{alert_message}}`
- Condition: "Any alert() function call"

5) Run the smoke test:
```bash
./scripts/smoke.sh
```

Health checks:
- `http://localhost:8080/health`
- `http://localhost:8080/health/dependencies`

Trade monitoring seed:
- `POST http://localhost:8080/trades/open`
Example:
```bash
curl -X POST http://localhost:8080/trades/open \
  -H "Content-Type: application/json" \
  -d '{"exchangeId":"kraken-futures","symbol":"BTCUSD.P","side":"LONG","entryPrice":70000,"invalidationPrice":68000}'
```

## Reconciliation System
The system continuously monitors order state consistency between internal tracking and exchange state:

**Automatic Detection:**
- Runs every 60 seconds via background worker (configurable via `Reconciliation:IntervalSeconds`)
- Detects 6 types of discrepancies:
  - `MISSING_ON_EXCHANGE`: Order placed internally but not found on exchange
  - `ORPHANED_ON_EXCHANGE`: Order exists on exchange but not tracked internally
  - `STATUS_MISMATCH`: Order status differs between internal and exchange state
  - `FILL_MISMATCH`: Fill quantity/price discrepancies
  - `INVALIDATION_TRIGGERED`: Stop-loss/take-profit triggered but not reflected in status
  - `ERROR`: Reconciliation process errors

**Manual Resolution:**
When discrepancies are detected, review them in the database:
```sql
-- View recent discrepancies
SELECT * FROM reconciliation_discrepancy 
WHERE detected_at > NOW() - INTERVAL '1 hour'
ORDER BY detected_at DESC;

-- Check reconciliation state
SELECT * FROM reconciliation_state 
ORDER BY checked_at DESC LIMIT 10;
```

**Resolution Steps:**
1. Investigate the discrepancy details (stored as JSON in `expected_state` and `actual_state` columns)
2. Verify the actual exchange state using exchange UI or API
3. Take corrective action:
   - Cancel orphaned orders on exchange
   - Update internal state to match exchange reality
   - Re-submit missing orders if appropriate
4. Monitor subsequent reconciliation runs to ensure resolution

See `docs/m7-low-level-requirements.md` for detailed technical specifications.

## Kraken integration tests
These are disabled by default. To run them against demo endpoints:
```bash
export KRAKEN_FUTURES_INTEGRATION_TESTS=1
export KRAKEN_FUTURES_REST_BASE=https://demo-futures.kraken.com/derivatives/api/v3
export KRAKEN_FUTURES_TEST_SYMBOL=BTCUSD.P
./scripts/test.sh
```

## Fixture capture (Kraken Futures history)
Capture real trade history and aggregate into candles for tests:
```bash
SYMBOL=PF_ETHUSD INTERVAL_MINUTES=1 DURATION_SECONDS=600 \
  ./scripts/fixtures/capture-futures-history.sh
```
The output defaults to `tests/fixtures/kraken-futures/<symbol>_m<interval>.json`.

## Runtime configuration
- `TradingView:WebhookSecret`
- `Postgres:ConnectionString`
- `Redis:ConnectionString`
- `Redis:AlertQueueKey` (default: `mvp:alerts`)
- `OpenAI:ApiKey`
- `OpenAI:BaseUrl`
- `OpenAI:Organization`
- `OpenAI:Project`
- `McpProvider:Provider`
- `McpProvider:FallbackOnOpenAi429`
- `LocalLlm:BaseUrl`
- `LocalLlm:ApiKey`
- `LocalLlm:ResponsesPath`
- `LocalLlm:ChatCompletionsPath`
- `LocalLlm:Mode`
- `LocalLlm:UseResponseFormat`
- `LocalLlm:ModelOverride`
- `Worker:PollIntervalMs`
- `Reconciliation:IntervalSeconds` (default: 60)
- `KrakenFutures:Environment`
- `KrakenFutures:BaseUrl`
- `KrakenFutures:AuthBaseUrl`
- `KrakenFutures:WebSocketUrl`
- `KrakenFutures:TestSymbol`
- `KrakenFutures:ApiKey`
- `KrakenFutures:ApiSecret`
- `KrakenFutures:DemoApiKey`
- `KrakenFutures:DemoApiSecret`
- `KrakenFutures:ProdApiKey`
- `KrakenFutures:ProdApiSecret`
- `KrakenFutures:TimeoutSeconds`
- `KrakenFutures:Cache:InstrumentsTtlSeconds`
- `KrakenFutures:Cache:TickersTtlSeconds`
- `KrakenFutures:Cache:CandlesTtlSeconds`
- `KrakenFutures:RateLimit:MaxCostPerWindow`
- `KrakenFutures:RateLimit:WindowSeconds`
- `KrakenFutures:RateLimit:InstrumentsCost`
- `KrakenFutures:RateLimit:TickersCost`
- `KrakenFutures:RateLimit:CandlesCost`
- `Elliott:BaseTimeframe`
- `Elliott:Parameters:PivotMethod`
- `Elliott:Parameters:Depth`
- `Elliott:Parameters:DeviationPct`
- `Elliott:Parameters:MaxCandidates`
- `Elliott:TickSizeFallback`
- `Elliott:TickSizeOverrides` (per-symbol overrides, e.g. `Elliott:TickSizeOverrides:BTCUSD.P=0.5`)

## Local services
`./scripts/build.sh` will install and start Postgres/Redis and create the `ai-trading-db` database by default. To disable, set `DEV_BOOTSTRAP=0`.
On Linux, `scripts/dev/bootstrap.sh` uses `apt-get` and may require `sudo`. If your Postgres uses a non-default superuser, set `PG_USER`.

## Container notes
When running in containers, set connection strings via environment variables so the API can reach sibling Postgres/Redis containers on the same network, for example:
`Postgres__ConnectionString=Host=postgres;Port=5432;Database=ai-trading-db;Username=postgres;Password=postgres`
`Redis__ConnectionString=redis:6379`
