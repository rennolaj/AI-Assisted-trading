# M6 Overview: Risk Engine, Execution Settings, and Trade Plan Builder

## Purpose
M6 introduces deterministic risk enforcement and plan building before execution.

## New Config Files
- `config/account.json`
  - Static account state for MVP (equity + daily risk used).
- `config/instruments.json`
  - Per-symbol constraints (price tick, qty step, min qty/notional, max leverage).
- `config/execution.json`
  - Execution mode and risk-related settings (slippage cap, heartbeat, retries).

## TradePlan Builder (MVP)
`ITradePlanBuilder.BuildPlan(...)` produces a deterministic `TradePlan` or rejects.

Key rules:
- Only ALLOW decisions produce plans.
- Stop-loss must come from candidate invalidation (anchor: `WAVEINVALIDATION`).
- Entry reference comes from alert close; entry limit applies slippage cap.
- Quantity is derived from risk-per-trade and stop distance, rounded to step.
- Min/max constraints and daily risk budget are enforced (fail closed).
- TradePlan includes 3 partial take-profit targets (1R/2R/3R, 50/30/20 split).
- PlanId is a deterministic hash of core inputs.

## Runtime Wiring
The worker loads:
- `IAccountStateProvider` (account.json)
- `IInstrumentSpecProvider` (instruments.json)
- `IExecutionSettingsProvider` (execution.json)
- `ITradePlanBuilder` (deterministic plan generation)

## Notes / Assumptions
- Risk sizing uses `contractMultiplier` from `config/instruments.json` to determine
  per-point P/L (defaults to 1 when not provided).
- Execution mode supports `SIMULATED` and `KRAKEN_DEMO` (entry + stop + take-profit targets).
- Take-profit targets are submitted as `take_profit` orders and recorded as receipts.
