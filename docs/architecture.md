# Architecture Reference

## System Overview

A .NET 10 / C# 14 event-driven trading backend that ingests TradingView webhook alerts, computes multi-timeframe technical indicators (RSI, MACD, StochRSI, Volume), extracts Elliott Wave candidates via ZigZag pivot analysis, and gates trade entry through a deterministic rule engine (LLM advisory path planned for M16/M17). Approved signals are sized by a risk engine and executed against the Kraken Futures demo sandbox. Every step of the pipeline produces a full audit trail persisted to PostgreSQL. Observability is delivered via OpenTelemetry instrumentation exported to Prometheus and visualised in Grafana.

---

## Component Map

| Component | Project | Responsibility |
|-----------|---------|----------------|
| API | `Mvp.Trading.Api` | ASP.NET Core 10 Minimal API; webhook ingestion, idempotency, payload normalisation, kill-switch endpoint |
| Worker | `Mvp.Trading.Worker` | `AlertWorker` BackgroundService — dequeue, orchestrate pipeline, persist results. Also hosts `ReconciliationWorker` and `TradeMonitorWorker`. |
| Contracts | `Mvp.Trading.Contracts` | 81 sealed records — all shared DTOs, `Result<T>` envelope, error types |
| Indicator Engine | `Mvp.Trading.Indicators` | RSI, MACD, StochRSI, Volume — multi-timeframe snapshots (M5 / M15 / M30 / H1 / H2) |
| Elliott Engine | `Mvp.Trading.Elliott` | ZigZag pivot extraction, candidate generation, rule violation checks, invalidation prices |
| Risk Engine | `Mvp.Trading.Risk` | `TradePlan` builder, position sizing, take-profit targets, `DeterministicElliottAdjudicator` (M16 — in development) |
| Execution | `Mvp.Trading.Execution` | `ExecutionService`, `KillSwitch`, order receipts, reconciliation state |
| Kraken Integration | `Mvp.Trading.Integrations.Kraken` | Kraken Futures HTTP client, rate-limit budget, market data provider, fixture provider for testing |

---

## Alert Processing Pipeline

1. TradingView fires Pine Script alert → `POST /webhooks/tradingview/{secret}`
2. API validates shared secret, checks idempotency key, normalises payload → `AlertEvent`
3. Raw payload persisted; `AlertEvent` enqueued to Redis list
4. `AlertWorker` dequeues `AlertEvent`
5. Indicator Engine computes RSI / MACD / StochRSI / Volume across all configured timeframes → `SignalSnapshot`
6. Elliott Wave Engine fetches OHLC candles from Kraken, extracts ZigZag pivots, generates `ElliottCandidate` records with rule violations and invalidation prices
7. Adjudication gate evaluates candidates: direction alignment, wave label (W3 / W5END), empty rule violations, valid invalidation price → `ALLOW` or `REJECT`
8. On `ALLOW`: Risk Engine builds `TradePlan` (entry, stop, take-profit targets, position size from risk policy)
9. Kill switch checked immediately before execution
10. `ExecutionService` places entry order + stop-loss + take-profit limit orders on Kraken Futures demo
11. Order receipts and full audit chain persisted to PostgreSQL
12. `ReconciliationWorker` (60-second interval) cross-checks internal order state with exchange state and logs any discrepancies

---

## Persistence Model

| Table | Purpose |
|-------|---------|
| `alerts` | Raw webhook payloads and processing status |
| `alert_processing` | Processing state per alert |
| `indicator_snapshots` | Multi-timeframe `SignalSnapshot` per alert |
| `elliott_candidates` | Generated Elliott Wave candidates |
| `llm_adjudications` | Adjudication decision and reasoning (deterministic or LLM) |
| `trade_plans` | Computed `TradePlan` per alert |
| `execution_intents` | Intended order submissions |
| `order_receipts` | Exchange acknowledgements |
| `open_trades` | Currently tracked positions |
| `reconciliation_state` | Last reconciliation run state |
| `reconciliation_discrepancy` | Detected order state mismatches |
| `system_state` | Kill switch state and other global flags |
| `kill_switch_audit` | Audit log for kill switch activations |

---

## Known Design Decisions and Trade-offs

- **Adjudication gate**: Currently uses deterministic rule logic. LLM is used as an advisory layer (M16 / M17). The original LLM-as-gatekeeper pattern is being replaced — tracked in ADR-001.
- **AlertWorker scope**: The worker currently orchestrates the full pipeline (indicators → Elliott → adjudication → risk → execution). Refactoring into focused domain services is tracked as M14.9.1.
- **Redis queue**: Currently a Redis list (at-most-once delivery). Migrating to Redis Streams for at-least-once delivery is tracked in the backlog.
- **Demo mode only**: Execution is wired to Kraken Futures sandbox. Production execution requires additional hardening (M18).
