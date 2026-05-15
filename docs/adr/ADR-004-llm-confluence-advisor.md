# ADR-004: LLM Confluence Advisor — Multi-Timeframe Indicator Scoring

| Field | Value |
|-------|-------|
| **ID** | ADR-004 |
| **Date** | 2026-05-15 |
| **Status** | PROPOSED |
| **Milestone** | M17 |
| **Author** | Analysis: GitHub Copilot CLI session 2026-05-15 |
| **Supersedes** | `adjudicateElliott` LLM tool (partially — gate function replaced by M16; advisory function is new) |
| **Depends on** | ADR-001 (advisory-only LLM), ADR-002 (SignalSnapshot primary), ADR-003 (MEA), ADR-007 (market regime) |

---

## Context

After ADR-001 moves the ALLOW/REJECT gate to deterministic code, the LLM is freed to do what it is genuinely better at: **reasoning about multi-dimensional, fuzzy signal confluence across timeframes**.

Currently, the `SignalSnapshot` is computed for every alert and contains:
- RSI (value + state) for 5 timeframes
- StochRSI (K, D, state) for 5 timeframes
- MACD (macd, signal, histogram, state) for 5 timeframes
- Volume (value, state) for 5 timeframes

**= 20 indicator readings per alert, currently ignored by all LLM prompts**

A deterministic confluence check (e.g., "RSI < 50 on M15") is possible but brittle. The value of indicator confluence lies in *combinations and context*:

- RSI at 42 in an uptrend is bullish; RSI at 42 in a downtrend is neutral
- MACD bullish cross on M15 + RSI bullish cross on H1 + volume spike = strong; any one alone = weak
- StochRSI just turning up from oversold on M5 and M15 simultaneously = high-quality long entry
- RSI overbought on M5 entering a long trade = caution, even if Elliott pattern is clean

These patterns are combinatorial and context-dependent. A 4-condition deterministic rule cannot capture them reliably. An LLM reasoning about the full indicator table can.

---

## Decision

**Implement a `LlmConfluenceAdvisor` that analyses multi-timeframe indicator confluence and returns a `ConfluenceAssessment` used to modulate position size.**

The advisor is called AFTER the deterministic gate approves a trade (ADR-001). It is non-blocking: a 3-second timeout with a default assessment allows the trade to proceed even if the LLM is unavailable.

### New `confluenceScore` LLM tool

**Input**: `LlmConfluenceInput` (defined in ADR-002)

**Output**: `ConfluenceAssessment`

```csharp
public sealed record ConfluenceAssessment(
    decimal Score,                          // 0.0 (poor) – 1.0 (excellent)
    string Recommendation,                  // "FULL_SIZE" | "HALF_SIZE" | "QUARTER_SIZE" | "SKIP"
    decimal SizingMultiplier,               // 1.0 | 0.5 | 0.25 | 0.0
    IReadOnlyList<string> AlignedTimeframes,// e.g. ["M15", "H1"]
    IReadOnlyList<string> Concerns,         // e.g. ["RSI overbought M5", "Volume below average H1"]
    string Notes                            // brief natural language summary
);
```

### Position size modulation:

| Score | Recommendation | SizingMultiplier | Description |
|-------|---------------|-----------------|-------------|
| 0.75–1.0 | `FULL_SIZE` | 1.0 | Strong multi-timeframe confluence |
| 0.50–0.74 | `HALF_SIZE` | 0.5 | Partial confluence — reduce risk |
| 0.25–0.49 | `QUARTER_SIZE` | 0.25 | Weak confluence — minimum position |
| 0.00–0.24 | `SKIP` | 0.0 | No meaningful confluence — do not trade |

> **Note**: `SKIP` from the confluence advisor does NOT reject the trade — the deterministic gate approved it. It signals that the operator or a higher-level risk rule should consider reducing or skipping. The final `SizingMultiplier` application is in `TradePlanBuilder`, where a minimum size floor applies (e.g., if minimum viable position > 0.25× base, treat `QUARTER_SIZE` as `SKIP`).

### Prompt design for `confluenceScore` tool

```markdown
# prompts/confluence-score.md

You are a multi-timeframe indicator confluence analyst for {{direction}} trade signals on crypto futures.

## Signal Context
- Symbol: {{symbol}}
- Direction: {{direction}}
- Timeframe: {{alert_timeframe}}
- Elliott wave: {{wave_label}} (confidence {{elliott_confidence}})
- Invalidation price: {{invalidation_price}}
- Market regime: {{regime}}

## Indicator Readings
| TF  | RSI (state)         | StochRSI K/D (state)       | MACD (state)        | Volume (state) |
|-----|--------------------|-----------------------------|---------------------|----------------|
{{indicator_table}}

## Task
Score the quality of indicator confluence for this {{direction}} signal.

High confluence (score 0.75–1.0) exists when:
- RSI is NOT overbought (>70) for LONG or oversold (<30) for SHORT at entry
- MACD state is aligned with direction on 2+ timeframes
- StochRSI is turning from a reversal zone (not peaking mid-range)
- Volume is ABOVE_AVG or SPIKE on at least one key timeframe
- Multiple timeframes agree on direction

Low confluence (score 0.0–0.25) exists when:
- RSI is at an extreme opposing the trade direction
- MACD is diverging from entry direction on higher timeframes
- Volume is consistently BELOW_AVG
- Only 1 timeframe supports the direction

## Output (strict JSON, no extra text)
{
  "score": 0.0–1.0,
  "recommendation": "FULL_SIZE" | "HALF_SIZE" | "QUARTER_SIZE" | "SKIP",
  "alignedTimeframes": ["M15", "H1"],
  "concerns": ["brief concern 1", "brief concern 2"],
  "notes": "1–2 sentence summary of confluence quality"
}
```

### Default assessment (used on LLM timeout or error):

```csharp
internal static readonly ConfluenceAssessment Default = new(
    Score: 0.65m,
    Recommendation: "HALF_SIZE",
    SizingMultiplier: 0.5m,
    AlignedTimeframes: [],
    Concerns: ["LLM advisory unavailable — using conservative default"],
    Notes: "Confluence advisor timed out. Proceeding at half size per fail-safe policy."
);
```

---

## Consequences

### Positive
- **First genuine LLM reasoning in the system**: LLM now analyses patterns that cannot be deterministically encoded
- **Position size becomes risk-aware**: trades with weak confluence execute at reduced size; strong confluence trades execute at full size
- **Non-blocking**: 3-second timeout with sensible default ensures trading is never blocked by LLM availability
- **Observable**: all calls persisted to `llm_adjudications` (M9.7) with full prompt/response/score
- **Self-validating**: `ConfluenceAssessment.AlignedTimeframes` and `Concerns` provide audit trail for why a score was given
- **Iterative**: prompt can be refined based on actual outcomes (feeds ADR-006 post-trade review loop)

### Negative / Trade-offs
- **LLM latency on approved trades**: adds 500–1500ms for trades that pass the deterministic gate; mitigated by 3-second timeout and parallel execution potential
- **Score calibration required**: initial score-to-sizing mapping is a hypothesis; requires backtesting against historical outcomes to validate thresholds (0.75, 0.50, 0.25)
- **Prompt engineering effort**: requires several iterations to get consistent, well-calibrated scores from the LLM
- **Evaluation complexity**: "did the confluence score correctly predict trade quality?" requires post-trade outcome data (ADR-006 provides this)
- **SKIP does not veto**: operator must decide whether to enforce SKIP at trade plan level or allow minimum position; adds a configuration decision

### Neutral
- `LlmDecision` contract is preserved for backward compatibility; `ConfluenceAssessment` is a new record alongside it
- `AlertWorker` pipeline change is additive: one new `await` call after the deterministic gate

---

## Implementation Notes

### New interface in `Mvp.Trading.Api/Mcp/`:
```csharp
public interface ILlmConfluenceAdvisor
{
    Task<Result<ConfluenceAssessment>> AssessAsync(LlmConfluenceInput input, CancellationToken ct);
}
```

### Timeout handling in `AlertWorker`:
```csharp
using var advisorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
advisorCts.CancelAfter(TimeSpan.FromSeconds(3));

var confluenceResult = await _confluenceAdvisor.AssessAsync(confluenceInput, advisorCts.Token);
var assessment = confluenceResult.Ok
    ? confluenceResult.Value!
    : ConfluenceAssessment.Default;
```

### `TradePlanBuilder` integration:
```csharp
// Existing: quantity = RiskEngine.ComputeQuantity(price, stopLoss, riskAmount)
// New:      quantity = RiskEngine.ComputeQuantity(...) * assessment.SizingMultiplier
//           quantity = Math.Max(quantity, _policy.MinViableQuantity)
```

### Prompt template location: `prompts/confluence-score.md`
- Use `{{symbol}}`, `{{direction}}`, `{{wave_label}}`, `{{indicator_table}}` tokens
- `{{indicator_table}}` is rendered as a compact Markdown table in C# (not JSON)
- Compact table format is ~100 tokens vs ~300 tokens for equivalent JSON representation

### Config:
```json
"Adjudication": {
  "AdvisoryMode": "llm",           // "llm" | "disabled"
  "AdvisoryTimeoutSeconds": 3,
  "FullSizeThreshold": 0.75,
  "HalfSizeThreshold": 0.50,
  "QuarterSizeThreshold": 0.25
}
```

---

## Alternatives Considered

| Alternative | Reason Rejected |
|-------------|----------------|
| Deterministic confluence scoring (rule-based) | Possible for simple cases but brittle for multi-dimensional combinations; loses signal on edge cases; LLM handles the fuzziness better |
| Binary LLM decision (STRONG / WEAK) | Loses granularity; a 4-level scale (FULL/HALF/QUARTER/SKIP) maps better to position sizing options |
| Separate LLM call per timeframe | Token-expensive; loses cross-timeframe reasoning (the key insight); latency scales with timeframe count |
| Use LLM score as an additional veto gate | Violates ADR-001; keeps LLM on the execution-critical path |
| Fixed confidence score (always 0.7 as today) | Current approach; has zero information value; position size is always the same regardless of signal quality |
