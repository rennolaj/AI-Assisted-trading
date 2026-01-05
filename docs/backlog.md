# MVP Backlog

## Scope and Guardrails
- Target runtime: C# / .NET 10
- Safety-first: fail closed on uncertainty, schema failure, rate-limit exhaustion, or audit persistence failure
- LLM is advisory and bounded to predefined enums, candidates, and anchors
- Demo trading only until explicit promotion to production

## Epics and Stories

### M0 - Scaffolding and Contracts ✅
**Goal**: establish repo structure, shared contracts, and CI skeleton.
- Story M0.1: Create solution structure (src/tests/docs/schemas)
- Story M0.2: Define shared DTOs and result/error envelopes
- Story M0.3: Create JSON schemas for AlertEvent, SignalSnapshot, ElliottCandidates, LlmDecision, TradePlan
- Story M0.4: Establish baseline CI workflow (build + test)
**Done when**: repo builds in CI, test project exists, schemas are versioned

### M1 - TradingView Ingestion ✅
**Goal**: fast ACK webhook ingress with idempotent enqueueing.
- Story M1.1: Implement webhook endpoint with shared-secret auth
- Story M1.2: Normalize payload to AlertEvent and persist raw payload
- Story M1.3: Enforce 3s ACK with queue enqueue
- Story M1.4: Idempotency by IdempotencyKey
**Done when**: valid alert enqueues within 3s; invalid payloads rejected; duplicates do not re-run

### M2 - Kraken Futures (Read-Only) ✅
**Goal**: market data access with caching and rate-limit budgeting.
- Story M2.1: Implement market data connector (instruments, tickers, candles)
- Story M2.2: Add rate-limit budget tracking and safe backoff
- Story M2.3: Add caching for public endpoints
**Done when**: integration tests pass against demo public endpoints; budget respected

### M3 - Indicator Snapshot ✅
**Goal**: deterministic, multi-timeframe indicator computation.
- Story M3.1: Implement RSI/Stoch RSI/MACD/Volume rules
- Story M3.2: Snapshot serialization and schema validation
- Story M3.3: Determinism tests using fixed fixtures
**Done when**: same inputs yield identical snapshots; fixtures pass

### M4 - Elliott Candidate Generation
**Goal**: generate candidate counts with rule checks.
- Story M4.1: Pivot extraction (ZigZag or equivalent)
- Story M4.2: Candidate generation with rule violations and invalidations
- Story M4.3: Determinism tests
**Done when**: candidates are deterministic and bounded; empty candidate list is explicit

### M5 - MCP Server and LLM Adjudication
**Goal**: MCP resources/tools with strict LLM decision schema.
- Story M5.1: MCP server resources (config and state URIs)
- Story M5.2: MCP tools (adjudicateElliott, explainStopLoss)
- Story M5.3: OpenAI Responses integration with strict schema validation
**Done when**: schema-valid LlmDecision enforced 100 percent; invalid outputs fail closed

### M6 - Risk Engine and Demo Execution
**Goal**: deterministic TradePlan and demo execution with guardrails.
- Story M6.1: Risk policy enforcement and sizing
- Story M6.2: ExecutionService with dead-man's switch heartbeat
- Story M6.3: Persist receipts and reconciliation state
**Done when**: demo E2E run places entry/stop and full audit chain exists

### M7 - Hardening and Observability
**Goal**: resilience, monitoring, and reconciliation.
- Story M7.1: Reconciliation loop for orders/positions
- Story M7.2: Kill switch and fail-closed chaos tests
- Story M7.3: Metrics and tracing (queue lag, errors, execution outcomes)
**Done when**: failures fail closed; dashboards and alerts defined

## Implementation Order (Suggested)
- M0, M1, M2, M3, M4, M5, M6, M7

## Open Decisions
- Exact .NET SDK version to target for CI
- Queue provider selection (Redis Streams, RabbitMQ, SQS)
- Storage choice for audit chain (Postgres preferred in specs)
