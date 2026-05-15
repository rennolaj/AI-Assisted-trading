# ADR-005: LLM Stop-Loss Advisor — Substantive Stop Reasoning

| Field | Value |
|-------|-------|
| **ID** | ADR-005 |
| **Date** | 2026-05-15 |
| **Status** | PROPOSED |
| **Milestone** | M17 |
| **Author** | Analysis: GitHub Copilot CLI session 2026-05-15 |
| **Supersedes** | `explainStopLoss` tool (current 10-line stub prompt) |
| **Depends on** | ADR-001 (advisory-only LLM), ADR-002 (SignalSnapshot primary), ADR-003 (MEA) |

---

## Context

The system has a second LLM tool today: `explainStopLoss`. Its entire prompt is:

```markdown
You are a stop-loss explainer. Use the inputs below to suggest a stop-loss anchor.

Rules:
- Output must be valid JSON matching the StopLossSuggestion schema.
- Do not include prose outside JSON.
- If inputs are insufficient, return null fields with notes.

Inputs:
{{input}}
```

This 10-line prompt is effectively a stub: it tells the LLM almost nothing about how to reason about stop placement, provides no examples, no decision framework, no constraint on what constitutes a good stop vs a bad one.

**The current deterministic stop-loss placement** is also very simple: use the Elliott wave `invalidation.longInvalidationPrice` or `shortInvalidationPrice` — which is the price at which the chosen wave pattern is structurally invalidated. This is technically correct but one-dimensional:

- It does not consider whether the invalidation price is too tight relative to current volatility (risk of stop-out before the move plays out)
- It does not consider whether the invalidation price is too wide relative to the risk/reward target (degraded R:R ratio)
- It does not provide alternative stop anchors when the Elliott invalidation price is suboptimal
- It does not consider key support/resistance levels that may act as stronger stop anchors

**Real stop-loss placement in professional trading** involves multiple considerations simultaneously:
1. **Technical invalidation**: where does the wave count become structurally invalid?
2. **Volatility buffer**: is the stop far enough below key levels to avoid being swept by normal volatility?
3. **Risk/reward ratio**: given the stop and the take-profit levels, is the R:R acceptable (>1:2)?
4. **Key S/R proximity**: is the stop just below a key support (long) / above key resistance (short)?
5. **ATR filter**: is the stop distance within 1–3× ATR? Too tight = premature stop-out; too wide = excessive risk.

This is exactly the kind of multi-factor, contextual reasoning where an LLM adds genuine value over a single `invalidation.price` lookup.

---

## Decision

**Rewrite the `explainStopLoss` tool with a substantive prompt that evaluates multiple stop anchoring strategies and returns a reasoned recommendation with R:R ratio and confidence.**

The advisor is called after the `TradePlanBuilder` produces an initial plan with the Elliott invalidation stop. It can recommend an adjusted stop within a bounded range (±20% of the Elliott invalidation price). The final stop price decision remains with the risk engine, which applies the recommendation subject to hard policy limits.

### New `stopLossAdvice` LLM tool output contract:

```csharp
public sealed record StopLossAdvice(
    string RecommendedAnchor,       // "WAVEINVALIDATION" | "ATR_BASED" | "KEY_SUPPORT" | "PERCENTAGE"
    decimal RecommendedStopPrice,   // specific price recommendation
    decimal RiskRewardRatio,        // expected R:R given recommended stop + current take-profit targets
    decimal Confidence,             // 0.0–1.0 how confident the LLM is in this recommendation
    string Notes,                   // 1–2 sentence reasoning
    IReadOnlyList<string> Warnings  // e.g. ["Stop is within 0.3% of current price — very tight"]
);
```

### Stop anchor types and when each applies:

| Anchor | Price | When to recommend |
|--------|-------|------------------|
| `WAVEINVALIDATION` | Elliott `invalidation.price` | Wave invalidation is clear, ATR distance is reasonable (1–3×), R:R is acceptable |
| `ATR_BASED` | Current price ± (1.5 × ATR) | Wave invalidation is too tight (<0.5× ATR away); adds buffer |
| `KEY_SUPPORT` | Nearest pivot low / high | Wave invalidation is too wide (>3× ATR); tighter stop at key level improves R:R |
| `PERCENTAGE` | Current price ± configured% | No clear technical level; fallback to percentage-based stop |

### Prompt design for `stopLossAdvice` tool

```markdown
# prompts/stop-loss-advice.md

You are a professional stop-loss placement advisor for crypto futures trading.

## Trade Context
- Symbol: {{symbol}}
- Direction: {{direction}}
- Entry price (reference): {{entry_price}}
- Elliott wave: {{wave_label}} on {{base_timeframe}}
- Wave invalidation stop: {{invalidation_price}} ({{invalidation_distance_pct}}% from entry)
- Take-profit targets: {{take_profit_prices}}
- ATR ({{atr_timeframe}}): {{atr_value}} ({{atr_distance_pct}}% of current price)

## Indicator Context
{{indicator_summary}}  ← compact 2–3 line summary (not full table)

## Current Risk Parameters
- Max risk per trade: {{max_risk_pct}}%
- Max notional: {{max_notional}}

## Your Task
Evaluate the Elliott wave invalidation stop and recommend the optimal stop placement:

1. Calculate: Is the invalidation stop distance between 1× and 3× ATR? 
   - < 0.5× ATR → too tight, suggest ATR_BASED stop
   - > 3× ATR → too wide, suggest KEY_SUPPORT if available or PERCENTAGE
   - 1–3× ATR → WAVEINVALIDATION is appropriate

2. Estimate the risk/reward ratio: 
   - R:R = (avg take-profit distance) / (stop distance from entry)
   - If R:R < 1.5 with the Elliott stop → suggest a tighter alternative if technically valid

3. Recommend one of: WAVEINVALIDATION | ATR_BASED | KEY_SUPPORT | PERCENTAGE

## Output (strict JSON, no extra text)
{
  "recommendedAnchor": "WAVEINVALIDATION",
  "recommendedStopPrice": 94200.00,
  "riskRewardRatio": 2.8,
  "confidence": 0.82,
  "notes": "Elliott invalidation at 94,200 is 1.6% away (1.4× ATR) — appropriate buffer for W3. R:R 2.8:1 is strong.",
  "warnings": []
}
```

### Stop price acceptance rules (enforced deterministically after LLM response):

```csharp
// Hard limits applied to LLM recommendation before use:
// 1. Never tighter than 0.5× ATR from entry
// 2. Never wider than 4× ATR from entry
// 3. Never worse than 1.5:1 R:R (if take-profit targets are defined)
// 4. Always within ±30% of Elliott invalidation price (LLM cannot move stop drastically)
// 5. Always on the correct side (long stop < entry; short stop > entry)

var acceptedStop = _stopLossValidator.Clamp(
    advice.RecommendedStopPrice,
    elliottInvalidationPrice,
    entry,
    atr,
    takeProfitTargets
);
```

---

## Consequences

### Positive
- **R:R awareness**: stop placement now considers whether the trade has acceptable reward relative to risk — not just "where does the wave count break?"
- **ATR-grounded**: stops are validated against current volatility; eliminates the failure mode of stops being too tight for the instrument's normal noise level
- **Reasoned audit trail**: `StopLossAdvice.Notes` provides human-readable explanation of why the stop was placed where it was — critical for reviewing losses
- **Bounded LLM authority**: hard clamp rules (±30% of Elliott invalidation) ensure LLM cannot move the stop to an absurd level; risk engine always has the final word
- **Multiple anchor strategies**: system can now discover when `KEY_SUPPORT` or `ATR_BASED` stops outperform `WAVEINVALIDATION` over time (feeds ADR-006 post-trade review)
- **Confidence tracking**: `Confidence` field allows downstream handling (e.g., log a warning if confidence < 0.5)

### Negative / Trade-offs
- **Requires ATR data**: the current system does not explicitly pass ATR to the stop-loss tool; ATR must either be computed from the existing indicator pipeline or estimated from OHLCV data — requires data plumbing
- **Prompt complexity**: this is the most complex prompt in the system; requires careful engineering and validation with real examples before production use
- **Additional latency**: called after trade plan build, adding 500–1500ms; acceptable since it is advisory and can be run in parallel with persistence calls
- **Clamp rules must be carefully calibrated**: if the ±30% clamp is too tight, LLM recommendations are always overridden (pointless); if too wide, LLM can move stop beyond acceptable limits
- **`KEY_SUPPORT` anchor requires pivot data**: to recommend a specific support/resistance level, the LLM would need pivot price data from the Elliott engine — currently not included in the stop-loss prompt input

### Neutral
- Existing `StopLossSuggestion` record is superseded by `StopLossAdvice` (more fields); old record kept for any external references
- `ExplainStopLossAsync` method on `IMcpGateway` is renamed to `AdviseStopLossAsync` with new input/output types

---

## Implementation Notes

### ATR requirement:
- ATR must be added to `SignalSnapshot` or computed separately before calling this advisor
- Simplest approach: compute ATR from the existing OHLCV data fetched by `IndicatorEngine` (already in memory, not persisted)
- Add `AtrByTimeframe: IReadOnlyDictionary<Timeframe, decimal>` to `SignalSnapshot`

### Pivot data for `KEY_SUPPORT` anchor:
- Phase 1: Recommend only between `WAVEINVALIDATION`, `ATR_BASED`, and `PERCENTAGE` (no pivot S/R)
- Phase 2: Pass the top 3 nearby pivot highs/lows from `ElliottCandidates` data for `KEY_SUPPORT` recommendations

### Execution timing:
- Called after `TradePlanBuilder` produces initial plan (has entry price, initial stop, take-profit targets)
- Final stop price = `_stopLossValidator.Clamp(advice.RecommendedStopPrice, ...)` applied before `ExecutionService`
- If LLM times out (3s): use Elliott invalidation stop unchanged (current behaviour)

### Metrics:
- `stop_advice_anchor_total{anchor="WAVEINVALIDATION|ATR_BASED|KEY_SUPPORT|PERCENTAGE"}` counter
- `stop_advice_confidence_histogram` — distribution of confidence scores over time
- `stop_rr_ratio_histogram` — distribution of recommended R:R ratios

### Config:
```json
"StopLossAdvisor": {
  "Enabled": true,
  "TimeoutSeconds": 3,
  "MaxDeviationFromElliottPct": 30,
  "MinRiskRewardRatio": 1.5,
  "MinAtrMultiple": 0.5,
  "MaxAtrMultiple": 4.0
}
```

---

## Alternatives Considered

| Alternative | Reason Rejected |
|-------------|----------------|
| Keep the current 10-line stub prompt | Provides no real reasoning; LLM call cost not justified by output quality |
| Always use Elliott invalidation price (no LLM) | Valid default; but misses ATR validation and R:R optimisation — leaves money on the table |
| Deterministic multi-factor stop calculation | Possible for ATR check + R:R check (and these MUST be done deterministically as the clamp rules); but optimal selection between competing anchors benefits from LLM contextual reasoning |
| Remove `explainStopLoss` entirely | The use case is valid; the problem is the current prompt quality, not the concept |
| Full LLM authority over stop price (no clamp) | Too risky for financial system; hard clamp rules are non-negotiable |
