# ADR-002: SignalSnapshot as Primary LLM Context

| Field | Value |
|-------|-------|
| **ID** | ADR-002 |
| **Date** | 2026-05-15 |
| **Status** | PROPOSED |
| **Milestone** | M17 |
| **Author** | Analysis: GitHub Copilot CLI session 2026-05-15 |
| **Supersedes** | — |

---

## Context

Every LLM call in the current system receives an `ElliottAdjudicationInput` that includes:

```
ElliottAdjudicationInput:
  ├─ Direction: "LONG" | "SHORT"
  ├─ SignalSnapshot (RSI, StochRSI, MACD, Volume across 5 timeframes)  ← NEVER READ BY PROMPT
  ├─ ElliottCandidates (waveLabel, violations, invalidation prices)      ← ONLY DATA USED
  └─ RiskPolicy (maxRisk%, leverage, notional, sides)                   ← NEVER READ BY PROMPT
```

Inspecting `prompts/adjudicate-elliott.md` confirms that the prompt template only reads `input.direction` and `input.candidates.candidates`. The `SignalSnapshot` and `RiskPolicy` are serialised into the JSON payload and sent over the network but **zero lines of the prompt reference them**.

The `SignalSnapshot` contains the richest time-series context available at trade decision time:

```
Per timeframe (M5, M15, M30, H1, H2):
  RSI:      value (0–100) + state label (OVERSOLD | NEUTRAL | OVERBOUGHT)
  StochRSI: K value, D value + state label
  MACD:     macd line, signal line, histogram + state label
             (BULLISH_CROSS | BEARISH_CROSS | BULLISH | BEARISH)
  Volume:   value + state label (ABOVE_AVG | BELOW_AVG | SPIKE)
```

**Across 5 timeframes, that is 20 distinct indicator readings per alert — all currently ignored.**

The only reason the `SignalSnapshot` is in the payload is legacy: it was included in case the LLM wanted to reason about it, but the prompt was never written to use it.

**This is the most significant missed opportunity in the current LLM integration.**

The Elliott wave candidate (waveLabel, ruleViolations, invalidation price) tells you *what pattern may be forming*. The SignalSnapshot tells you *how strong, confirmed, and momentum-backed that pattern is*. Both together give a complete picture. A confluence analysis without both is incomplete.

---

## Decision

**All future LLM prompts MUST use `SignalSnapshot` as their primary context input.** Elliott candidates and pattern data are secondary context — supporting evidence for the indicator picture, not the other way around.

### New canonical LLM input ordering:

```
1. Alert Context       ← what triggered this analysis (symbol, direction, timeframe, time)
2. SignalSnapshot      ← PRIMARY: what the market indicators say right now
3. Elliott Context     ← SECONDARY: what wave pattern structure supports this direction
4. Market Regime       ← TERTIARY: what kind of market environment we are in (from ADR-007)
5. Risk Context        ← BOUNDARY: what constraints the risk engine has imposed
```

### What this means for prompt design:

**Before (current `adjudicateElliott` prompt structure — never uses SignalSnapshot):**
```
Inputs: { direction, candidates: [...] }
→ Prompt: "check if any candidate has waveLabel W3/W5END, empty violations, and a price"
```

**After (new `confluenceScore` prompt structure — SignalSnapshot is the opening context):**
```
Inputs: { alert, snapshot, elliottContext, regime, riskContext }
→ Prompt: "You are analysing a {direction} signal on {symbol} {timeframe}.

Indicator picture:
  M5:  RSI {value} ({state}), MACD {state}, Volume {state}
  M15: RSI {value} ({state}), StochRSI K={K}/D={D} ({state}), MACD {state}
  H1:  RSI {value} ({state}), MACD {state}
  [...]

Elliott confirmation:
  Wave pattern: {waveLabel} (confidence={confidence}, score={score})
  Invalidation: {price}

Market regime: {regime}

Score the indicator confluence quality for this {direction} signal from 0.0 to 1.0..."
```

### Prompt budget allocation:

For a target prompt of ~500 tokens, allocate as follows:

| Section | Token Budget | Rationale |
|---------|-------------|-----------|
| System instruction | 50 | Model role + output format |
| Alert context | 30 | Symbol, direction, timeframe |
| SignalSnapshot (primary) | 200 | 5 timeframes × 4 indicators × compact format |
| Elliott context (secondary) | 80 | Wave label, score, confidence, invalidation price |
| Market regime (tertiary) | 20 | Single enum + brief description |
| Output format spec | 80 | JSON schema + field explanations |
| Examples | 80 | 1 FULL_SIZE + 1 SKIP example |

**Total: ~540 tokens — well within GPT-4o context with room for response**

Compare to the current `adjudicateElliott` prompt which is **400+ tokens for a task that needs 0 LLM reasoning**.

---

## Consequences

### Positive
- Every LLM token spent now contributes to a decision that cannot be made deterministically
- Indicator confluence is the strongest trading signal after wave structure — now it is actually used
- Prompt is denser and more informative; LLM responses will be more contextually grounded
- `SignalSnapshot` is already computed and persisted for every alert — zero additional API calls needed
- More stable prompts: indicator values change gradually; wave patterns are noisier

### Negative / Trade-offs
- **Prompt size increases**: from ~50 tokens (current adjudicate prompt body) to ~540 tokens for confluence scoring
  - Mitigation: only called on ALLOW from deterministic gate (ADR-001), so fewer calls overall
  - Mitigation: compact inline format (not pretty-printed JSON) for the snapshot section
- **New prompt engineering required**: the current prompt is a mechanical algorithm; the new prompt requires careful design and few-shot examples to get consistent confluence scoring
- **Validation complexity**: scoring output (0.0–1.0) is harder to validate than a 5-enum decision output

### Neutral
- `ElliottAdjudicationInput` contract can be extended with a `MarketRegime` field rather than replaced
- Existing `SignalSnapshot` serialisation is already in place; no new data collection needed

---

## Implementation Notes

### New `LlmConfluenceInput` record (extends or replaces `ElliottAdjudicationInput` for advisory calls):
```csharp
public sealed record LlmConfluenceInput(
    string Symbol,
    string Direction,
    Timeframe AlertTimeframe,
    DateTimeOffset AlertTime,
    SignalSnapshot Snapshot,           // PRIMARY — full indicator data
    ElliottContext ElliottContext,     // SECONDARY — chosen candidate summary
    MarketRegime Regime,              // TERTIARY — from MarketRegimeClassifier (ADR-007)
    RiskPolicy Policy                 // BOUNDARY — risk constraints
);

public sealed record ElliottContext(
    string WaveLabel,
    decimal Score,
    decimal Confidence,
    decimal? InvalidationPrice,
    string CandidateId
);
```

### Prompt template location: `prompts/confluence-score.md`
- Use `{{symbol}}`, `{{direction}}`, `{{timeframe}}` tokens for direct substitution (not JSON blob)
- Inline indicator table format (compact, readable, token-efficient):
  ```
  | TF  | RSI       | StochRSI         | MACD           | Volume    |
  | M5  | 42 NEUTRAL| K=38 D=41 NEUTRAL| BEARISH        | ABOVE_AVG |
  | M15 | 38 NEUTRAL| K=31 D=44 NEUTRAL| BEARISH_CROSS  | SPIKE     |
  | H1  | 55 NEUTRAL| K=60 D=55 BULLISH| BULLISH_CROSS  | ABOVE_AVG |
  ```

### Breaking change risk: LOW
- `ElliottAdjudicationInput` is only consumed by `IMcpGateway.AdjudicateElliottAsync`
- After ADR-001, `adjudicateElliott` tool is replaced by `confluenceScore` tool
- Old contract can be deprecated gradually

---

## Alternatives Considered

| Alternative | Reason Rejected |
|-------------|----------------|
| Keep SignalSnapshot as secondary / optional context | Misses the primary value of LLM reasoning; indicator data is the richest signal |
| Summarise SignalSnapshot before sending (e.g., just "bullish" / "bearish" per timeframe) | Loses nuance (e.g., RSI 49 vs 28 both "neutral" but very different); LLM can handle raw values |
| Send raw candle OHLCV data instead of computed indicators | Candles are not persisted per-alert (see `docs/alert-dataflow-overview.md`); would require additional storage; computed indicators are already the right abstraction |
| Add SignalSnapshot to the existing `adjudicateElliott` prompt | The `adjudicateElliott` tool is being replaced by the deterministic gate (ADR-001 + M16); adding context to a deprecated prompt is wasted effort |
