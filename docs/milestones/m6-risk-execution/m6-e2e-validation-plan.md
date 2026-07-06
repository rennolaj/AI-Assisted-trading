# M6.5 E2E Validation Plan

## Objective
Validate the complete alert-to-execution audit chain without modifying source code.

## What Already Exists

### API Endpoints (Read-Only)
- `POST /webhooks/tradingview/{secret}` - Accepts alerts
- `GET /alerts/status/{idempotencyKey}` - Processing status
- `GET /alerts/{alertId}/indicator-snapshot` - Indicator data
- `GET /alerts/{alertId}/elliott-candidates` - Elliott wave data
- `GET /health` - Health check
- `GET /health/dependencies` - Postgres/Redis check

### Database Tables (Full Audit Chain)
```sql
alerts                    -- Raw alert data
alert_processing          -- Processing status (processing, executed, rejected, etc.)
indicator_snapshots       -- Indicator computations
elliott_candidates        -- Elliott wave analysis
trade_plan               -- Risk-sized trade plans (plan_json: TradePlan)
execution_intent         -- Execution attempts (mode, status)
order_receipt            -- All orders (ENTRY, STOP, TAKE_PROFIT_1/2/3)
execution_heartbeat      -- Dead-man's switch heartbeat
```

### Smoke Test (Current Coverage)
- ✅ Webhook accepts alert (202)
- ✅ Processing status transitions
- ✅ Indicator snapshot exists (200)
- ❌ Trade plan verification
- ❌ Execution intent verification
- ❌ Order receipt verification (5 expected: entry + stop + 3 take-profits)
- ❌ Heartbeat verification

### Execution Modes
- `SIMULATED` - All orders simulated (no real API calls)
- `KRAKEN_DEMO` - Real orders to Kraken demo environment

## What Needs to be Tested (M6.5 Acceptance Criteria)

### 1. Trade Plan Generation
- [ ] Trade plan record exists in `trade_plan` table
- [ ] `plan_json` contains valid TradePlan JSON
- [ ] Plan has deterministic `plan_id` (UUID)
- [ ] Plan includes `symbol`, `side`, `quantity`, `entryLimitPrice`, `stopLossPrice`
- [ ] Plan has 3 `takeProfitTargets` with prices and quantities

### 2. Execution Intent
- [ ] Execution intent record exists in `execution_intent` table
- [ ] References correct `plan_id`
- [ ] Has correct `mode` (SIMULATED or KRAKEN_DEMO)
- [ ] Has terminal `status` (SIMULATED_FILLED, ORDERS_PLACED, etc.)

### 3. Order Receipts (Critical)
- [ ] Exactly 5 order receipts exist for execution_id
- [ ] 1 ENTRY order receipt
- [ ] 1 STOP order receipt
- [ ] 3 TAKE_PROFIT orders (TAKE_PROFIT_1, TAKE_PROFIT_2, TAKE_PROFIT_3)
- [ ] All receipts have `client_order_id` populated
- [ ] SIMULATED mode: `exchange_order_id` is null, status is "SIMULATED"
- [ ] KRAKEN_DEMO mode: `exchange_order_id` is populated, status is from API

### 4. Heartbeat Verification
- [ ] Heartbeat record exists in `execution_heartbeat` for "execution-service"
- [ ] `last_beat_utc` is recent (within execution timeframe)
- [ ] `stale_threshold_seconds` matches config

### 5. Audit Chain Linkage
- [ ] alert_id → trade_plan.alert_id
- [ ] trade_plan.plan_id → execution_intent.plan_id
- [ ] execution_intent.execution_id → order_receipt.execution_id
- [ ] All timestamps are sequential and reasonable

## Testing Strategy (No Source Code Changes)

### Option 1: Database Query Script
Create `scripts/validate-audit-chain.sh` that:
1. Takes `idempotency_key` or `alert_id` as input
2. Queries Postgres directly for all audit chain records
3. Validates record counts and relationships
4. Reports pass/fail with details

**Pros**: Simple, direct, no code changes
**Cons**: Requires psql access, manual SQL queries

### Option 2: Extended Smoke Test
Modify `scripts/smoke.sh` to:
1. Run existing smoke test
2. After terminal status, query database via psql
3. Validate audit chain
4. Report comprehensive results

**Pros**: Single script execution
**Cons**: Mixes HTTP and DB validation

### Option 3: C# Integration Test
Create `tests/Mvp.Trading.E2E.Tests/AuditChainValidationTests.cs`:
1. Uses existing test infrastructure
2. Queries database via NpgsqlDataSource
3. Validates complete audit chain
4. Uses standard test assertions

**Pros**: Type-safe, uses existing patterns, can run in CI
**Cons**: Requires new test project (but no source changes)

## Recommended Approach: Option 1 + Option 3

### Phase 1: Quick Validation Script
- Create `scripts/validate-audit-chain.sh`
- Postgres queries for immediate validation
- Can be run ad-hoc during smoke tests

### Phase 2: Proper E2E Test
- Create `tests/Mvp.Trading.E2E.Tests` project
- Single test: `AuditChainValidationTests.ValidateCompleteAuditChain()`
- Reusable for future E2E scenarios

## Sample SQL Queries Needed

```sql
-- Get alert by idempotency key
SELECT alert_id, status FROM alert_processing WHERE idempotency_key = $1;

-- Get trade plan
SELECT plan_id, plan_json FROM trade_plan WHERE alert_id = $1;

-- Get execution intent
SELECT execution_id, mode, status FROM execution_intent WHERE plan_id = $1;

-- Get all order receipts
SELECT order_kind, client_order_id, exchange_order_id, status 
FROM order_receipt 
WHERE execution_id = $1
ORDER BY created_at_utc;

-- Count receipts by kind
SELECT order_kind, COUNT(*) 
FROM order_receipt 
WHERE execution_id = $1
GROUP BY order_kind;

-- Get heartbeat
SELECT last_beat_utc, stale_threshold_seconds 
FROM execution_heartbeat 
WHERE service_name = 'execution-service';
```

## Configuration Requirements

### Execution Mode Selection
Via `config/execution.json`:
```json
{
  "mode": "SIMULATED",  // or "KRAKEN_DEMO"
  "slippageCapBps": 10,
  "maxOrderRetries": 3,
  "heartbeatIntervalSeconds": 60,
  "staleThresholdSeconds": 120
}
```

### Test Symbol Setup
Via `config/instruments.json` - ensure test symbol (BTCUSD.P) has complete specs.

## Success Criteria for M6.5

✅ **SIMULATED Mode E2E Pass**:
- Alert processes to "executed" status
- Trade plan persisted with 3 take-profit targets
- Execution intent status = "SIMULATED_FILLED"
- 5 order receipts (all with status "SIMULATED")
- Heartbeat updated
- Audit chain complete

✅ **KRAKEN_DEMO Mode E2E Pass** (Optional/Future):
- Same as SIMULATED but with real Kraken demo API calls
- Exchange order IDs populated
- Status = "ORDERS_PLACED" or "ORDERS_PLACED_TP_PARTIAL"

## Next Steps

1. Create `scripts/validate-audit-chain.sh` for immediate validation
2. Run smoke test in SIMULATED mode
3. Execute validation script
4. Document any gaps
5. (Optional) Create proper E2E test project for CI integration
