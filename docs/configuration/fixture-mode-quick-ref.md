# Quick Reference: Fixture Mode Switch

## Current State
- **Active Config**: `.env` (used by docker-compose)
- **Current Mode**: FIXTURE MODE (for M9 testing)
- **Production Config**: `.env.prod.local` (ready for production, not active)

## Switch to Production Mode

```bash
# Option A: Quick switch (if .env.production.backup exists)
cp .env.production.backup .env
docker compose down && docker compose up -d

# Option B: Manual switch
cp .env.prod.local .env
docker compose down && docker compose up -d

# Verify production mode
docker exec ai-assisted-worker-1 printenv | grep MARKETDATA_MODE
# Should show: MARKETDATA_MODE=kraken
```

## Switch Back to Fixture Mode

```bash
# Restore fixture settings
cp .env.fixtures.backup .env
docker compose down && docker compose up -d

# Verify fixture mode
docker exec ai-assisted-worker-1 printenv | grep MARKETDATA_MODE
# Should show: MARKETDATA_MODE=fixtures
```

## Run Fixture Test (Quick)

```bash
# 1. Ensure fixture mode is active
docker exec ai-assisted-worker-1 printenv | grep MARKETDATA_MODE

# 2. Submit test alert
./scripts/fixtures/simulate-alert-at-time.sh \
  -f tests/fixtures/kraken-futures/btcusd_p_m1_varied.json \
  -i 4500 -d LONG -r "Test"

# 3. Wait and check
sleep 30
docker exec ai-assisted-postgres-1 psql -U postgres -d ai-trading-db -c \
  "SELECT status FROM alert_processing ORDER BY last_updated_utc DESC LIMIT 1;"

# 4. Capture if successful
./scripts/fixtures/capture-llm-decision.sh backtest-<TIMESTAMP>
```

## Key Files
- `.env` = Active (currently: fixture mode)
- `.env.prod.local` = Production template (restored, not active)
- `.env.fixtures.backup` = Fixture mode backup
- `.env.production.backup` = Production mode backup
- `docs/configuration/fixture-mode-switching.md` = Full documentation

## ⚠️ Safety Check Before Trading

Always verify mode before starting:
```bash
docker exec ai-assisted-worker-1 printenv | grep -E "MARKETDATA_MODE|KRAKEN_FUTURES_ENV"
```

Should show for production:
- `MARKETDATA_MODE=kraken`
- `KRAKEN_FUTURES_ENV=prod` (or `demo` for testing)
