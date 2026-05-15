# ADR-001: Separate Deterministic Gating from LLM Advisory Layer

| Field | Value |
|-------|-------|
| **ID** | ADR-001 |
| **Date** | 2026-05-15 |
| **Status** | PROPOSED |
| **Milestone** | M16 / M17 |
| **Author** | Analysis: GitHub Copilot CLI session 2026-05-15 |
| **Supersedes** | — |

---

## Context

The current system uses an LLM (OpenAI or local) as the **primary trade gate**: if the LLM returns anything other than `ALLOWLONGW3`, `ALLOWLONGW5END`, `ALLOWSHORTW3`, or `ALLOWSHORTW5END`, the trade is rejected. No trade executes without LLM approval.

Analysis of `prompts/adjudicate-elliott.md` reveals that the **LLM prompt encodes a fully deterministic 4-rule algorithm**:

```
FOR EACH candidate:
  IF ruleViolations == []
  AND direction matches wave direction
  AND matching invalidation price is not null
  → RETURN ALLOW_{DIRECTION}_{WAVE}
RETURN REJECT
```

The LLM is being used as an expensive, non-deterministic, HTTP-dependent execution of logic that a simple C# method can perform in microseconds with 100% determinism.

**Problems with using LLM as the primary gate:**

| Problem | Impact |
|---------|--------|
| 500–2000ms latency per alert | Slower signal processing; potential missed entries on fast moves |
| Token cost per call | Ongoing OpEx with zero decision quality benefit |
| Non-determinism | Identical inputs can yield different outputs (hallucination risk) |
| HTTP dependency | LLM API outage = system cannot trade at all |
| Non-testability | Cannot write unit tests that guarantee a specific ALLOW/REJECT outcome |
| Auditability gap | "Why was this trade rejected?" requires reading LLM logs, not inspecting code |

**However, the LLM infrastructure and gateway pattern are well-built** and the `SignalSnapshot` (RSI, MACD, StochRSI, Volume across all timeframes) — which is passed to the LLM but never used — represents a genuine opportunity for LLM-based insight.

The conclusion is not to remove the LLM, but to **relocate it to where it can add irreplaceable value**: advisory enrichment after the deterministic gate has already made the binary decision.

---

## Decision

**The trade execution gate (ALLOW/REJECT) MUST be decided by deterministic code.**

The LLM layer is **advisory only**: it provides scoring, confidence enrichment, and quality assessment that influences trade sizing and operator visibility — but it does not control the ALLOW/REJECT outcome.

### Architectural layers after this ADR:

```
AlertWorker pipeline:
  1. [DETERMINISTIC] DeterministicElliottAdjudicator
     ├─ Input: ElliottCandidates + Direction
     ├─ Logic: 4-rule filter (wave label + violations + invalidation price)
     └─ Output: Result<ElliottGateDecision> → ALLOW | REJECT
                If REJECT → stop processing, persist result, done.
                If ALLOW → continue to step 2.

  2. [LLM ADVISORY] LlmConfluenceAdvisor  (only reached if step 1 ALLOWs)
     ├─ Input: Full SignalSnapshot + chosen Elliott candidate + market regime
     ├─ Logic: Multi-timeframe indicator confluence reasoning
     └─ Output: ConfluenceAssessment { Score: 0.0–1.0, SizingMultiplier, Concerns[] }
                Score is used by risk engine to modulate position size.
                Timeout (3s) → use default score (0.65), proceed normally.

  3. [DETERMINISTIC] TradePlanBuilder
     ├─ Input: ElliottGateDecision + ConfluenceAssessment + RiskPolicy
     └─ Output: TradePlan with quantity adjusted by SizingMultiplier
```

### What the LLM can and cannot do after this ADR:

| Capability | LLM Advisory | Deterministic Gate |
|-----------|-------------|-------------------|
| Reject a trade | ❌ Cannot veto | ✅ Only gatekeeper |
| Approve a trade | ❌ Cannot approve | ✅ Only approver |
| Reduce position size | ✅ Via low confluence score | ❌ Not position-aware |
| Increase position size | ✅ Via high confluence score (capped at 1.0×) | ❌ Not position-aware |
| Provide trade rationale | ✅ Notes field | ✅ Deterministic reason code |
| Operate offline | ❌ Requires HTTP | ✅ Always available |

---

## Consequences

### Positive
- **Fail-closed guaranteed**: if the LLM is unavailable, the deterministic gate has already decided; trades still execute at baseline size
- **Full auditability**: ALLOW/REJECT decisions are reproducible from code, not from LLM logs
- **Testability**: `DeterministicElliottAdjudicator` has 100% deterministic unit test coverage
- **Latency**: LLM call moves off the critical gate path; alert processing is no longer blocked by LLM response
- **Cost control**: LLM is only called when a trade is genuinely approved; rejects are free
- **LLM quality**: LLM is now free to reason about richer signals (indicator confluence) rather than executing a simple filter

### Negative / Trade-offs
- **Two-step processing**: approved trades now go through two stages (gate + advisory); total latency may be similar if LLM advisory is fast
- **Complexity**: two distinct engines with different contracts must be maintained
- **Position size linkage**: the risk engine must consume `ConfluenceAssessment.SizingMultiplier` — new coupling point

### Neutral
- Existing `IMcpGateway` interface remains; `McpGatewayRouter` is repurposed to route advisory calls
- `LlmDecision` contract is preserved for backward compatibility with `llm_adjudications` table

---

## Implementation Notes

- `DeterministicElliottAdjudicator` implemented in M16.2 (`Mvp.Trading.Risk` project)
- New interface: `IElliottGate` — single method `Evaluate(ElliottAdjudicationInput) → ElliottGateDecision`
- `ElliottGateDecision` replaces `LlmDecision` as the gate output type (or wraps it for backward compat)
- `LlmConfluenceAdvisor` implemented in M17.3 — uses `IChatClient` from `Microsoft.Extensions.AI` (see ADR-003)
- `AlertWorker` pipeline updated: call gate first, call advisor second (only on ALLOW)
- Config: `Adjudication:GateMode = "deterministic"` (only mode supported post-M16); `Adjudication:AdvisoryMode = "llm" | "disabled"`
- All advisory calls persisted to `llm_adjudications` with `provider = "openai"` or `provider = "local"` (M9.7 prerequisite)

---

## Alternatives Considered

| Alternative | Reason Rejected |
|-------------|----------------|
| Keep LLM as primary gate | Non-deterministic, expensive, fails without internet — no upside over deterministic code for this specific task |
| Remove LLM entirely | Throws away valuable infrastructure and misses real opportunity for indicator confluence reasoning |
| LLM as parallel gate (both must agree) | Adds latency and fragility with no benefit; deterministic gate is always correct by construction |
| LLM confidence score as the gate threshold | Still non-deterministic; still requires HTTP; moves complexity without resolving root issue |
