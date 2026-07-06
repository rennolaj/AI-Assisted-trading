# M6 Status Snapshot

## Scope (M6)
- Deterministic TradePlan builder with risk sizing + partial targets
- Execution settings, heartbeat guard, and demo execution flow
- Persistence for trade plans, intents, and receipts
- Demo E2E validation run + audit chain verification

## Current Implementation
- Config files:
  - `config/account.json`
  - `config/instruments.json`
  - `config/execution.json`
- Trade plan + risk sizing:
  - `schemas/TradePlan.schema.json`
  - `src/Mvp.Trading.Risk/TradePlanBuilder.cs`
  - `src/Mvp.Trading.Risk/ITradePlanBuilder.cs`
  - `docs/milestones/m6-risk-execution/m6-partial-targets.md`
- Execution service + stores:
  - `src/Mvp.Trading.Execution/ExecutionService.cs`
  - `src/Mvp.Trading.Execution/PostgresTradePlanStore.cs`
  - `src/Mvp.Trading.Execution/PostgresExecutionIntentStore.cs`
  - `src/Mvp.Trading.Execution/PostgresOrderReceiptStore.cs`
  - `src/Mvp.Trading.Execution/PostgresExecutionHeartbeatStore.cs`
- Database schema:
  - `scripts/db/init.sql` (trade_plan, execution_intent, order_receipt, fill_receipt, execution_heartbeat)
- Worker wiring:
  - `src/Mvp.Trading.Worker/Program.cs`
  - `src/Mvp.Trading.Worker/AlertWorker.cs`

## Completed Features / Validation ✅
- M6.2: Heartbeat fail-closed path implemented with dead-man's switch.
- M6.3: Receipt persistence complete with order_receipt and fill_receipt tables.
- M6.4: Take-profit targets (3x) emitted in plans and routed to execution orders.
- M6.5: Demo E2E validation infrastructure complete:
  - `scripts/validate-audit-chain-docker.sh` validates complete audit chain
  - `scripts/smoke.sh` runs end-to-end alert processing
  - ForceAllow feature tested and validated (requires valid Elliott candidates)
  - Rejection path fully validated
  - See `docs/milestones/m6-risk-execution/m6-e2e-test-results.md` for detailed test results

## Notes / Risks
- Demo execution requires complete instrument specs in `config/instruments.json` for active symbols.
- No automated E2E test currently asserts the full plan -> execution -> receipt chain.

## Optional / Future Enhancements
- Make take-profit target multiples + allocation splits config-driven.
- Allow strategies to override the default 3-target requirement (if approved).
