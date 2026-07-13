# Fixture Mode vs Production Mode Switching Guide

## Overview

The system can run in two modes:
1. **Production Mode**: Connects to Kraken Futures API for real market data
2. **Fixture Mode**: Uses historical JSON fixtures for backtesting and LLM test generation

## Active Configuration File

Docker Compose uses `.env` file (specified in `docker-compose.yml`):
```yaml
env_file:
  - .env
```

**`.env.prod.local` is NOT used by Docker unless explicitly specified.**

## Quick Switch Commands

### Switch to Fixture Mode (for M9 testing)

```bash
# 1. Backup current .env
cp .env .env.production.backup

# 2. Update key settings
cat >> .env << 'EOF'
# Fixture Mode for M9 Testing
MARKETDATA_MODE=fixtures
MARKETDATA_FIXTURE_PATH=tests/fixtures/kraken-futures
MARKETDATA_EXTEND_FIXTURES=true
ELLIOTT_LOOKBACK_DAYS=5
INDICATOR_LOOKBACK_DAYS_M5=5
LOCAL_LLM_BASE_URL=http://192.168.1.15:1234/v1/
EOF

# 3. Restart services
docker compose down && docker compose up -d

# 4. Verify fixture mode is active
docker exec ai-assisted-worker-1 printenv | grep MARKETDATA_MODE
# Should show: MARKETDATA_MODE=fixtures

# 5. Test fixture loading
docker exec ai-assisted-worker-1 ls /app/tests/fixtures/kraken-futures/
# Should list: btcusd_p_m1_varied.json, etc.
```

> **Path resolution caveat**: a relative `MARKETDATA_FIXTURE_PATH` is resolved
> against the application's build output directory (`AppContext.BaseDirectory`),
> NOT the repo root. It works in Docker because compose mounts the fixtures under
> the container workdir — when running the Worker as a host process
> (`dotnet run`), use an **absolute path** or the catalog silently loads nothing
> ("No fixtures loaded" warning, then `FIXTURE_NOT_FOUND` on every alert).
>
> Fixture symbols must match the alert ticker: bundled series cover `BTCUSD.P`
> and `PF_ETHUSD`. M1 series are automatically aggregated up to any higher
> timeframe (M5/M15/M30/H1/H2), so an M1 fixture is sufficient per symbol.

### Switch to Production Mode (for real trading)

```bash
# 1. Restore production .env (or manually update)
cp .env.production.backup .env

# OR manually update these keys:
MARKETDATA_MODE=kraken
ELLIOTT_LOOKBACK_DAYS=1
INDICATOR_LOOKBACK_DAYS_M5=1
LOCAL_LLM_BASE_URL=http://10.10.50.16:1234/v1/

# 2. Restart services
docker compose down && docker compose up -d

# 3. Verify production mode
docker exec ai-assisted-worker-1 printenv | grep MARKETDATA_MODE
# Should show: MARKETDATA_MODE=kraken
```

## Key Configuration Differences

| Setting | Production Mode | Fixture Mode |
|---------|----------------|--------------|
| `MARKETDATA_MODE` | `kraken` | `fixtures` |
| `MARKETDATA_FIXTURE_PATH` | (not used) | `tests/fixtures/kraken-futures` |
| `MARKETDATA_EXTEND_FIXTURES` | `false` | `true` |
| `ELLIOTT_LOOKBACK_DAYS` | `1` | `5` |
| `INDICATOR_LOOKBACK_DAYS_M5` | `1` | `5` |
| `LOCAL_LLM_BASE_URL` | Prod LM Studio | Test LM Studio |
| `KRAKEN_FUTURES_ENV` | `prod` or `demo` | (doesn't matter) |

## Running Fixture Tests (M9 Workflow)

### Prerequisites
1. Fixture mode enabled in `.env`
2. Fixture files in `tests/fixtures/kraken-futures/`
3. LM Studio running at `192.168.1.15:1234`
4. ngrok tunnel active for webhook delivery

### Test a Single Alert

```bash
# Submit alert at specific candle index
./scripts/fixtures/simulate-alert-at-time.sh \
  -f tests/fixtures/kraken-futures/btcusd_p_m1_varied.json \
  -i 4200 \
  -d LONG \
  -r "Testing wave pattern"

# Wait 30 seconds for processing
sleep 30

# Check result
docker exec ai-assisted-postgres-1 psql -U postgres -d ai-trading-db -c \
  "SELECT status, error_message FROM alert_processing ORDER BY last_updated_utc DESC LIMIT 1;"

# If successful (status=completed or rejected), capture fixture
./scripts/fixtures/capture-llm-decision.sh backtest-<TIMESTAMP>
```

### Generate Multiple Test Fixtures

```bash
# Test different candle indices for variety
for idx in 1000 2000 3000 4000 5000 6000; do
  echo "Testing candle $idx..."
  ./scripts/fixtures/simulate-alert-at-time.sh \
    -f tests/fixtures/kraken-futures/btcusd_p_m1_varied.json \
    -i $idx \
    -d LONG \
    -r "Batch test $idx"
  sleep 30
done

# Capture all successful decisions
ls tests/fixtures/llm-decisions/*/backtest-*.json
```

## Troubleshooting

### Fixture Not Loading
```bash
# Check if fixture path is mounted in container
docker exec ai-assisted-worker-1 ls /app/tests/fixtures/kraken-futures/

# Check environment variables
docker exec ai-assisted-worker-1 printenv | grep MARKETDATA

# Check worker logs
docker logs ai-assisted-worker-1 2>&1 | grep -i "fixture\|marketdata"
```

### Elliott Finding 0 Pivots
- Fixture data may lack volatility
- Try different candle indices
- Check Elliott lookback: `ELLIOTT_LOOKBACK_DAYS=5`
- Consider adjusting ZigZag deviation (requires code change)

### LLM Not Responding
```bash
# Test LM Studio connectivity
curl http://192.168.1.15:1234/v1/models

# Check worker logs for LLM calls
docker logs ai-assisted-worker-1 2>&1 | grep "POST http://192.168"

# Verify LOCAL_LLM_BASE_URL is correct
docker exec ai-assisted-worker-1 printenv | grep LOCAL_LLM_BASE_URL
```

## Important Notes

⚠️ **Always verify which mode you're in before running tests or trading**

⚠️ **Fixture mode will NOT execute real trades** (MarketDataProvider doesn't call Kraken API)

⚠️ **Production mode requires valid Kraken API credentials**

⚠️ **Keep backups**: Always backup `.env` before switching modes

## File Reference

- `.env` - Active configuration (used by Docker)
- `.env.prod.local` - Production settings template (not auto-loaded)
- `.env.fixtures.backup` - Backup of fixture mode settings
- `.env.production.backup` - Backup of production mode settings
- `docker-compose.yml` - Specifies which env file to use
- `tests/fixtures/kraken-futures/` - Fixture data directory (must be volume-mounted)

## Volume Mount Configuration

The `docker-compose.yml` must have this volume mount for fixture mode:

```yaml
worker:
  volumes:
    - ./tests/fixtures:/app/tests/fixtures:ro
```

This is already configured in the current `docker-compose.yml`.
