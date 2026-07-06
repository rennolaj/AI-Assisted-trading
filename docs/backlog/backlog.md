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

### M2 Follow-up - Kraken Futures Candle Gap (Pending)
**Goal**: ensure indicator snapshots have enough OHLC data per timeframe.
- Story M2.F1: Document API findings (history returns trades only, ~100 max) ✅
- Story M2.F2: Integrate Charts API candles (`/api/charts/v1/{tick_type}/{symbol}/{resolution}`) ✅
- Story M2.F3: Add charts base URL config + symbol discovery cache ✅
- Story M2.F4: Add exchange-level max bars config + explicit fallback behavior ✅
**Done when**: indicator pipeline can reliably meet minimum bars or uses an agreed low-data mode.

### M3 - Indicator Snapshot ✅
**Goal**: deterministic, multi-timeframe indicator computation.
- Story M3.1: Implement RSI/Stoch RSI/MACD/Volume rules
- Story M3.2: Snapshot serialization and schema validation
- Story M3.3: Determinism tests using fixed fixtures
**Done when**: same inputs yield identical snapshots; fixtures pass

### M4 - Elliott Candidate Generation ✅
**Goal**: generate candidate counts with rule checks.
- Story M4.1: Pivot extraction (ZigZag or equivalent)
- Story M4.2: Candidate generation with rule violations and invalidations
- Story M4.3: Determinism tests
**Done when**: candidates are deterministic and bounded; empty candidate list is explicit
**Coverage**: unit + real-world fixtures (spot/futures), pipeline integration test

### M4 Extra - Gap-Resolved LLD ✅
**Goal**: implement the addendum decisions for deterministic scoring and invalidation.
- Story M4.X.1: Enforce timeframe restrictions and EW_TIMEFRAME_UNSUPPORTED behavior
- Story M4.X.2: Implement lookback sizing and EW_PIVOTS_INSUFFICIENT handling
- Story M4.X.3: Add scoring/confidence formula with penalties and rounding rules
- Story M4.X.4: Implement invalidation buffer and tick rounding
- Story M4.X.5: Align RuleViolation.details to string with stable JSON encoding
- Story M4.X.6: Add Elliott profile selection (conservative/aggressive) with fallback on pivots insufficient
- Story M4.X.7: Allow config-driven profile mapping (base + fallback) tied to indicator risk category

### M5 - MCP Server and LLM Adjudication ✅
**Goal**: MCP resources/tools with strict LLM decision schema.
- Story M5.1: Embedded MCP host (in-process) with clean gateway boundary
- Story M5.2: MCP tools (adjudicateElliott, explainStopLoss)
- Story M5.3: OpenAI Responses integration with strict schema validation
- Story M5.4: File-based policy/config loader (risk-policy.json, prompts/*.md)
- Story M5.5: Orchestration tests + fail-closed matrix
**Done when**: schema-valid LlmDecision enforced 100 percent; invalid outputs fail closed

### M6 - Risk Engine and Demo Execution ✅
**Goal**: deterministic TradePlan and demo execution with guardrails.
- Story M6.X: TradePlan partial targets (>=3) added to contract + builder ✅
- Story M6.1: Risk policy enforcement and sizing ✅
- Story M6.2: ExecutionService with dead-man's switch heartbeat ✅
- Story M6.3: Persist receipts and reconciliation state ✅
- Story M6.4: Route take-profit targets into execution orders ✅
- Story M6.5: Demo E2E validation run (Kraken demo) + audit chain verification ✅
- Story M6.O1 (Optional): Make take-profit target multiples + allocation splits config-driven
- Story M6.O2 (Optional): Allow strategy overrides for target requirements
**Done when**: demo E2E run places entry/stop, persists receipts, and audit chain exists
**Coverage**: E2E validation infrastructure complete, rejection path validated, ForceAllow feature tested

### M7 - Hardening and Observability
**Goal**: resilience, monitoring, and reconciliation.
- Story M7.1: Reconciliation loop for orders/positions ✅
- Story M7.2: Kill switch and fail-closed chaos tests ✅
- Story M7.3: Metrics and tracing (queue lag, errors, execution outcomes) ✅
**Done when**: failures fail closed; dashboards and alerts defined
**Status**: Core implementation complete; optional dashboard updates and alerting rules remain

### M8 - AI-Driven Order Management
**Goal**: intelligent order monitoring and management for open positions.
- Story M8.1: Implement AI-driven order monitoring service
  - Monitor open orders and positions in real-time
  - AI decision-making for order modifications (close, partial close, break-even adjustment)
  - Allow percentage-based partial exits while letting remainder run to maximum potential
  - Break-even stop adjustment once profit thresholds reached
  - Integration with risk policy and position sizing rules
- Story M8.2: Define LLM decision schema for order management actions
  - Schema for monitoring decision (HOLD, CLOSE_PARTIAL, CLOSE_ALL, ADJUST_STOP, MOVE_TO_BREAKEVEN)
  - Percentage allocation rules for partial exits
  - Confidence scoring and decision rationale
- Story M8.3: Add MCP tools for order management adjudication
  - monitorPosition tool with market context and position state
  - Integration with existing risk engine and execution service
- Story M8.4: Persistence layer for order management decisions and audit trail
  - Track all AI-driven order modifications
  - Maintain decision history for backtesting and analysis
**Done when**: AI can monitor open orders, suggest and execute intelligent order modifications with full audit trail; partial profit-taking and break-even features validated in demo

### M9 - LLM Test Fixtures and Validation (IN PROGRESS - 50%)
**Goal**: comprehensive test fixtures with known-good LLM responses and end-to-end dataflow validation.
- Story M9.1: Create fixture capture infrastructure ✅
  - Built `capture-llm-decision.sh` to extract full context from database
  - Created directory structure (`tests/fixtures/llm-decisions/{accept,reject}`)
  - Captured initial REJECT fixtures from production
  - Explored ForceAllow approach (not viable for synthetic data)
  - Built multi-scenario testing framework ($100, $1k, $10k, $500k equity levels) ✅
  - Fixed qtyStep proportionality bug (maintain consistent risk profile) ✅
- Story M9.2: Build LLM response fixture library (IN PROGRESS)
  - Captured 5 REJECT cases with full context (webhook, indicators, Elliott, LLM decision)
  - Need to capture ALLOW cases from real TradingView alerts with valid Elliott patterns
  - Requires monitoring production for 2-3 days to collect diverse scenarios
  - ForceAllow cannot generate synthetic fixtures (needs 5+ pivots, real market data)
- Story M9.3: Implement fixture-based integration tests (BLOCKED by M9.2)
  - Need fixture library completed first
  - Will replay scenarios and validate system behavior
  - Test both ALLOW and REJECT paths with known outcomes
- Story M9.4: Add fixture capture tooling ✅
  - `capture-llm-decision.sh` script complete (239 lines)
  - `generate-positive-fixture.sh` created but requires real data
  - Metadata includes: LLM model, prompt version, decision, status, timestamp
  - JSON validation and auto-categorization working
- Story M9.5: Re-evaluate indicator + Elliott pipeline integration (NEW - PRIORITY)
  - Analyze production Pine Script alert combinations (LONG/SHORT signals)
  - Cross-reference with Elliott engine triggers and candidate generation
  - Validate that indicator conditions properly filter Elliott analysis
  - Document all possible alert scenarios (high/medium/low risk × LONG/SHORT)
  - Prepare comprehensive test matrix covering all alert types
  - Create fixtures for each scenario: ACCEPT (valid Elliott alignment) and REJECT (misalignment/no candidates)
  - Validate that dataflow from Pine → Indicators → Elliott → LLM is logically consistent
- Story M9.6: Full dataflow analysis and validation plan (NEW - PRIORITY)
  - Document complete end-to-end dataflow from TradingView alert to trade execution
  - Map data transformations at each stage (webhook → indicator snapshot → Elliott candidates → LLM decision → trade plan)
  - Identify validation checkpoints and cross-reference points
  - Create validation plan to verify logical consistency across all pipeline stages
  - Document expected behavior for each alert type through entire pipeline
  - Define acceptance criteria for "correct" vs "incorrect" pipeline behavior
  - See: `docs/architecture/alert-dataflow-overview.md` for existing partial documentation
- Story M9.7: LLM adjudication persistence and observability (NEW - CRITICAL)
  - Add `llm_adjudications` table to database schema to persist full LLM interactions
  - Store: prompt text, raw response, parsed decision, reasoning, token counts, response time
  - Track: LLM provider, model, parse errors, validation errors for debugging
  - Update `AlertWorker` to persist LLM adjudication results after each call
  - Update `McpGatewayRouter` to return full context (prompt sent, raw response, timing, tokens)
  - Update `capture-llm-decision.sh` to include LLM adjudication data in fixtures
  - Add fixture JSON schema section for `llmAdjudication` with full prompt/response/reasoning
  - Enable debugging of LLM rejections by seeing exact prompt and response
  - See: `docs/milestones/m9-backtesting-fixtures/m9-llm-persistence-design.md` for complete design
**Done when**: test suite includes 10+ positive LLM acceptance cases; fixtures serve as regression suite; tooling exists to capture and review new fixtures; complete alert scenario matrix documented; full dataflow validated for logical consistency; LLM interactions fully observable in database
**Status**: Infrastructure complete, scenario framework built, dataflow analysis complete (M9.6 ✅), need LLM persistence (M9.7) and comprehensive fixture matrix

### M10 - Additional Timeframe Support (NEW)
**Goal**: Add 4H timeframe support for Elliott Wave analysis and indicator validation
- Story M10.1: Add 4H timeframe to ElliottEngine validation configuration
  - Update timeframe enum to include H4
  - Add 4H lookback configuration (suggested: 5-7 days)
  - Update minimum bar requirements for 4H analysis
- Story M10.2: Update IndicatorEngine for 4H support
  - Add 4H to INDICATOR_LOOKBACK configuration
  - Configure appropriate lookback period for 4H indicators
  - Update multi-timeframe analysis to include 4H
- Story M10.3: Update environment configuration files
  - Add INDICATOR_LOOKBACK_H4 to all .env templates
  - Add ELLIOTT_LOOKBACK_H4 configuration
  - Update documentation for 4H timeframe settings
- Story M10.4: Update fixture generation and test infrastructure
  - Ensure fetch-historical-candles.sh supports 4h resolution
  - Update test matrix to include 4H test cases
  - Add 4H fixtures to test suite
- Story M10.5: Validate 4H Elliott pivot extraction
  - Test minimum bar requirements (~200 bars for 4H)
  - Validate pivot extraction quality on 4H timeframe
  - Document 4H-specific Elliott Wave patterns
**Done when**: System can analyze 4H timeframe alongside M5/M15/M30/H1; configuration files updated; tests pass with 4H data
**Status**: Backlog - triggered by M9.2 test plan execution discovering 4H availability

### M11 - Scale-In Entry Strategy (NEW)
**Goal**: Add configurable multiple entry points (dollar-cost averaging) to improve average entry price
- Story M11.1: Add scale-in configuration to environment files
  - Add RISK_SCALE_IN_ENABLED (true/false) - enable/disable feature
  - Add RISK_SCALE_IN_COUNT (default: 3) - number of entry orders (including initial entry)
  - Add RISK_SCALE_IN_SPACING_PCT (default: 0.5) - percentage spacing between entries
  - Add RISK_SCALE_IN_SIZE_DISTRIBUTION (default: "EQUAL") - options: EQUAL, PYRAMID, REVERSE_PYRAMID
  - All entries share the same stop-loss price (from initial Elliott invalidation)
- Story M11.2: Update TradePlanBuilder to support multiple entries
  - Generate entry orders array with spaced prices (initial, -0.5%, -1.0%)
  - Split position size across entries based on distribution strategy
  - EQUAL: 33%/33%/33%, PYRAMID: 25%/35%/40%, REVERSE_PYRAMID: 40%/35%/25%
  - Maintain same stop-loss for all entries (no moving the stop)
  - Adjust take-profit targets based on average entry (not initial entry)
- Story M11.3: Update ExecutionService for staged order placement
  - Place initial entry immediately (market or limit)
  - Place additional entries as limit orders at calculated levels
  - Track partial fills and average entry price
  - Update take-profit calculations dynamically as entries fill
  - Cancel unfilled scale-in orders if stop-loss is hit
- Story M11.4: Add scale-in audit trail and metrics
  - Track which entry orders filled and at what prices
  - Calculate realized average entry vs planned entry
  - Metrics: fill rate per entry level, average slippage per level
  - Include scale-in details in execution receipts
- Story M11.5: Add scale-in validation and testing
  - Validate that stop-loss is never moved (risk stays constant)
  - Test with historical fixtures to validate averaging improves outcomes
  - Document scenarios where scale-in helps vs hurts
  - Add configuration validation (count >= 1, spacing > 0, etc.)
**Done when**: Trade plans can include 1-5 entry orders with configurable spacing; all entries share same stop-loss; execution tracks partial fills and average entry; full audit trail; feature can be disabled via config
**Status**: Backlog - user request for dollar-cost averaging / scale-in feature
**Rationale**: Improves average entry price if initial entry is premature; reduces impact of timing risk; common in professional trading to scale into positions

### M12 - Agent Orchestration Alignment (NEW - CRITICAL)
**Goal**: align orchestration behavior with deterministic multi-agent best practices and reduce token waste.
- Story M12.1 (P0): Enforce `agent-stuck.threshold` in lifecycle transitions
  - Use configured threshold to transition sessions to `stuck`
  - Trigger configured reaction once threshold is exceeded
  - Add tests covering threshold-based transition behavior
- Story M12.2 (P0): Add LLM budget controls (`max_tokens`, `max_steps`, `max_retries`)
  - Extend config schema with per-agent and per-session budget settings
  - Enforce budget limits during execution
  - Define fallback behavior (`needs_input`, stop, or configurable action)
- Story M12.3 (P0): Add structured outbox contract for role handoffs
  - Define JSON schema for role outputs
  - Validate outputs before routing to next stage
  - Fail fast with actionable error messages on schema mismatch
- Story M12.4 (P0): Add deterministic staged pipeline mode
  - Support Planner -> Implementer -> Reviewer -> Tester stage order
  - Add stage gating, retries, and fail-fast/continue policy
  - Expose as `ao run-pipeline` (or equivalent)
- Story M12.5 (P1): Add shared task-state model for multi-stage runs
  - Persist `goal`, `plan`, `current_step`, `decisions`, `constraints`, `modified_files`
  - Provide filtered state views per stage/role
- Story M12.6 (P1): Add progressive context expansion
  - Start each stage with minimal context bundle
  - Allow explicit context expansion requests and track decisions
  - Prefer snippets/diffs/artifacts over full-file context
**Done when**: deterministic staged runs are supported, budget limits are enforceable, handoffs are schema-validated, stuck threshold is truly enforced, and context expansion is explicit and minimal.
**Status**: Backlog

## Implementation Order (Suggested)
- M0, M1, M2, M3, M4, M5, M6, M7, M8, M9

## Open Decisions
- Exact .NET SDK version to target for CI
- Queue provider selection (Redis Streams, RabbitMQ, SQS)
- Storage choice for audit chain (Postgres preferred in specs)
- Future: run MCP as separate container instead of embedded
- Future: additional LLM providers beyond OpenAI/local (Azure OpenAI, etc.)
- Future: policy/config source (DB or remote store vs file-based)

### Multi-Agent Follow-up Bugs (Auto)
**Goal**: Track blocking findings from reviewer/quality/integrator for the next iteration.
- Story BUG.2026-03-04.reviewer: [PRIORITY: NEXT_ITERATION] m9-7-llm-adjudication-persistence-observability - REVIEWER reported blocking findings (AUTOBUG:m9-7-llm-adjudication-persistence-observability:reviewer)
  - Source: /tmp/multi-agent-sync/m9-7-llm-adjudication-persistence-observability/outbox/reviewer.md
  - Trigger: 1. **HIGH** - Forced adjudication path is never persisted
  - Required action: Fix blocking findings before continuing feature delivery.
- Story BUG.2026-03-04.quality: [PRIORITY: NEXT_ITERATION] m9-7-llm-adjudication-persistence-observability - QUALITY reported blocking findings (AUTOBUG:m9-7-llm-adjudication-persistence-observability:quality)
  - Source: /tmp/multi-agent-sync/m9-7-llm-adjudication-persistence-observability/outbox/quality.md
  - Trigger: QUALITY_STATUS: CHANGES_REQUIRED
  - Required action: Fix blocking findings before continuing feature delivery.

### M13 - AO Integration Readiness: Linear + GitHub PR Flow (NEW - HIGH)
**Goal**: guarantee that AO sessions can fetch Linear issues and reliably open/manage GitHub PRs.

**Missing overview (current gaps to close):**
- Config readiness checks are not enforced from project backlog/workflow:
  - `projects.<id>.repo` must be valid `owner/repo`
  - `projects.<id>.tracker.plugin` must be set correctly (`linear` when using Linear tickets)
  - `projects.<id>.tracker.teamId` required for Linear list/create flows
  - `projects.<id>.scm.plugin` should be explicitly set to `github` for clarity
- Auth readiness is not tracked as a backlog gate:
  - `gh auth status` must be valid for PR operations
  - `LINEAR_API_KEY` (or `COMPOSIO_API_KEY`) must be present when tracker is Linear
- No preflight command/checklist is documented in project-level runbook before `ao spawn`.
- No explicit failure-mode checklist for common breakpoints (invalid repo slug, missing teamId, missing GH auth, missing Linear key).

- Story M13.1: Add AO config contract section to project docs (required fields + valid examples)
  - Include minimal YAML for GitHub+Linear project
  - Include explicit `repo`, `scm`, `tracker`, `defaultBranch`, `sessionPrefix` expectations
- Story M13.2: Add preflight checklist command block to docs
  - `gh auth status`
  - `echo $LINEAR_API_KEY` (or COMPOSIO alternative)
  - `ao status` / `ao spawn <project> <ticket>` smoke test
- Story M13.3: Add "PR flow readiness" acceptance test
  - Spawn from a Linear ticket
  - Verify branch creation, PR detection (`pr_open`), and CI/review polling via SCM plugin
- Story M13.4: Add troubleshooting matrix for auth/config failures
  - Map symptom -> likely root cause -> fix command
- Story M13.5: Add optional strict validation task
  - Fail fast in startup/preflight when tracker=linear and required env/config keys are missing

**Done when**: one documented checklist validates Linear + GitHub PR path end-to-end, and failures are actionable before agents are spawned.
**Status**: Backlog

### M14 - Full Technical Review: C# .NET 10 Standards (NEW - HIGH)
**Goal**: comprehensive technical review of the entire codebase against C# and .NET 10 best practices, identifying gaps, anti-patterns, and improvement opportunities with an actionable remediation plan per story.

---

#### M14.1 — Language and Compiler Modernization
**Research status**: ✅ Complete | **Overall**: EXCELLENT — codebase ~95% modernized
**Global baseline confirmed**: `net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, SDK pinned `10.0.101`

**Already compliant — no action needed:**
- Pattern matching: 105 modern `is null` / `is not null` / switch expressions, zero old-style
- Record DTOs: 81 sealed records — entire Contracts project is records
- File-scoped namespaces: 149/150 files (100% modern)
- LINQ: zero anti-patterns (`Where().Count()` etc.)
- String interpolation: 93 interpolations, zero `string.Format()` calls
- `var` usage: well-balanced throughout
- Global usings: `ImplicitUsings` already covers System.* — no redundant blocks

**Stories:**
- Story M14.1.A: Convert 5+ assignment-only constructors to primary constructor syntax (C# 12)
  - Targets: ExecutionService, TradePlanBuilder, ImpulseCandidateBuilder, InvalidationCalculator, IndicatorEngine
  - Risk: LOW — purely syntactic, no behavior change | Effort: ~2h
- Story M14.1.B: Convert 6 `new[] { }` array initializers to collection expressions `[ ]` (C# 12)
  - Files: Program.cs, IndicatorDefaults.cs, ElliottEngine.cs, TradePlanBuilder.cs
  - Risk: LOW | Effort: ~15min
- Story M14.1.C: Remove 5 redundant null-forgiving operators (`!`)
  - Files: KillSwitchService.cs (~lines 35, 47), IndicatorEngine.cs (~line 60) — redundant after `TryGetValue`/`is not null`
  - Risk: LOW | Effort: ~30min
- Story M14.1.F: Replace `Substring(0,N)` with range syntax `[..N]` in 3 files (optional)
  - Files: LocalLlmMcpGateway.cs, WebhookHeaderSanitizer.cs, KrakenFuturesSymbolFormatter.cs
  - Risk: LOW | Effort: ~15min
- Story M14.1.J: Enable `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in Directory.Build.props
  - Also evaluate removing `<NoWarn>1591</NoWarn>` in Mvp.Trading.Api.csproj
  - Risk: MEDIUM — run full build first; may expose hidden warnings | Effort: ~2-4h

**Done when**: all five stories applied, build passes clean with no warnings, TreatWarningsAsErrors active.
**Status**: Backlog

##### M14.1.X — C# 14 / .NET 10 Feature Adoption (concrete analysis complete)
**Research status**: ✅ Complete — 14 features scanned, 4 applicable, 10 NOT_APPLICABLE (codebase already modern)

**Features scanned and ruled NOT applicable** (no changes needed):
- `field` keyword — no simple backing-field properties found
- `nameof` with unbound generics — no generic type names in string literals
- Null-conditional assignment `??=` — no suitable single-assignment null-guard patterns
- `Task.WhenEach` — existing `Task.WhenAll` in IndicatorEngine collects all results before processing (correct)
- `Base64Url` — only HMAC-SHA512 signing (not URL-safe encoding)
- `SearchValues<T>` — no multi-char containment loops found
- `CountBy`/`AggregateBy` LINQ — no GroupBy-ToDictionary patterns found
- `Index()` LINQ — no `Select((item, i) => ...)` patterns found
- `[OverloadResolutionPriority]` — no dual overload conflicts
- `\e` escape sequence — no ANSI escape sequences in source

**Stories:**
- Story M14.1.X-1: Upgrade `object` lock fields to `System.Threading.Lock` (C# 13)
  - `src/Mvp.Trading.Integrations.Kraken/KrakenFuturesRateLimitBudget.cs:10` — `private readonly object _gate = new();` → `private readonly Lock _gate = new();`
  - `src/Mvp.Trading.Integrations.Kraken/FixtureMarketDataProvider.cs:303` — `private readonly object _sync = new();` → `private readonly Lock _sync = new();`
  - Add `using System.Threading;` to both files. `lock` statement syntax is unchanged.
  - Risk: LOW (1:1 syntactic replacement, no behavioral change) | Effort: ~15min

- Story M14.1.X-2: Replace `Guid.NewGuid()` with `Guid.CreateVersion7()` for DB-persisted entity IDs (.NET 10)
  - `src/Mvp.Trading.Api/Services/PostgresOpenTradeCommand.cs:36` — `tradeId` (persisted to `open_trades.trade_id`)
  - `src/Mvp.Trading.Execution/PostgresOrderReceiptStore.cs:35` — `receipt_id` (persisted to `order_receipt.receipt_id`)
  - `src/Mvp.Trading.Worker/AlertWorker.cs:467` — `AdjudicationId` (persisted to `llm_adjudication`)
  - `src/Mvp.Trading.Api/Program.cs:214` — `AlertId` in AlertEvent (persisted to DB)
  - **Do NOT change**: CorrelationId GUIDs (AlertWorker.cs:149, 215) — non-persisted, leave as `NewGuid()`
  - Risk: MEDIUM — verify no existing queries depend on random GUID ordering; test DB index behaviour | Effort: ~30min + validation

- Story M14.1.X-3: Replace manual list composition with collection expression spread (C# 12)
  - `src/Mvp.Trading.Integrations.Kraken/FixtureMarketDataProvider.cs:232-235`
  - Current: `new List<Candle>(lookbackBars); result.AddRange(prefix); result.AddRange(candles); return result;`
  - Replace with: `return [..prefix, ..candles];`
  - Risk: LOW (collection expression allocates exact size; no capacity hint needed) | Effort: ~5min

- Story M14.1.X-4: Convert `params string[]` to `params ReadOnlySpan<string>` in JSON normalizer helpers (C# 13)
  - `src/Mvp.Trading.Api/TradingViewNormalizer.cs:38` — `GetStringCaseInsensitive` method signature
  - `src/Mvp.Trading.Api/TradingViewNormalizer.cs:62` — `TryGetNumberCaseInsensitive` method signature
  - Both only iterate over params; no storage, mutation, or return of the array.
  - Risk: MEDIUM — verify all callers compile after signature change; run webhook normalisation tests | Effort: ~30min

**Done when**: all 4 stories complete, build passes, unit tests for TradingViewNormalizer green.
**Status**: Backlog

---

#### M14.2 — .NET 10 Runtime and API Alignment
**Research status**: ✅ Complete | **Overall**: GOOD with CRITICAL gaps

**Per-project health:**
| Project | Status | Key Gap | Effort |
|---------|--------|---------|--------|
| Mvp.Trading.Worker | NEEDS ATTENTION | `DateTime.UtcNow` (10+ files), no ShutdownTimeout, JsonOptions not cached | ~32h |
| Mvp.Trading.Integrations.Kraken | NEEDS ATTENTION | No resilience policies, `DateTime.UtcNow` in rate limiter, HttpClient config in ctor | ~16h |
| Mvp.Trading.Api | GOOD | `DateTime.UtcNow` (5 files), no `ValidateOnStart` | ~12h |
| Mvp.Trading.Execution | GOOD | `DateTime.UtcNow` in KillSwitchService | ~8h |
| Mvp.Trading.Risk | GOOD | JsonOptions not cached (3 files) | ~2h |
| Mvp.Trading.Indicators | GOOD | None critical | ~2h |
| Mvp.Trading.Elliott | EXCELLENT | None | 0h |
| Mvp.Trading.Contracts | EXCELLENT | None (pure DTO lib) | 0h |

**Already compliant — no action needed:**
- IHttpClientFactory used correctly — no raw `new HttpClient()` anywhere
- BackgroundService implementations correct (`ExecuteAsync` + `stoppingToken`)
- Structured logging — no string interpolation defeating log properties
- Minimal APIs with Swagger/OpenAPI in place
- `IOptions<T>` pattern used throughout
- No deprecated NuGet packages; OpenTelemetry instrumentation present
- No service locator anti-patterns; no captive dependency lifetime mismatches

**Phase 1 — MUST FIX (~54h total):**
- Story M14.2.1 (CRITICAL): Inject `TimeProvider` abstraction — replace all `DateTimeOffset.UtcNow` / `DateTime.UtcNow`
  - 40+ call sites across 12+ files: AlertWorker, KillSwitchService, KrakenFuturesMarketDataProvider (rate limiter), all Postgres store timestamp captures
  - Register `TimeProvider.System` in DI; inject via constructor; use `timeProvider.GetUtcNow()`
  - Create `FakeTimeProvider` tests using `Microsoft.Extensions.TimeProvider.Testing`
  - Blocker: unlocks all time-dependent unit and integration testing | Risk: MEDIUM | Effort: ~40h
- Story M14.2.2 (CRITICAL): Add `Microsoft.Extensions.Http.Resilience` to Kraken HTTP client
  - Zero retry, circuit-breaker, or timeout policies on any Kraken API call — single transient failure = missed trade
  - Add `AddStandardResilienceHandler()` in `Program.cs` `AddHttpClient<KrakenFuturesTradingProvider>`
  - Configure: 3× retry with exponential backoff, 30s circuit-breaker, 10s per-attempt timeout
  - Risk: LOW | Effort: ~8h
- Story M14.2.3 (HIGH): Add `ValidateDataAnnotations().ValidateOnStart()` to all `IOptions<T>` bindings
  - Missing validation means bad config fails at runtime during trade execution, not at startup
  - Add `[Required]`, `[Range]`, `[Url]` to all `*Options` / `*Settings` classes; update all `services.Configure<T>()` in Program.cs
  - Risk: LOW | Effort: ~6h

**Phase 2 — SHOULD FIX (~18h total):**
- Story M14.2.4 (HIGH): Configure `HostOptions.ShutdownTimeout = 30s` in Program.cs
  - Default 5s is insufficient to drain Redis queue or complete in-flight orders
  - `services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));`
  - Risk: LOW | Effort: ~1h
- Story M14.2.5 (MEDIUM): Cache `JsonSerializerOptions` as `static readonly` fields
  - 11+ files instantiate `new JsonSerializerOptions { ... }` per call — micro-allocation on every alert
  - Extract to `static readonly` singleton per usage context across Worker, Api, Integrations.Kraken, Risk
  - Risk: LOW | Effort: ~8h
- Story M14.2.6 (MEDIUM): Move Kraken `HttpClient` configuration from constructors to DI factory
  - `BaseAddress`, `Timeout`, default headers set in constructor; should live in `AddHttpClient<T>()` in Program.cs
  - Risk: LOW | Effort: ~6h

**Phase 3 — NICE TO HAVE (~26h total):**
- Story M14.2.7: Add `JsonSerializerContext` source generation for `AlertEvent`, `TradePlan`, `LlmDecision`, Kraken API models (~12h)
- Story M14.2.8: Replace `_logger.LogInformation(...)` with `[LoggerMessage]` partial methods in worker hot paths (~6h)
- Story M14.2.9: Introduce `Directory.Packages.props` — Central Package Management for all 8 projects (~4h)
- Story M14.2.10: Convert Minimal API `Results.Ok(...)` → `TypedResults.Ok(...)` for AOT + OpenAPI correctness (~2h)

**Total estimated effort: ~98h**
**Done when**: TimeProvider injected everywhere; Kraken calls have resilience policies; bad config fails at startup; phases 1–3 complete and build + tests green.
**Status**: Backlog

---

#### M14.3 — Dependency Injection and Configuration
**Research status**: ✅ Complete | **Overall**: GOOD with targeted gaps

**Already compliant — no action needed:**
- Service lifetimes: all major singletons are stateless/long-lived — no captive dependency violations
- No service locator anti-patterns (`IServiceProvider.GetService<T>()` at runtime)
- Extension method organization: DI registrations split into logical groups per domain
- `IOptions<T>` variant selection: correct variant used per injection site (no `IOptionsSnapshot<T>` in singletons)

**Must Fix:**
- Story M14.3.1 (HIGH): Add `[Required]` data annotations to all `*Options` / `*Settings` classes
  - `PostgresOptions`, `RedisOptions`, `OpenAiOptions`, `LocalLlmOptions`, `KillSwitchApiOptions` have no validation attributes
  - Prerequisite for `ValidateOnStart` — without annotations validation has nothing to check
  - Risk: LOW | Effort: ~2h
- Story M14.3.2 (HIGH): Add `.ValidateDataAnnotations().ValidateOnStart()` to all `Configure<T>()` calls
  - Api/Program.cs (6 calls), Worker/Program.cs (3 calls) — none have `ValidateOnStart`
  - Currently bad config (e.g. missing Redis connection string) fails on first trade, not at startup
  - Risk: LOW | Effort: ~4h

**Should Fix:**
- Story M14.3.3 (MEDIUM): Extract 15+ config section name magic strings to `ConfigurationKeys` constants class
  - Section names hardcoded across both Program.cs files — typos silently produce empty/null config
  - Risk: LOW | Effort: ~3h
- Story M14.3.4 (MEDIUM): Fix HttpClient + Singleton coupling for `KrakenFuturesMarketDataProvider`
  - Registered as Singleton but uses `AddHttpClient<T>` typed client — defeats connection pooling semantics
  - Fix: make it a properly configured typed client or adjust registration | Effort: ~2h

**Done when**: all options classes annotated; `ValidateOnStart` active on all bindings; build and tests green.
**Status**: Backlog

---

#### M14.4 — Async/Await and CancellationToken Correctness
**Research status**: ✅ Complete | **Overall**: SIGNIFICANT GAPS — 5 must-fix issues on critical paths

**Already correct — no action needed:**
- KillSwitchService: correctly propagates ct through `OpenAsync` and `ExecuteScalarAsync`
- KrakenFuturesTradingProvider: correctly passes ct to `HttpClient.SendAsync` and `ReadAsStringAsync`
- AlertWorker.GetAdjudicationAsync: correctly propagates ct to MCP gateway
- ElliottEngine.GenerateCandidatesAsync: correctly passes ct to market data calls

**Must Fix (blocks production):**
- Story M14.4.1 (CRITICAL): Fix CT not propagated in `ExecutionService.SendWithRetriesAsync`
  - File: Mvp.Trading.Execution/ExecutionService.cs ~line 366
  - `Func<Task<Result<OrderAck>>>` invoked as `await action()` — ct never forwarded
  - Fix: change to `Func<CancellationToken, Task<Result<OrderAck>>>` and call `await action(ct)`
  - Impact: order retry loop cannot be cancelled during shutdown | Risk: LOW | Effort: ~1h
- Story M14.4.2 (HIGH): Fix CT ignored in `RedisAlertQueue.EnqueueAsync`
  - File: Mvp.Trading.Api/Services/RedisAlertQueue.cs ~line 31
  - `ListRightPushAsync` called without ct — Redis enqueue blocks graceful shutdown
  - Risk: LOW | Effort: ~30min
- Story M14.4.3 (HIGH): Fix CT ignored in `AlertWorker.DequeueAsync`
  - File: Mvp.Trading.Worker/AlertWorker.cs ~lines 331-339
  - Both `ListLengthAsync` and `ListLeftPopAsync` called without ct — queue reads block shutdown
  - Risk: LOW | Effort: ~30min
- Story M14.4.4 (HIGH): Standardize 14+ Postgres files from `CreateCommand` to `OpenConnectionAsync(ct)` pattern
  - `NpgsqlDataSource.CreateCommand()` has no CT support — command creation is uncancellable
  - Correct pattern already in PostgresReconciliationStore.SaveReconciliationAsync — standardize everywhere
  - Affected: PostgresOpenTradeRepository, PostgresAlertProcessingStore, PostgresElliottCandidatesStore, PostgresIndicatorSnapshotStore, PostgresIdempotencyStore, PostgresAlertStore, PostgresOpenTradeCommand, PostgresElliottCandidatesQuery, PostgresAlertProcessingQuery, PostgresIndicatorSnapshotQuery, PostgresReconciliationStore, PostgresTradePlanStore, PostgresExecutionHeartbeatStore, PostgresOrderReceiptStore, PostgresLlmAdjudicationStore, PostgresExecutionIntentStore
  - Risk: LOW | Effort: ~4h
- Story M14.4.5 (HIGH): Fix CT not passed in `McpGatewayRouter.ExecuteWithDefaultAsync`
  - File: Mvp.Trading.Api/Mcp/McpGatewayRouter.cs ~line 79
  - Private method never receives or forwards ct → LLM/MCP calls are uncancellable
  - Fix: add `CancellationToken ct` parameter and propagate to action invocation
  - Risk: LOW | Effort: ~30min

**Nice to Have:**
- Story M14.4.6 (MEDIUM): Add `ConfigureAwait(false)` consistently to all awaits in library projects
  - Libraries: Elliott, Indicators, Risk, Integrations.Kraken — should avoid capturing ASP.NET sync context
  - Api and Worker projects are exempt
  - Risk: LOW | Effort: ~3h

**Done when**: CT-1 through CT-5 fixed, all Postgres stores use `OpenConnectionAsync(ct)`, build and tests green.
**Status**: Backlog

---

#### M14.5 — Error Handling and Resilience
**Research status**: ✅ Complete | **Overall**: CRITICAL — 4 fail-open financial risks identified

**Already compliant — no action needed:**
- Exception handling: disciplined throughout — `OperationCanceledException` separated and re-thrown correctly; all catch blocks log with context
- `Result<T>` envelope: uniform usage across service boundaries; no raw exception returns at domain layer
- Logging on failure: good coverage; correlation IDs included in error logs

**Must Fix (financial loss risk):**
- Story M14.5.1 (CRITICAL): Fix `KillSwitchService` fails-open on DB unreachable
  - File: Mvp.Trading.Execution/KillSwitchService.cs ~lines 42-81
  - If DB unreachable + cache miss → returns inactive kill switch → trades execute without safety check
  - Fix: return `KillSwitchStatus(active: true, level: EMERGENCY_STOP)` on any DB failure
  - Risk: HIGH — financial loss | Effort: ~1-2h
- Story M14.5.2 (CRITICAL): Fix webhook returning 202 when Redis enqueue fails
  - File: Mvp.Trading.Api/Services/RedisAlertQueue.cs ~line 31
  - Redis unavailable → alert silently dropped but caller receives 202 Accepted
  - Fix: add Polly retry; return 503 if all retries exhausted | Effort: ~3-4h
- Story M14.5.3 (CRITICAL): Fix DB persistence failures proceeding silently in worker
  - Files: PostgresAlertStore, PostgresLlmAdjudicationStore — INSERT failure throws but worker marks as processed
  - Fix: wrap in try-catch; return `Result<bool>`; do not mark processed on failure | Effort: ~2-3h
- Story M14.5.4 (CRITICAL): Replace per-process Kraken rate limit budget with distributed (Redis-backed)
  - File: Mvp.Trading.Integrations.Kraken/KrakenFuturesRateLimitBudget.cs ~lines 21-45
  - Per-process counter only — multi-instance deployments silently exceed Kraken API limits
  - Fix: Redis `INCR` + `EXPIRE` counter or enforce single-instance constraint | Effort: ~4-5h
- Story M14.5.5 (CRITICAL): Add HTTP resilience policies to all HTTP clients
  - Zero Polly/resilience policies on OpenAI, LocalLLM, Kraken clients — transient 5xx fails immediately
  - Add retry (3 attempts, exponential backoff) + circuit breaker (open after 5 failures/30s)
  - Note: overlaps M14.2.2 for Kraken — apply uniformly to all clients | Effort: ~6-8h

**Should Fix:**
- Story M14.5.6 (MEDIUM): Add global exception middleware (RFC 7807 ProblemDetails) to API
  - No `UseExceptionHandler` middleware — unhandled exceptions return raw 500 | Effort: ~1h
- Story M14.5.7 (MEDIUM): Add exponential backoff with jitter to `ExecutionService` manual retry
  - ExecutionService.cs ~line 356: fixed 200ms delay between order retries — thundering herd risk
  - Replace with: `delay = min(1000, 100 * 2^attempt) + random(0,100)` | Effort: ~30min
- Story M14.5.8 (MEDIUM): Standardize persistence `Task` return types to `Task<Result<bool>>`
  - `PostgresAlertStore.StoreAsync`, `PostgresExecutionIntentStore.SaveAsync` return plain `Task`
  - Breaks `Result<T>` consistency at persistence boundary | Effort: ~2-3h

**Done when**: all 5 critical fail-closed fixes applied; resilience policies active; tests cover all failure paths.
**Status**: Backlog

---

#### M14.6 — Performance and Memory
**Research status**: ✅ Complete | **Overall**: GOOD with targeted improvements available

**Already compliant — no action needed:**
- Return types: all public methods return `IReadOnlyList<T>` — no double-allocation smell
- Boxing: zero boxing detected; all value types passed strongly typed
- Dictionary lookups: `TryGetValue` used correctly throughout; no `ContainsKey + []` pattern
- IMemoryCache: explicit TTLs on all entries, deterministic keys, no unbounded growth risk
- String hot paths: no `+` concatenation in loops; no `string.Format()` remaining
- Collection pre-sizing: `new List<T>(count)` capacity hints used in IndicatorMath throughout

**Must Fix:**
- Story M14.6.1 (HIGH): Convert `IndicatorMath` core methods to accept `ReadOnlySpan<decimal>`
  - `ComputeRsi`, `ComputeMacd`, `ComputeStochRsi` in IndicatorMath.cs currently accept `IReadOnlyList<decimal>`
  - Called per-alert, per-timeframe — highest GC pressure point in the system
  - Estimated ~15-20% GC reduction in Indicators subsystem under sustained load
  - Update IndicatorEngine.cs callers accordingly | Risk: LOW | Effort: ~4-6h

**Should Fix:**
- Story M14.6.2 (MEDIUM): Fix `.Skip().ToList()` double-materialization in KrakenFuturesMarketDataProvider
  - Lines ~294, 302, 306, 419: `.Skip(n).ToList()` chained on already-materialized list
  - Replace with slice notation `[n..]` or defer `.ToList()` to final return
  - Eliminates double-allocation on 500-1000 element candle arrays | Risk: LOW | Effort: ~1-2h
- Story M14.6.3 (MEDIUM): Remove unnecessary `.ToList()` on closes/volumes in IndicatorEngine
  - Lines ~142-143: `.Select(c => c.Close).ToList()` and `.Select(c => c.Volume).ToList()`
  - `IndicatorMath` already accepts `IReadOnlyList<decimal>` — intermediate materializations are redundant
  - Saves ~50KB/alert across 10 timeframes | Risk: LOW | Effort: <1h

**Nice to Have:**
- Story M14.6.4 (LOW): Consolidate pivot ordering in ZigZagPivotExtractor and IndicatorEngine
  - ZigZagPivotExtractor.cs ~line 200, IndicatorEngine.cs ~line 116: `.OrderBy().ToList()` before filter
  - Combine to single pass; small GC reduction | Effort: ~1-2h

**Done when**: Span<decimal> adopted in IndicatorMath; double-materializations eliminated; build and tests green.
**Status**: Backlog

---

#### M14.7 — Testing Standards
**Research status**: ✅ Complete | **Overall**: GOOD foundations, critical coverage gaps in orchestration layer

**Already compliant — no action needed:**
- xUnit patterns: used idiomatically throughout (constructors, async, `[Fact]`, `[Theory]`)
- Determinism tests: comprehensive for Elliott and Indicator engines across all timeframes
- Test isolation: no static mutable state; 23 hand-rolled fakes well-encapsulated
- Fixtures: organized under `tests/fixtures/` with metadata (symbol, interval, capturedAtUtc)
- No external mock library — hand-rolled fakes are minimal and interface-bound

**Must Fix:**
- Story M14.7.1 (CRITICAL): Create `Mvp.Trading.Worker.Tests` project
  - AlertWorker, ReconciliationWorker, TradeMonitorWorker have ZERO unit tests — these are system entry points
  - Add coverage: message dequeue, error handling, state transitions
  - Depends on M14.9.1 (AlertWorker refactor) to be fully testable | Effort: ~2d
- Story M14.7.2 (CRITICAL): Add WebApplicationFactory integration tests for webhook endpoint
  - `/api/webhook/tradingview` has no automated tests
  - Cover: valid ingestion, duplicate idempotency key rejection, invalid JSON, oversized payload | Effort: ~2d
- Story M14.7.3 (HIGH): Unskip database-dependent KillSwitchService tests
  - KillSwitchServiceTests.cs lines 23-47 skipped with `[Fact(Skip="Requires database")]`
  - Implement TestContainers.PostgreSQL fixture | Effort: ~1d
- Story M14.7.4 (HIGH): Add execution retry logic tests
  - No tests for transient failure retry, timeout, backoff in ExecutionService | Effort: ~1d
- Story M14.7.5 (HIGH): Add rate limit exhaustion tests for Kraken integration
  - No tests for HTTP 429 handling, circuit breaker activation, retry-after parsing | Effort: ~1d

**Should Fix:**
- Story M14.7.6 (MEDIUM): Add Elliott Wave edge-case tests (empty candles, single pivot, RSI boundary 0/50/100) | Effort: ~1d
- Story M14.7.7 (MEDIUM): Implement TestContainers for PostgreSQL and Redis (enables CI/CD integration tests) | Effort: ~2d

**Nice to Have:**
- Story M14.7.8 (LOW): Centralize 23 hand-rolled fakes into shared `Mvp.Trading.TestHelpers` project | Effort: ~1d

**Done when**: Worker tests exist; webhook tests pass; skipped tests re-enabled; retry/rate-limit paths covered.
**Status**: Backlog

---

#### M14.8 — Security and Secrets Hygiene
**Research status**: ✅ Complete | **Overall**: 🚨 CRITICAL — real credentials committed to repository

**Already compliant — no action needed:**
- Runtime secret injection: `IConfiguration` + environment variable substitution — correct pattern
- Sensitive data in logs: no API keys, passwords, or account numbers in log calls; header sanitization middleware present
- `.dockerignore`: sensitive files excluded from image build

**🚨 URGENT — act immediately (do not wait for sprint):**
- Story M14.8.1 (CRITICAL): Revoke all credentials in committed `.env` files
  - `.env.prod.local` lines 30-31, 59: real Kraken production API keys + OpenAI API key
  - `.env.demo.local` line 9: ngrok authtoken
  - `.env.smoke.fixtures` lines 96-98: demo/prod credentials
  - Action: revoke at Kraken futures.kraken.com, OpenAI platform.openai.com, ngrok dashboard.ngrok.com NOW
- Story M14.8.2 (CRITICAL): Remove `.env` files from Git history
  - Use `git filter-branch` or BFG Repo-Cleaner then force-push
  - `git filter-branch --tree-filter 'rm -f .env.prod.local .env.demo.local .env.smoke.fixtures' -- --all && git gc --prune=now`
  - Must be done after credential revocation
- Story M14.8.3 (HIGH): Harden `.gitignore` and add pre-commit secret scanning hook
  - Add explicit deny rules; install `detect-secrets` or `git-secrets` pre-commit hook | Effort: ~30min

**Must Fix:**
- Story M14.8.4 (HIGH): Replace simple string `==` comparison with `CryptographicOperations.FixedTimeEquals()` for secrets
  - Program.cs ~line 141 (webhook), KillSwitchController.cs ~lines 37, 57 (kill switch)
  - Timing attack prevention for all shared-secret validation | Effort: ~1h
- Story M14.8.5 (HIGH): Add non-root `USER` to Dockerfile and Dockerfile.worker
  - Both Dockerfiles run as root — container breakout escalates to host root
  - Add `RUN useradd -m -u 1000 appuser && chown -R appuser:appuser /app && USER appuser` | Effort: ~30min

**Should Fix:**
- Story M14.8.6 (MEDIUM): Pin Docker image versions for ngrok, Prometheus, Grafana (currently `:latest`) | Effort: ~30min
- Story M14.8.7 (MEDIUM): Add `dotnet list package --vulnerable` check to CI pipeline | Effort: ~1h

**Nice to Have:**
- Story M14.8.8 (LOW): Move webhook secret from URL path to `X-Webhook-Signature` HMAC-SHA256 header (prevents secret appearing in access logs) | Effort: ~3-4h

**Done when**: credentials revoked + purged from history; constant-time comparison active; containers non-root; CI scans packages.
**Status**: Backlog

---

#### M14.9 — Code Organization and Architecture
**Research status**: ✅ Complete | **Overall**: GOOD architecture, visibility and God Class issues

**Already compliant — no action needed:**
- Project dependency graph: clean acyclic layered architecture — zero circular dependencies
- Interface segregation: all interfaces are small and focused (ISP fully respected)
- Abstraction consistency: all external dependencies (HTTP, DB, Redis, LLM) behind interfaces
- Naming conventions: PascalCase, I-prefix interfaces, Async suffix — 100% consistent
- Namespace organization: single root namespace per project, minimal folder structure

**Must Fix:**
- Story M14.9.1 (CRITICAL): Refactor `AlertWorker` God Class
  - AlertWorker.cs: 541 lines, 19 constructor dependencies, mixed concerns (queue polling, indicator computation, Elliott analysis, MCP adjudication, trade execution)
  - Split into focused services (IndicatorSnapshotService, ElliottAdjudicationService, TradeOrchestrationService)
  - Prerequisite for Worker unit tests (M14.7.1) | Effort: ~16-20h
- Story M14.9.2 (HIGH): Mark implementation classes as `internal` across all projects
  - All Options, Store, Query, Engine implementations are `public` unnecessarily
  - Add `internal` keyword; add `InternalsVisibleTo` for test projects where needed
  - Non-breaking binary change | Effort: ~2-4h
- Story M14.9.3 (HIGH): Extract large methods (102-130 lines) in AlertWorker and ExecutionService
  - `GetAdjudicationAsync`: 102+ lines; `ExecuteKrakenAsync`: 130+ lines
  - Extract into focused helper classes (McpDecisionNormalizer, OrderSubmissionHandler) | Effort: ~6-8h

**Should Fix:**
- Story M14.9.4 (MEDIUM): Split `KrakenFuturesMarketDataProvider` (925 lines) into focused classes | Effort: ~12-16h
- Story M14.9.5 (MEDIUM): Extract DI registration into `AddXxxServices()` extension methods per domain in Program.cs | Effort: ~4-6h

**Done when**: AlertWorker split; internal visibility applied; large methods extracted; build and tests green.
**Status**: Backlog

---

#### M14.10 — Remediation Report
**Research status**: ⏳ Pending (produced after M14.1–M14.9 complete)
- Consolidate all findings into `docs/backlog/technical-review-report.md`
- Severity triage: Critical / High / Medium / Low per finding with file + line reference
- Prioritized action list grouped by effort (quick wins vs refactors)
- Propose follow-up milestones for findings requiring dedicated work

**Done when**: full written report in `docs/`; all completed findings reflected as closed stories.
**Status**: Backlog

---

### M15 — Claude Multi-Agent Setup

**Goal**: Implement a fully Claude-native multi-agent pipeline, completely separate from the existing Codex/AO setup, incorporating C# 14 / .NET 10 skill knowledge and M14 anti-pattern rules into every agent run.

**Research status**: ✅ Complete — Gap analysis done 2026-05-01

#### Gap Analysis Summary (Codex → Claude)

| Area | Codex Setup | Claude Gap |
|------|------------|------------|
| Native contract file | `AGENTS.md` | Missing `CLAUDE.md` → **FIXED** |
| Orchestrator config | `agent-orchestrator.yaml` (codex-only) | Missing `claude-orchestrator.yaml` → **FIXED** |
| Gate mechanism | Shell sleep + `.done` files | File polling → `read_agent(wait:true)` → **FIXED** |
| Agent isolation | Git worktrees per role | Main worktree + branches-for-history → **FIXED** |
| Role prompts | Shell/codex-oriented | Task-tool-oriented Claude prompts → **FIXED** |
| Plan validation | ❌ None | `rubber-duck` agent inserted after planner → **ADDED** |
| Code review | Generic reviewer role | Native `code-review` agent type → **UPGRADED** |
| Skill reference | Not in agent prompts | `docs/development/csharp-dotnet10-skill.md` mandated in every role → **ADDED** |
| M14 anti-patterns | Not in agent prompts | Full anti-pattern catalogue in `CLAUDE.md` → **ADDED** |
| Dry run checklist | tmux/AO-oriented | Claude execution model checklist → **FIXED** |
| SQL state tracking | File-based only | `.done` files + SQL todos integration → **ADDED** |
| Bootstrap script | Worktree-per-agent | Bus-only bootstrap, no worktrees required → **FIXED** |

#### Stories

- Story M15.1 (**DONE**): Create `CLAUDE.md` — Claude-native multi-agent contract
  - Multi-agent roles, policies, stage order, handoff bus definitions
  - Full M14 anti-pattern catalogue with before/after code examples
  - Role instructions in Claude task-tool idiom (not shell/ao idiom)
  - Reference to `docs/development/csharp-dotnet10-skill.md` mandated for all agents
  - Rubber-duck stage inserted between planner and builder
  - `code-review` agent type used for reviewer stage
  - File: `CLAUDE.md` (repo root, auto-read by Claude)

- Story M15.2 (**DONE**): Create `claude-orchestrator.yaml` — Claude project config
  - Separate from `agent-orchestrator.yaml` (Codex stays untouched)
  - `defaults.agent: claude`, `defaults.runtime: copilot-cli`
  - `skillFile` and `contractFile` pointers for AO integration
  - 13 agent rules covering all M14 anti-patterns
  - File: `claude-orchestrator.yaml` (repo root)

- Story M15.3 (**DONE**): Create `bootstrap-feature-claude.sh`
  - Creates `/tmp/multi-agent-sync/<scope>/` bus without git worktrees
  - Creates per-role git branches from base (for history/PR review)
  - Each inbox file references CLAUDE.md, csharp-dotnet10-skill.md
  - `--force` flag for clean restart, `--no-branches` for doc-only scopes
  - File: `scripts/agents/bootstrap-feature-claude.sh`

- Story M15.4 (**DONE**): Create `run-feature-once-claude.sh`
  - Generates full orchestration prompt for all 7 stages
  - Includes parallel stage 4 (reviewer + quality in same response)
  - Fallback to `ao send` if AO gains Claude support
  - File: `scripts/agents/run-feature-once-claude.sh`

- Story M15.5 (**DONE**): Create `DRY_RUN_CHECKLIST_CLAUDE.md`
  - Full pre-flight checklist (7 file checks)
  - Stage-by-stage gate criteria for Claude execution model
  - Observability comparison table: Codex vs Claude
  - M14 anti-pattern check list at quality gate
  - Final report template
  - File: `docs/development/DRY_RUN_CHECKLIST_CLAUDE.md`

- Story M15.7 (**DONE**): Fix single-branch model — remove per-role branches from both setups
  - **Codex**: `bootstrap-feature.sh` now creates single `feature/<scope>` branch + 1 shared worktree (was: 6 per-role branches + 6 worktrees)
  - **Codex**: `run-feature-once-ao.sh` action blocks no longer say `git checkout -B agent/<role>/<scope>`; builder commits then saves `git diff main..HEAD > outbox/builder.diff`; reviewer/quality/integrator read `outbox/builder.diff`
  - **Claude**: `bootstrap-feature-claude.sh` creates single `feature/<scope>` branch (was: 6 per-role branches)
  - **Claude**: `run-feature-once-claude.sh` builder prompt commits + saves diff; reviewer/quality/integrator prompts read from `outbox/builder.diff`
  - `CLAUDE.md`, `AGENTS.md`, `DRY_RUN_CHECKLIST_CLAUDE.md` all updated with single-branch model

- Story M15.6 (Backlog): Run dry-run validation pass
  - Execute `bootstrap-feature-claude.sh --scope dryrun-claude-doc-only`
  - Run full 7-stage pipeline on a doc-only change
  - Verify all gate checks pass
  - Confirm M14 quality checks fire correctly
  - Sign off dry run as PASS before using on real feature work

**Done when**: All 7 stories complete; dry run PASS confirmed; Codex setup updated and still functional.
**Status**: M15.1–M15.5, M15.7 DONE | M15.6 Backlog

---

### M16 — Deterministic Adjudication Engine (NEW - HIGH)
**Goal**: Analyse the full LLM I/O dataflow and replace the LLM adjudication call with a pure deterministic C# engine, eliminating LLM latency, cost, and non-determinism from the trade-gating hot path.

#### Background

The current LLM adjudication call (`IMcpGateway.AdjudicateElliottAsync`) sends a JSON payload to an LLM (OpenAI or local) and asks it to evaluate Elliott Wave candidates. However, inspection of `prompts/adjudicate-elliott.md` reveals that the prompt itself already encodes a **fully deterministic step-by-step algorithm** — there is no reasoning or judgment involved; the LLM is being used as an expensive JSON-in/JSON-out rule engine.

**Key finding: the LLM is already constrained to 5 possible outputs** (`ALLOWLONGW3`, `ALLOWLONGW5END`, `ALLOWSHORTW3`, `ALLOWSHORTW5END`, `REJECT`) decided by 4 explicit boolean conditions. This is trivially implementable as a pure C# function with zero external calls.

#### Current LLM Dataflow

```
AlertWorker
  └─ GetAdjudicationAsync(ElliottAdjudicationInput, ct)
       ├─ Input type: ElliottAdjudicationInput
       │    ├─ Direction: "LONG" | "SHORT"                  ← from AlertEvent.Intent
       │    ├─ Snapshot: SignalSnapshot                      ← from IndicatorEngine
       │    ├─ Candidates: ElliottCandidates                 ← from ElliottEngine
       │    │    └─ candidates[]: ElliottCandidate[]
       │    │         ├─ candidateId: string
       │    │         ├─ waveLabel: "W3" | "W5END" | ...
       │    │         ├─ ruleViolations: RuleViolation[]     ← empty = clean candidate
       │    │         └─ invalidation:
       │    │              ├─ longInvalidationPrice: decimal?
       │    │              └─ shortInvalidationPrice: decimal?
       │    └─ Policy: RiskPolicy                            ← from risk-policy.json
       │
       └─ IMcpGateway.AdjudicateElliottAsync(input, ct)
            ├─ Renders prompt template (adjudicate-elliott.md) with {{input}} substituted
            ├─ Sends to OpenAI / LocalLLM HTTP endpoint
            └─ Returns Result<McpAdjudicationResult>
                 └─ McpAdjudicationResult wraps LlmDecision:
                      ├─ Decision: "ALLOWLONGW3" | "ALLOWLONGW5END" | "ALLOWSHORTW3" | "ALLOWSHORTW5END" | "REJECT"
                      ├─ Confidence: decimal (0.0–1.0)
                      ├─ ChosenCandidateId: string?
                      ├─ StopLossAnchor: "WAVEINVALIDATION" | "NONE"
                      └─ Notes: string
```

#### The Algorithm (already encoded in the prompt — verbatim)

```
FOR EACH candidate in Candidates:
  IF ruleViolations == []                              ← no violations
    AND direction == "LONG" AND waveLabel == "W3"
    AND longInvalidationPrice is not null
    → RETURN ALLOWLONGW3 (chosenCandidateId, confidence=0.7, anchor=WAVEINVALIDATION)

  IF ruleViolations == []
    AND direction == "LONG" AND waveLabel == "W5END"
    AND longInvalidationPrice is not null
    → RETURN ALLOWLONGW5END

  IF ruleViolations == []
    AND direction == "SHORT" AND waveLabel == "W3"
    AND shortInvalidationPrice is not null
    → RETURN ALLOWSHORTW3

  IF ruleViolations == []
    AND direction == "SHORT" AND waveLabel == "W5END"
    AND shortInvalidationPrice is not null
    → RETURN ALLOWSHORTW5END

RETURN REJECT (no candidate matched)
```

#### Stories

- Story M16.1 (ANALYSIS — DONE): Document full LLM I/O dataflow and feasibility assessment
  - **Finding**: LLM adjudication is a deterministic 4-rule filter. No probabilistic reasoning. Fully replaceable.
  - **Input**: `ElliottAdjudicationInput` (Direction + ElliottCandidates with waveLabel + ruleViolations + invalidation prices)
  - **Output**: `LlmDecision` (one of 5 enum values, fixed confidence=0.7, fixed anchor=WAVEINVALIDATION on ALLOW)
  - **Risk of LLM**: latency (500–2000ms/call), cost (token fees), non-determinism (LLM can hallucinate despite strict prompt), HTTP dependency (outage = no trades)
  - **Risk of deterministic**: none — the algorithm is already explicit and testable

- Story M16.2: Implement `DeterministicElliottAdjudicator` in `Mvp.Trading.Risk`
  - New internal sealed class `DeterministicElliottAdjudicator : IElliottAdjudicator`
  - New interface `IElliottAdjudicator` in `Mvp.Trading.Contracts` (or co-located in Risk)
  - Implements the 4-rule loop verbatim in pure C# — no HTTP, no JSON rendering, no external call
  - Returns `Result<LlmDecision>` using same contract as existing `McpAdjudicationResult`
  - Confidence fixed at `0.7m` (match current LLM default), anchor = `WAVEINVALIDATION` on ALLOW, `NONE` on REJECT
  - Unit tests: cover all 4 ALLOW paths, REJECT on violations present, REJECT on null invalidation price, REJECT on empty candidates, REJECT on direction mismatch, first-match semantics (multiple candidates — picks first valid)

- Story M16.3: Wire `DeterministicElliottAdjudicator` into `AlertWorker` as primary path
  - Replace `_mcpGateway.AdjudicateElliottAsync(input, ct)` call with `_adjudicator.AdjudicateAsync(input, ct)`
  - Keep `IMcpGateway` wired in DI but demote to optional fallback or remove entirely (decide in implementation)
  - Remove prompt template rendering path from hot path — `FilePromptTemplateStore` no longer called per alert
  - Update `AlertWorker` DI constructor: remove `IMcpGateway` if fully replaced, or mark optional
  - Update `LlmAdjudicationStore` persistence: set `llm_provider = "deterministic"`, `llm_model = "rule-engine-v1"`, `prompt_text = ""`, `raw_response = ""` (or skip persistence for deterministic path)
  - Feature flag: add `Adjudication:Mode = "deterministic" | "llm" | "llm-with-deterministic-fallback"` to config
  - `ValidateOnStart` + `[Required]` on the new options class

- Story M16.4: Add `llm-with-deterministic-fallback` mode
  - If LLM HTTP call fails or times out → fall back to deterministic engine automatically
  - Log `LogWarning` with correlation ID when fallback activates
  - Metric: `adjudication_fallback_total` counter (label: `reason=timeout|error|circuit_open`)
  - This gives zero-downtime LLM → deterministic migration path

- Story M16.5: Validation and regression tests
  - Replay all existing LLM fixture files (`tests/fixtures/llm-decisions/`) through deterministic engine
  - Assert deterministic output matches recorded LLM output for every fixture (known-good regression suite)
  - Add property-based tests: any candidate with non-empty `ruleViolations` always produces REJECT
  - Add property-based tests: any candidate with `null` invalidation price for matching direction always produces REJECT
  - Confirm `./scripts/test.sh` passes

- Story M16.6 (OPTIONAL): Remove LLM dependency entirely
  - Once M16.5 passes and deterministic mode has run in demo for 2+ weeks without issues
  - Remove `OpenAiMcpGateway`, `LocalLlmMcpGateway`, `InProcessMcpGateway` from codebase
  - Remove `adjudicate-elliott.md` prompt template (archive in docs/)
  - Remove `McpProvider` config section and `IMcpGateway` interface
  - Remove OpenAI NuGet packages
  - Estimated savings: ~$0–20/month in OpenAI tokens; 500–2000ms latency per alert removed

**Done when**: `DeterministicElliottAdjudicator` passes all fixture replay tests; feature flag controls mode; demo validated with `Adjudication:Mode=deterministic` for 5+ real alerts; optional LLM fallback documented.
**Status**: Backlog | M16.1 DONE (analysis complete — deterministic replacement confirmed feasible)
**Rationale**: The LLM prompt is already a deterministic algorithm. Using an LLM to execute it introduces latency, cost, and fragility with zero benefit. The deterministic engine is strictly superior for this use case.

---

### M17 — LLM Architecture Redesign: ADRs and Strategic Direction (NEW - STRATEGIC)
**Goal**: Define the architectural direction for where LLM adds genuine, irreplaceable value in the trading system — and where it must not be used. Produce a set of Architecture Decision Records (ADRs) that govern all future LLM integration.

#### Context: Full Analysis of Current LLM Usage

**Two active LLM tools exist today:**

| Tool | Prompt | Input | Output | Verdict |
|------|--------|-------|--------|---------|
| `adjudicateElliott` | 72-line deterministic algorithm in natural language | `Direction` + `ElliottCandidates` | 5-enum `LlmDecision` | ❌ Replace with deterministic code (M16) |
| `explainStopLoss` | 10-line stub "suggest a stop-loss anchor" | `Side` + `ElliottCandidate` + `SignalSnapshot` + `RiskPolicy` | `StopLossSuggestion(Anchor, Price?, Notes)` | ⚠️ Underdeveloped — huge untapped potential |

**Critical finding: The `SignalSnapshot` (RSI, StochRSI, MACD, Volume across all timeframes) is passed to the adjudication LLM but the prompt NEVER uses it.** The LLM only reads `direction` and `candidates`. This means the most contextually rich data in the system is being sent as dead weight on every LLM call.

**What the full `ElliottAdjudicationInput` actually contains:**
```
Direction: "LONG" | "SHORT"
SignalSnapshot:
  ├─ Timeframes[]: M5, M15, M30, H1, H2 (configurable)
  │    ├─ RSI: value + state ("OVERSOLD" | "NEUTRAL" | "OVERBOUGHT")
  │    ├─ StochRSI: K, D, state
  │    ├─ MACD: macd, signal, histogram, state ("BULLISH_CROSS" | etc.)
  │    └─ Volume: value, state, rule
ElliottCandidates:
  ├─ BaseTimeframe
  └─ candidates[]: waveLabel, score, confidence, ruleViolations, invalidation prices
RiskPolicy: maxRisk%, maxLeverage, maxNotional, allowedSides
```

**Current architecture weaknesses:**
1. `adjudicateElliott` is deterministic logic pretending to need AI — costs money, adds latency, fails non-deterministically
2. `explainStopLoss` has a meaningless 10-line prompt — no structure, no examples, no context on how to reason
3. `SignalSnapshot` (the richest context) is never analysed by any LLM
4. LLM infrastructure (OpenAI + local gateway + router + schema validation) is solid — but pointed at the wrong problems
5. No post-trade learning loop — the LLM never sees outcomes
6. No market regime awareness — the LLM doesn't know if we're in a trending or ranging market
7. `Microsoft.Extensions.AI` not used — abstraction is hand-rolled; swapping models requires code changes

**Microsoft .NET AI agent best practices (from learn.microsoft.com/dotnet/ai/conceptual/agents):**
- Agents = **reasoning + tools + context**. Use LLMs where all three are needed.
- Sequential workflows: deterministic engine → LLM advisory layer → execution
- Handoff pattern: pass to LLM only when deterministic rules cannot make a decision
- MCP servers as tools: LLM should invoke tools to get data, not receive it pre-packaged
- Observability is non-negotiable: every model call, tool invocation, and decision must be traced
- Microsoft.Extensions.AI provides provider-agnostic abstractions — prefer over custom gateway code
- Hosted agents (Azure AI Foundry): for complex multi-step reasoning scenarios at scale

#### Where LLM Adds Genuine, Irreplaceable Value in This System

| Use Case | Why LLM? | Cannot Be Deterministic Because |
|----------|----------|---------------------------------|
| **Multi-timeframe confluence quality scoring** | Reason about subtle alignment: RSI divergence + volume spike + MACD cross across 3+ timeframes is "strong" vs "weak" | Confluence patterns are fuzzy, context-dependent, and not reducible to binary thresholds |
| **Dynamic stop-loss placement reasoning** | Consider ATR, key S/R from pivot history, Elliott structure depth to choose optimal stop anchor | Requires judgment about which of several valid stop levels is best in context |
| **Market regime detection** | Classify current regime: trending, ranging, volatile, consolidating — to adapt trade sizing/approach | Regime boundaries are not crisp; requires pattern recognition across multiple signals |
| **Trade rationale narration** | Generate human-readable audit trail explaining WHY a trade was taken | Requires synthesis of multi-dimensional context into natural language |
| **Post-trade review** | After trade closes (win/loss), analyse what signals were predictive vs misleading | Requires temporal reasoning and counterfactual analysis |
| **Anomaly / override detection** | Flag unusual Elliott structures, contradictory indicator readings, or abnormal market conditions before execution | Edge cases are infinite; deterministic rules cannot enumerate them |

#### Architecture Decision Records (ADRs)

---

##### ADR-001: Separate Deterministic Gating from LLM Advisory Layer

**Status**: PROPOSED
**Decision**: The trade execution gate (ALLOW/REJECT) MUST be decided by deterministic code. The LLM layer is ADVISORY ONLY — it provides scoring, context enrichment, and quality assessment that can influence trade sizing and confidence, but cannot veto a trade that the deterministic engine approved, and cannot approve a trade that the deterministic engine rejected.

**Rationale**:
- Deterministic gating is auditable, testable, and reproducible
- LLM-as-gatekeeper introduces latency and non-determinism on the execution-critical path
- Advisory LLM can still influence outcomes via confidence scores and position sizing adjustments
- Fail-closed is guaranteed: if the LLM is unavailable, the deterministic engine already made the decision

**Implementation**:
- `DeterministicElliottAdjudicator` (M16.2): PRIMARY gate — approves or rejects based on wave/violation/invalidation rules
- `LlmConfluenceAdvisor` (M17): ADVISORY layer — scores signal quality (0.0–1.0) and returns `ConfluenceAssessment`
- Risk engine uses `ConfluenceAssessment.Score` to modulate position size (e.g., full size at 0.8+, half size at 0.5–0.8, skip below 0.5)

**Constraints**:
- LLM advisor is called AFTER deterministic gate passes — never before
- LLM advisor timeout = 3 seconds; on timeout → use default confidence (0.65) and proceed
- All LLM advisory calls must be persisted to `llm_adjudications` (M9.7)

---

##### ADR-002: Redesign LLM Input — Make SignalSnapshot the Primary Context

**Status**: PROPOSED
**Decision**: The primary input to ANY LLM call must be the full `SignalSnapshot` with rich multi-timeframe context. Elliott candidates are secondary context. The current pattern of sending Elliott candidates as primary and ignoring SignalSnapshot must be inverted.

**Rationale**:
- RSI, MACD, StochRSI, Volume across 5 timeframes is the richest market context available
- Elliott wave pattern tells you WHAT structure may be forming; indicators tell you HOW STRONG the move is
- Current prompt uses neither RSI nor MACD data — this is the most significant missed opportunity in the system
- Indicator confluence is where LLM reasoning exceeds any deterministic rule set

**New primary LLM input structure**:
```
LlmConfluenceInput:
  ├─ AlertContext: symbol, direction, timeframe, alertTime
  ├─ SignalSnapshot: FULL multi-timeframe indicator data (THIS IS PRIMARY)
  ├─ ElliottContext: chosen candidate, wave label, confidence score (THIS IS SECONDARY)
  ├─ MarketRegimeHint: trending/ranging/volatile (from a lightweight deterministic check)
  └─ RiskContext: policy limits, current exposure
```

---

##### ADR-003: Migrate to `Microsoft.Extensions.AI` for LLM Provider Abstraction

**Status**: PROPOSED
**Decision**: Replace the hand-rolled `IMcpGateway` / `OpenAiMcpGateway` / `LocalLlmMcpGateway` / `McpGatewayRouter` with `Microsoft.Extensions.AI` (`IChatClient`) for provider abstraction. Keep the existing `IMcpGateway` interface as a domain-level facade over the standardised client.

**Rationale**:
- `Microsoft.Extensions.AI` (`Microsoft.Extensions.AI` NuGet) is the .NET standard for AI abstraction
- `IChatClient` supports OpenAI, Azure OpenAI, local LLMs (Ollama), and any OpenAI-compatible endpoint
- Built-in: middleware pipeline (logging, caching, retry), function calling, structured output
- Eliminates 300+ lines of hand-rolled gateway code (`OpenAiMcpGateway`, `LocalLlmMcpGateway`)
- Auto-fallback and provider switching is built into the middleware pipeline
- Structured JSON output support (`chatClient.GetResponseAsync<T>()`) eliminates manual schema validation

**Migration path**:
- Add `Microsoft.Extensions.AI.OpenAI` and `Microsoft.Extensions.AI` NuGet packages
- Register `IChatClient` in DI with middleware: `AddOpenAIChatClient().UseLogging().UseDistributedCache()`
- Wrap `IChatClient` in a domain-level `ILlmAdvisoryGateway` that returns typed domain results
- Keep `McpGatewayRouter` as a thin orchestrator using the new client
- Remove `IOpenAiResponsesClient`, `ILocalLlmResponsesClient`, and `IJsonSchemaValidator` (replaced by MEA structured output)

---

##### ADR-004: Implement `LlmConfluenceAdvisor` — Multi-Timeframe Indicator Confluence Scoring

**Status**: PROPOSED
**Decision**: Replace the `adjudicateElliott` LLM tool with a new `LlmConfluenceAdvisor` that analyses indicator confluence across timeframes and returns a confidence score + quality assessment, not a binary ALLOW/REJECT decision.

**Rationale**:
- This is the genuine value-add: pattern recognition across RSI + MACD + StochRSI + Volume combinations
- Output is a score (0.0–1.0) that modulates position sizing — not a gate
- Prompt can be 200–400 tokens (focused); current prompt is 400+ tokens for a task a `if` statement can do

**New prompt concept for `confluenceScore` tool**:
```
Given a {direction} trade signal on {symbol} {timeframe}:

Elliott context: {waveLabel} pattern, confidence={elliottConfidence}
Indicators:
  M5:  RSI={rsi_m5} ({rsi_m5_state}), MACD={macd_m5_state}, Volume={vol_m5_state}
  M15: RSI={rsi_m15} ({rsi_m15_state}), MACD={macd_m15_state}, StochRSI K={k_m15}/D={d_m15}
  H1:  RSI={rsi_h1} ({rsi_h1_state}), MACD={macd_h1_state}

Score the confluence quality from 0.0 (poor) to 1.0 (excellent):
- Higher score when: RSI not overbought on entry direction, MACD aligned across 2+ timeframes,
  StochRSI not peaking at entry, volume confirms direction
- Lower score when: divergences present, mixed signals across timeframes, overbought/oversold extremes

Return: {"confluenceScore": 0.0-1.0, "alignedTimeframes": [...], "concerns": [...], "recommendation": "FULL_SIZE|HALF_SIZE|SKIP"}
```

---

##### ADR-005: Implement `LlmStopLossAdvisor` — Replace the 10-Line Stub Prompt

**Status**: PROPOSED
**Decision**: Rewrite `explainStopLoss` with a substantive prompt that uses Elliott structure depth, ATR-based distance, and key pivot levels to recommend an optimal stop anchor. Output must include a specific price recommendation with reasoning.

**Rationale**:
- Current `explain-stoploss.md` is 10 lines with no examples and no reasoning framework
- The deterministic stop is always `invalidation.longInvalidationPrice` / `shortInvalidationPrice` — one choice
- An LLM can reason about whether the Elliott invalidation price is too tight (risk of stop-out before the move), too wide (excessive risk), or optimal — and suggest alternatives (e.g., ATR-based stop if Elliott stop is too far)
- This is genuine judgment that cannot be deterministically encoded

**Output contract** (extends existing `StopLossSuggestion`):
```json
{
  "anchor": "WAVEINVALIDATION" | "ATR_BASED" | "KEY_SUPPORT" | "PERCENTAGE",
  "suggestedStopPrice": 94500.00,
  "riskRewardRatio": 2.8,
  "notes": "Elliott invalidation at 94,200 is 1.8% away — tight but technically correct for W3. ATR-based at 94,500 adds buffer without excessive risk.",
  "confidence": 0.82
}
```

---

##### ADR-006: Add Post-Trade LLM Review Loop

**Status**: PROPOSED
**Decision**: After each trade closes (win, loss, or manual exit), asynchronously send the full trade context (entry signal, LLM advisory, execution, outcome) to an LLM for pattern review. Store analysis in a new `trade_reviews` table.

**Rationale**:
- This is where LLM adds unique long-term value: pattern recognition over outcomes
- "Which indicator combinations predicted profitable W3 trades on BTCUSD.P M15?"
- Creates a feedback loop: `trade_reviews` data informs prompt tuning and policy updates
- Async (does not block execution path): called by `ReconciliationWorker` when trade closes
- No latency risk: review happens after-the-fact

**Trigger**: `ReconciliationWorker` detects `STATUS_MISMATCH` (filled) or trade closed → enqueue review task
**Not on critical path**: all LLM review calls are fire-and-observe, never block trading

---

##### ADR-007: Market Regime Detection as Lightweight Deterministic Pre-Filter

**Status**: PROPOSED
**Decision**: Add a lightweight deterministic market regime classifier that runs before the LLM confluence advisor. Regime output (`TRENDING` | `RANGING` | `VOLATILE` | `CONSOLIDATING`) is used as a hint in the LLM prompt and as a position sizing multiplier.

**Rationale**:
- LLM confluence scoring is most valuable in trending markets; less reliable in choppy ranging markets
- A deterministic regime check (e.g., ADX > 25 = trending, Bollinger band width percentile, etc.) can short-circuit LLM call in clearly unsuitable conditions
- Adds context to LLM prompt so the model can reason about whether Elliott patterns are reliable in current regime
- Low effort, high value: saves LLM calls in ranging markets where trades have lower expected value

**Implementation**: New `MarketRegimeClassifier` in `Mvp.Trading.Indicators` using existing indicator data (no new API calls)

---

##### ADR-008: Adopt Structured Output (`GetResponseAsync<T>`) over Manual JSON Schema Validation

**Status**: PROPOSED
**Decision**: Use `Microsoft.Extensions.AI`'s `GetResponseAsync<T>()` with `ChatResponseFormat.ForType<T>()` for all LLM calls instead of the current hand-rolled JSON schema validation pipeline.

**Rationale**:
- Current pipeline: render prompt → send to OpenAI → receive string → validate against JSON schema file → deserialize → handle errors = 5 steps, ~300 lines of code per gateway
- With MEA structured output: `await chatClient.GetResponseAsync<LlmConfluenceScore>(prompt, options)` = 1 step, ~10 lines
- Type safety at call site: compiler knows the return type
- Eliminates `IJsonSchemaValidator`, `FilePromptTemplateStore` schema loading, and all manual `JsonSerializer` calls
- OpenAI structured output mode (`response_format: json_schema`) is used automatically

**Constraint**: Keep `JsonSchema` attributes on response records for documentation and validation fallback

---

#### M17 Stories

- Story M17.1: Write full ADR document set (`docs/adr/`) — one markdown file per ADR (ADR-001 through ADR-008)
  - Each ADR: Status, Context, Decision, Rationale, Consequences, Implementation notes
  - Review with stakeholder before any implementation begins

- Story M17.2: Adopt `Microsoft.Extensions.AI` — migrate gateway infrastructure (ADR-003)
  - Add `Microsoft.Extensions.AI.OpenAI` NuGet package
  - Register `IChatClient` in DI with OpenAI and Ollama providers behind feature flag
  - Remove `IOpenAiResponsesClient`, `ILocalLlmResponsesClient`, `IJsonSchemaValidator` (hand-rolled)
  - Rewrite `OpenAiMcpGateway` and `LocalLlmMcpGateway` as thin adapters over `IChatClient`
  - Depends on M16.2 (deterministic adjudicator already replacing the gate)

- Story M17.3: Implement `LlmConfluenceAdvisor` — multi-timeframe scoring (ADR-004)
  - New tool: `confluenceScore`
  - New prompt: structured template using SignalSnapshot as primary context
  - New contract: `ConfluenceAssessment(decimal Score, string[] AlignedTimeframes, string[] Concerns, string Recommendation)`
  - Wire into AlertWorker AFTER deterministic gate passes
  - Position size modulation: `quantity = baseQuantity * confluenceScore.SizingMultiplier`

- Story M17.4: Rewrite `LlmStopLossAdvisor` — substantive stop reasoning (ADR-005)
  - New prompt with examples, ATR context, and riskRewardRatio output
  - Called after trade plan is built; can override stop price within ±20% of Elliott invalidation
  - Store full prompt/response in `llm_adjudications`

- Story M17.5: Implement `MarketRegimeClassifier` (ADR-007)
  - Deterministic classifier in `Mvp.Trading.Indicators` using ADX + Bollinger Width
  - Outputs `MarketRegime` enum passed to LLM prompt as context
  - Regime also feeds a position sizing multiplier (e.g., `RANGING` → 0.5× size cap)

- Story M17.6: Add post-trade LLM review loop (ADR-006)
  - New `trade_reviews` table: trade outcome + signal context + LLM analysis
  - `ReconciliationWorker` enqueues closed trades for async review
  - New prompt: outcome-aware analysis with recommendations

- Story M17.7: Structured output adoption for all LLM calls (ADR-008)
  - Use `GetResponseAsync<T>()` with `ChatResponseFormat.ForType<T>()` everywhere
  - Remove `FilePromptTemplateStore` JSON schema loading
  - Eliminates ~300 lines of hand-rolled validation code

**Done when**: All 8 ADRs documented and reviewed; M17.2 (MEA migration) and M17.3 (confluence advisor) implemented and validated in demo; LLM is advisory-only with deterministic gate active (M16 complete).
**Status**: Backlog | ADRs PROPOSED — require review before implementation
**Dependencies**: M16.2 (DeterministicElliottAdjudicator) must be complete before M17.3 begins

---

## Milestone M18 — Azure Security Posture (MCSB v2 Alignment)

**Objective**: Bring the deployment configuration, container definitions, and CI/CD pipeline into alignment with the Microsoft Cloud Security Benchmark v2 (MCSB v2) and the Secure Future Initiative (SFI) six-pillar model. No new business features — this milestone is purely security hardening.

**Scope boundary**:
- Azure deployment configuration (ACR, App Service, PostgreSQL, Redis, Key Vault, VNet)
- Dockerfile and docker-compose hardening
- CI/CD pipeline security controls
- Application-level secret and identity management

**Out of scope**:
- Business logic or trading algorithm changes
- New Azure services not yet in the intended architecture
- On-premises infrastructure (no on-prem in this setup)

**MCSB v2 Domains addressed**:
| Domain | Code | Stories |
|--------|------|---------|
| Identity Management | IM | M18.1, M18.2 |
| Network Security | NS | M18.3 |
| Data Protection | DP | M18.2, M18.4 |
| Privileged Access | PA | M18.4 |
| Logging & Threat Detection | LT | M18.5 |
| Posture & Vulnerability Mgmt | PV | M18.5, M18.6 |
| DevOps Security | DS | M18.6 |
| Endpoint Security | ES | M18.7 |
| AI Security | AI | M18.8 |
| Backup & Recovery | BR | M18.9 |

**Current security findings (baseline)**:
- ❌ No Terraform or Bicep source files in repo — only local `terraform.tfstate`, `terraform.tfstate.backup`, `tfplan`, `terraform.tfvars`, and `parameters.local.json` artifacts exist under `infra/`
- ❌ Current state backup shows mixed Azure hosting: API on App Service F1; Worker, monitoring, and Ollama on Azure Container Instances (ACI)
- ❌ Containers run as root — no `USER` directive in Dockerfile or Dockerfile.worker (M14.8.5 — already tracked)
- ❌ String equality for secret comparison (webhook + KillSwitch) — timing-attack vulnerable (M14.8.4 — already tracked)
- ❌ Unpinned image tags (`:latest`) for postgres, redis, ngrok, prometheus, grafana (M14.8.6 — already tracked)
- ❌ No Managed Identity — all Azure service access uses API keys/connection strings in env vars
- ❌ No Azure Key Vault — secrets in `terraform.tfvars` (gitignored but plaintext on disk)
- ❌ No VNet or private endpoints — Redis and Postgres publicly reachable in the observed Azure baseline
- ❌ No Microsoft Defender for Cloud enabled — no CSPM posture score
- ❌ No database least-privilege — postgres admin account used by application
- ❌ No secret scanning in CI — no `dotnet list package --vulnerable` gate (M14.8.7 — tracked)
- ❌ No AI security posture — OpenAI API key unrotated in local file; no prompt injection protection audit

**ADR documents**: ADR-009 through ADR-018 in `docs/adr/`

---

### M18 Stories

- Story M18.0: Reconstruct Azure IaC source of truth (BLOCKER)
  - Choose one authoritative IaC path for Azure resource changes: Terraform or Bicep; do not maintain both as competing sources of truth
  - Recreate source for the observed baseline: resource group, ACR, App Service plan, API App Service, Worker/monitoring/Ollama ACI workloads, PostgreSQL Flexible Server, PostgreSQL database, Redis, role assignments, diagnostics, and networking placeholders
  - Reconcile/import existing Azure resources from live Azure and the local `terraform.tfstate.backup`; do not commit `.tfstate`, `.tfvars`, `tfplan`, or `parameters.local.json`
  - Move Terraform state to a protected encrypted remote backend if Terraform is selected, or document the Bicep deployment-state model if Bicep is selected
  - Replace secret-bearing local variable/parameter workflows with sample templates that contain placeholders only
  - Acceptance: clean `terraform plan` or Bicep `what-if` shows no unintended destroy/recreate; M18.1-M18.5 have concrete source files to modify; local state, plan, and secret files remain gitignored

- Story M18.1: Managed Identity adoption for Azure-hosted containers (ADR-009)
  - Enable managed identity on every Azure-hosted workload: API App Service and Worker/monitoring/Ollama ACI if retained; if Worker migrates to App Service or Container Apps, document that hosting decision before implementation
  - Replace all connection string credentials with identity-based auth where possible
  - Postgres: enable Microsoft Entra authentication, configure an Entra administrator, and create database principals for the API and Worker workload identities with `pgaadauth_create_principal(...)`
  - Postgres: migrate runtime auth to Entra ID token auth (eliminates postgres_password from app runtime env)
  - Redis: migrate to Entra ID auth (Azure Cache for Redis with Entra support)
  - ACR: replace admin password pull with Managed Identity AcrPull role assignment
  - Acceptance: Azure runtime uses managed identities for ACR, Postgres, Redis, and Key Vault; local docker-compose may keep local-only development credentials

- Story M18.2: Azure Key Vault integration for remaining secrets (ADR-010)
  - Provision Key Vault resource in IaC (Bicep or Terraform)
  - Migrate: TradingView webhook secret, OpenAI API key, Kraken API keys → Key Vault secrets
  - Rotate OpenAI API key immediately before storing the replacement in Key Vault
  - Use Key Vault references in App Service config (no SDK calls in application code)
  - Enable Key Vault soft-delete and purge protection
  - Set RBAC model (not access policies) — grant Managed Identity `Key Vault Secrets User` role
  - Acceptance: `terraform.tfvars`, `parameters.local.json`, and Terraform state contain no real secret values; secrets are populated out-of-band or imported with state protection explicitly documented

- Story M18.3: Network isolation — VNet integration and private endpoints (ADR-011)
  - Create VNet with workload egress subnet(s) for App Service and ACI or the selected replacement host, plus `data-subnet` for private endpoints
  - Add private endpoint for Azure Cache for Redis
  - Add private endpoint for Azure Database for PostgreSQL Flexible Server
  - Add NSG on `data-subnet` — deny all inbound except from the approved Azure workload subnet(s)
  - Disable public network access on Redis and Postgres once private endpoints active
  - Acceptance: `nmap` from public internet cannot reach Redis or Postgres ports; API and Worker still reach both stores through the private path

- Story M18.4: Database least-privilege and encryption hardening (ADR-012)
  - Create application-specific database role/principal with SELECT/INSERT/UPDATE/DELETE on required tables only (no DDL)
  - Revoke `postgres` admin account from application connection strings
  - Verify `require_secure_transport=on` on PostgreSQL Flexible Server and set client connection `Ssl Mode=Require`
  - Enable `pgaudit` extension for database activity logging
  - Enable transparent data encryption (TDE) — on by default on Azure PG, verify and document
  - Acceptance: Azure app connects as its managed identity database principal; local-only `appuser` is permitted only for docker-compose development; admin account unusable from app containers

- Story M18.5: Microsoft Defender for Cloud and centralized monitoring (ADR-013)
  - Enable Defender for Cloud Foundational CSPM (free tier) on the subscription
  - Enable Defender for Containers — covers ACR vulnerability scanning and runtime protection
  - Enable Defender for Databases — covers PostgreSQL threat detection
  - Configure diagnostic settings: App Service logs + ACI container logs + PostgreSQL logs → Log Analytics workspace
  - Create Log Analytics workspace; connect Prometheus remote-write to Azure Monitor
  - Set up Azure Monitor alert: failed KillSwitch DB calls → PagerDuty or email
  - Acceptance: Defender CSPM score visible; at least one alert fired successfully in test

- Story M18.6: DevOps security pipeline (ADR-014)
  - Extend the existing `.github/workflows/ci.yml` restore/build/test workflow rather than replacing it blindly
  - Add `dotnet list package --vulnerable --include-transitive` as CI gate — fail on critical/high
  - Add Trivy container scan step in Docker build CI — fail on critical CVEs
  - Add secret scanning: GitHub Advanced Security or `trufflehog` on every PR
  - Pin GitHub Actions to reviewed version tags or commit SHAs; do not use mutable `@main` or `@master`
  - Upload SARIF results for both API and Worker container scans
  - Pin all `FROM` base images to SHA digest in Dockerfiles (not just version tags)
  - Add Dependabot config for NuGet and Docker base images
  - Acceptance: CI pipeline fails if any critical vulnerability found in packages or containers

- Story M18.7: Container security hardening (ADR-015)
  - Add `USER` directive to Dockerfile and Dockerfile.worker (non-root `appuser`, UID 1000)
  - Add `HEALTHCHECK` instruction to both Dockerfiles
  - Pin all docker-compose images to exact versions (no `:latest`)
  - Add read-only root filesystem where supported by the local and selected Azure container host
  - Drop all Linux capabilities where the runtime supports it; document App Service/ACI limitations instead of forcing unsupported settings
  - Acceptance: `docker inspect` shows non-root user; `:latest` absent from docker-compose.yml

- Story M18.8: AI security posture — OpenAI and local LLM hardening (ADR-016)
  - Rotate OpenAI API key immediately, then store only the replacement in Key Vault
  - Scope OpenAI API key to minimum permissions (no fine-tuning, no image gen)
  - Add request/response logging for all LLM calls (audit trail — token counts, latency, truncated prompt)
  - Implement per-call spend limit via OpenAI API usage tier settings
  - Document prompt injection threat model for adjudication and confluence scoring use cases
  - Acceptance: LLM audit log populated in Log Analytics; old API key revoked

- Story M18.9: Backup and recovery plan (ADR-017, informational)
  - Enable automated backups on PostgreSQL Flexible Server (7-day retention minimum)
  - Enable Azure Backup for Redis (Basic SKU does not support backup — document upgrade path)
  - Write runbook: database restore procedure with RTO/RPO targets
  - Acceptance: restore drill performed successfully; runbook merged to `docs/`

**Done when**: Defender CSPM score > 70%; zero critical container CVEs in CI; no plaintext secrets in IaC or Terraform state; non-root containers; all ADR-009–ADR-018 reviewed.
**Status**: Backlog | ADRs PROPOSED — require review before implementation
**Dependencies**: M18.0 is a hard blocker for M18.1-M18.5 Azure resource changes; M18.10 secret rotation/history cleanup and OpenAI key rotation are immediate owner actions; M18.1 (Managed Identity) must precede production Key Vault references in M18.2; M18.3 (VNet) must precede public network lockdown in M18.4

- Story M18.10: Git history secret removal — TradingView webhook secret (ADR-018) ✅ DONE
  - Rotated TradingView webhook secret before history rewrite
  - Used `git-filter-repo` to replace secret value with `REDACTED_WEBHOOK_SECRET` in all 6 affected commits
  - Pushed clean history to new public repo: https://github.com/rennolaj/AI-Assisted-trading
  - Acceptance: `git log --all -p | grep "1b0a08c4"` returns zero results on the public repo ✅

---

### M19 — Codebase Quality Cleanup (from external technical review)

**Goal**: Address concrete quality and maintainability findings from an external senior engineer review. No new features — this is laaghangend fruit that raises the professionalism bar for the public repo.

**Background**: External roast review (2026-07-03) identified the following issues not already tracked in M14/M18. Items already tracked (AlertWorker god class → M14.9.1, LLM gate → M16, Redis fragility → M14.5, credentials → M14.8, container security → M18.7) are not duplicated here.

#### Stories

- Story M19.1 (HIGH): Fix duplicate project references in `Mvp.Trading.sln`
  - `Mvp.Trading.Execution`, `Mvp.Trading.Contracts`, and `Mvp.Trading.Risk` each appear twice with different GUIDs
  - First reference uses `{9A19103F}` (SDK-style), second uses `{FAE04EC0}` (legacy C# project type)
  - Fix: remove the duplicate `{FAE04EC0}` entries; keep the SDK-style references
  - Signal: duplicate entries tell any senior reviewer that the solution was auto-modified without cleanup
  - Risk: LOW | Effort: ~15min

- Story M19.2 (HIGH): Replace stringly-typed `StartsWith("ALLOW")` decisions with a closed `LlmDecisionType` enum
  - `AlertWorker.cs:344` — `decision.StartsWith("ALLOW", StringComparison.OrdinalIgnoreCase)`
  - `AlertWorker.cs:534` — `normalized.Contains("SHORT", StringComparison.OrdinalIgnoreCase)` for side derivation
  - `TradePlanBuilder` — `Contains("LONG")` / `Contains("SHORT")` for direction extraction
  - Fix: introduce `LlmDecisionType` enum (`AllowLongW3`, `AllowLongW5End`, `AllowShortW3`, `AllowShortW5End`, `Reject`) and parse once at deserialization boundary
  - This also closes the ForceAllow synthetic decision security gap (`ALLOWLONGDEMO` bypasses the enum)
  - Risk: LOW | Effort: ~2h

- Story M19.3 (MEDIUM): Guard Swagger/OpenAPI behind `IsDevelopment()` in Program.cs
  - `Program.cs:122-125` — `app.UseSwagger()` and `app.UseSwaggerUI()` always active
  - Fix: wrap in `if (app.Environment.IsDevelopment())` block
  - Risk: LOW | Effort: ~15min

- Story M19.4 (MEDIUM): Fix webhook endpoint URL mismatch between docs and code
  - Actual endpoint in `Program.cs`: `POST /webhooks/tradingview/{secret}` (secret in URL path)
  - `docs/operations/deployment/production-deployment-guide.md` references `/api/v1/tradingview/webhook` with secret in JSON body
  - Fix: update production guide to show the actual endpoint, actual curl example, and actual secret placement
  - Risk: LOW | Effort: ~30min

- Story M19.5 (MEDIUM): Add Docker Compose production profile with hardened defaults
  - `docker-compose.yml` uses `ASPNETCORE_ENVIRONMENT: Development`, default `changeme` webhook secret, default `postgres/postgres` credentials, and publishes all ports to `0.0.0.0`
  - Fix: add a `docker-compose.prod.yml` override that sets `ASPNETCORE_ENVIRONMENT: Production`, removes default secrets (fail-fast if env vars not set), and restricts port bindings
  - `docker-compose.yml` remains for local dev; `docker-compose.prod.yml` is the production overlay
  - Risk: LOW | Effort: ~2h

- Story M19.6 (LOW): Redis at-least-once delivery — migrate list queue to Redis Streams
  - Currently: `ListRightPushAsync` / `ListLeftPopAsync` — pop is immediate delete, no ack
  - A worker crash after LPOP but before successful processing silently drops the alert
  - Fix: migrate to Redis Streams with consumer groups — pending entry list (PEL) provides at-least-once delivery and dead-letter visibility
  - Alternative if Redis Streams adds complexity: PostgreSQL-backed outbox pattern
  - Note: overlaps M14.5.2 (webhook 503 on Redis failure) but is a separate concern (delivery guarantee vs failure signalling)
  - Risk: MEDIUM | Effort: ~8-12h

**Done when**: .sln cleaned; enum decisions active; Swagger guarded; production doc accurate; prod Docker profile exists; Redis delivery guaranteed.
**Status**: Backlog | Triggered by external review 2026-07-03
