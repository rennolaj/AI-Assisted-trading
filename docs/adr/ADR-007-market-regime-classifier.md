# ADR-007: Market Regime Classifier as Deterministic Pre-Filter

| Field | Value |
|-------|-------|
| **ID** | ADR-007 |
| **Date** | 2026-05-15 |
| **Status** | PROPOSED |
| **Milestone** | M17 |
| **Author** | Analysis: GitHub Copilot CLI session 2026-05-15 |
| **Supersedes** | — |
| **Depends on** | Existing `IndicatorEngine` infrastructure |

---

## Context

The current system treats every trade alert identically regardless of market conditions. A W3 impulse setup in a strong trending market is treated the same as an identical wave structure in a choppy, low-volatility ranging market — even though their expected value differs significantly.

**Market regime is one of the most impactful factors in trading system performance:**

| Regime | Elliott Wave Quality | System Expected Value |
|--------|---------------------|----------------------|
| Trending | High — wave impulses are clean, extensions are real | High — strong directional moves |
| Ranging | Low — wave counts are ambiguous, "Elliott" patterns often fail | Low — frequent stop-outs at range boundaries |
| Volatile | Medium — large moves occur but reversals are sharp | Medium — higher risk, higher potential |
| Consolidating | Very Low — price compression, fakeouts common | Very Low — tight range, directional signals unreliable |

Currently, no component in the system has any awareness of market regime. This has two consequences:

1. **Trades are taken in unsuitable market conditions** (ranging market = frequent Elliott pattern failures)
2. **The LLM confluence advisor (ADR-004) receives no regime context** — it cannot distinguish between "RSI at 45 in a trending market" (bullish continuation likely) and "RSI at 45 in a ranging market" (mean-reversion more likely)

**A lightweight, deterministic market regime classifier** can solve both problems without adding any external API calls. The data needed (indicator values across timeframes) is already computed by `IndicatorEngine`.

### What makes regime classification deterministic-friendly:

Regime classification does NOT require fuzzy reasoning or pattern recognition across millions of examples. The core inputs are well-understood:

- **ADX (Average Directional Index)**: > 25 = trending; < 20 = ranging/consolidating
- **Bollinger Band Width**: measures current volatility relative to historical baseline; expanding = trending/volatile; contracting = consolidating
- **RSI slope across timeframes**: rising RSI on H1 + M30 = trending; oscillating RSI = ranging
- **MACD histogram momentum**: consistent direction = trending; oscillating near zero = ranging

A two-factor rule (ADX + BB width) classifies regime with acceptable accuracy for this use case.

---

## Decision

**Add a `MarketRegimeClassifier` to `Mvp.Trading.Indicators` that produces a `MarketRegime` enum using a lightweight, deterministic algorithm based on existing indicator data.** The regime is injected as context into LLM advisory prompts (ADR-004, ADR-005) and used as an independent position sizing factor.

### `MarketRegime` enum:

```csharp
public enum MarketRegime
{
    Trending,       // ADX > 25, BB expanding, directional momentum
    Ranging,        // ADX < 20, BB contracting, oscillating indicators
    Volatile,       // High BB width percentile, rapid directional changes
    Consolidating   // Very low BB width, low volume, compressed price
}
```

### Classification algorithm (deterministic, no LLM):

```
Inputs required (add to IndicatorEngine outputs or compute separately):
  - ADX value on base timeframe (or H1 as default)
  - Bollinger Band Width (BBW) — current width as % of price
  - BBW percentile over last 20 periods (relative to recent history)
  - RSI slope (RSI_current - RSI_10_periods_ago) on M15 and H1

Classification rules:
  1. IF ADX > 25 AND BBW_percentile > 60:
       → Volatile (trending but chaotic)
  
  2. IF ADX > 25 AND BBW_percentile <= 60:
       → Trending (clean directional move)
  
  3. IF ADX < 20 AND BBW_percentile < 20:
       → Consolidating (tight compression, breakout imminent)
  
  4. IF ADX < 20 AND BBW_percentile >= 20:
       → Ranging (normal oscillation, no trend)
  
  5. ELSE (ADX 20-25, mixed signals):
       → Ranging (conservative default)
```

### Position sizing multipliers by regime:

| Regime | Sizing Multiplier | Rationale |
|--------|------------------|-----------|
| `Trending` | 1.0× | Best expected value — full size |
| `Volatile` | 0.75× | Higher risk per unit — reduce exposure |
| `Ranging` | 0.5× | Lower expected value — half size |
| `Consolidating` | 0.25× | Very high fakeout risk — minimum size or skip |

The regime multiplier is **independent of and multiplicative with** the confluence multiplier (ADR-004):
```
finalQuantity = baseQuantity × confluenceSizingMultiplier × regimeSizingMultiplier
```

**Example**: FULL_SIZE confluence (1.0) in Ranging market (0.5×) → 50% of base quantity.

### Integration with LLM advisory prompts:

Regime is passed to the confluence advisor (ADR-004) and stop-loss advisor (ADR-005) as one-line context:
```
Market regime: RANGING (ADX=18, BB contracting — typical oscillation environment)
```

This allows the LLM to reason: "The indicator confluence is moderate, but in a ranging regime, RSI oscillations are expected and less predictive of sustained moves — I should lower the confluence score accordingly."

---

## Consequences

### Positive
- **Zero additional API calls**: regime classification uses data already fetched by `IndicatorEngine`
- **Independent risk reduction layer**: adds a second position sizing axis (regime) orthogonal to confluence (signal quality)
- **Better LLM prompt context**: the LLM confluence advisor can now reason about whether indicators are reliable in current regime
- **Measurable**: `MarketRegime` is logged and persisted per alert; `trade_reviews` (ADR-006) will show which regime → outcome correlations emerge
- **Configurable thresholds**: ADX and BBW thresholds are config-driven; can be tuned without code changes
- **Fast**: microsecond computation; no latency impact on alert processing

### Negative / Trade-offs
- **ADX and Bollinger Bands not in current IndicatorEngine**: the current `IndicatorEngine` computes RSI, StochRSI, MACD, Volume — ADX and BBW require new indicator implementations
  - ADX requires True Range + Directional Movement computation: moderate implementation effort
  - BBW is straightforward: `(upperBand - lowerBand) / middleBand`; requires Bollinger Band computation
  - Estimated effort: 1–2 days for `IndicatorMath` extensions + unit tests
- **Additional data points added to `SignalSnapshot`**: `ADX`, `BollingerBandWidth`, `BollingerBandWidthPercentile` per timeframe — increases `indicator_snapshots` table row size slightly
- **Threshold calibration**: ADX 25 / BBW percentile 60 thresholds are standard starting points but may need adjustment for crypto (higher volatility asset class)

### Neutral
- `MarketRegime` is a new field on `ElliottAdjudicationInput` (or `LlmConfluenceInput` per ADR-002)
- Regime multiplier is applied in `TradePlanBuilder` alongside confluence multiplier — single point of quantity adjustment

---

## Implementation Notes

### New indicator computations in `Mvp.Trading.Indicators/IndicatorMath.cs`:
```csharp
// ADX — Average Directional Index
public static decimal ComputeAdx(ReadOnlySpan<decimal> highs, ReadOnlySpan<decimal> lows,
    ReadOnlySpan<decimal> closes, int period = 14);

// Bollinger Bands
public static (decimal Upper, decimal Middle, decimal Lower)
    ComputeBollingerBands(ReadOnlySpan<decimal> closes, int period = 20, decimal multiplier = 2.0m);

// BB Width Percentile (requires rolling history of BBW values)
public static decimal ComputeBbWidthPercentile(ReadOnlySpan<decimal> bbWidths, int lookback = 20);
```

### New service in `Mvp.Trading.Indicators/`:
```csharp
public interface IMarketRegimeClassifier
{
    MarketRegime Classify(MarketRegimeInput input);
}

public sealed record MarketRegimeInput(
    decimal Adx,
    decimal BollingerBandWidthPercentile
);

internal sealed class MarketRegimeClassifier : IMarketRegimeClassifier
{
    // Thresholds injected via IOptions<MarketRegimeOptions>
    public MarketRegime Classify(MarketRegimeInput input) { ... }
}
```

### Config:
```json
"MarketRegime": {
  "AdxTrendingThreshold": 25,
  "AdxRangingThreshold": 20,
  "BbwVolatilePercentileThreshold": 60,
  "BbwConsolidatingPercentileThreshold": 20,
  "RegimeSizingMultipliers": {
    "Trending": 1.0,
    "Volatile": 0.75,
    "Ranging": 0.5,
    "Consolidating": 0.25
  }
}
```

### Addition to `TimeframeSnapshot`:
```csharp
// Existing:
public sealed record TimeframeSnapshot(Timeframe Tf, RsiState Rsi, StochRsiState StochRsi, MacdState Macd, VolumeState Volume);

// After this ADR:
public sealed record TimeframeSnapshot(Timeframe Tf, RsiState Rsi, StochRsiState StochRsi, MacdState Macd, VolumeState Volume,
    AdxState Adx, BollingerBandState BollingerBand);  // new fields

public sealed record AdxState(decimal Value, string State);  // State: "TRENDING" | "WEAKENING" | "RANGING"
public sealed record BollingerBandState(decimal Width, decimal WidthPercentile, string State); // "EXPANDING" | "CONTRACTING" | "NORMAL"
```

---

## Alternatives Considered

| Alternative | Reason Rejected |
|-------------|----------------|
| Let the LLM determine market regime from indicators | Adds LLM call before the gate; slower; non-deterministic; regime is an objective classification, not a judgment call |
| Ignore market regime entirely | Misses a major source of expected value differentiation; trades in ranging markets have demonstrably lower EV |
| Use a separate ML model for regime classification | Overkill for this use case; ADX + BB is industry-standard and well-understood; ML model adds training/inference complexity |
| Single regime multiplier (not combined with confluence) | Independence of regime and confluence is valuable — they capture different risk dimensions (market suitability vs signal quality) |
| Regime classification from price action patterns only | Price action pattern recognition is harder to make deterministic; indicator-based regime is more stable |
