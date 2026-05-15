# ADR-006: Post-Trade LLM Review Loop

| Field | Value |
|-------|-------|
| **ID** | ADR-006 |
| **Date** | 2026-05-15 |
| **Status** | PROPOSED |
| **Milestone** | M17 |
| **Author** | Analysis: GitHub Copilot CLI session 2026-05-15 |
| **Supersedes** | — |
| **Depends on** | ADR-001 (LLM advisory), ADR-003 (MEA), M9.7 (LLM adjudication persistence), M7.1 (reconciliation loop) |

---

## Context

The current system makes LLM calls **before trade execution** (adjudication, stop-loss advice) but has **no mechanism to close the feedback loop**: once a trade closes, the system has no record connecting the pre-trade signals to the post-trade outcome.

This means:
- We cannot answer "did the confluence score correctly predict trade quality?"
- We cannot answer "which indicator patterns are most predictive of winning trades on BTCUSD.P M15?"
- We cannot answer "was the LLM stop recommendation better or worse than the Elliott invalidation stop?"
- We cannot improve prompts based on real outcome data
- Every trade is treated as statistically independent; no learning accumulates

**The reconciliation system (M7.1) already detects when trades close** — it identifies `STATUS_MISMATCH` and `FILL_MISMATCH` events. This is the natural trigger point for post-trade review.

**A post-trade LLM review is fundamentally different from pre-trade advisory:**
- It is async — not on the execution-critical path
- It has access to the outcome (win/loss/PnL)
- It can compare what the pre-trade advisory predicted vs what actually happened
- It builds a feedback dataset for prompt engineering and strategy refinement

This is exactly the "learn from experience" capability that makes an agentic system more than a one-shot rule engine.

---

## Decision

**After each trade closes, asynchronously send the complete trade context (entry signals, LLM advisory outputs, execution details, final outcome) to an LLM for outcome review. Persist the analysis in a new `trade_reviews` table.**

The review is entirely offline and asynchronous — it never blocks trading. It is triggered by `ReconciliationWorker` when a trade transitions to a terminal state (filled/closed/cancelled).

### What "trade closed" means:

A trade is considered closed when `ReconciliationWorker` detects that the position is no longer open:
- All orders reached terminal state (filled, cancelled, expired)
- PnL is known (from fill receipts + entry price)
- Position is flat (no remaining open orders on this trade)

### Post-trade review payload:

```csharp
public sealed record TradeReviewInput(
    // Identity
    Guid TradeId,
    string Symbol,
    string Direction,
    DateTimeOffset EntryTime,
    DateTimeOffset CloseTime,
    
    // Pre-trade signals (what we saw before trading)
    SignalSnapshot SignalAtEntry,          // indicator state at alert time
    ElliottContext ElliottAtEntry,         // wave pattern used for entry
    MarketRegime RegimeAtEntry,            // market regime at entry
    
    // Pre-trade advisory outputs (what the LLM said before trading)
    ConfluenceAssessment? ConfluenceAdvice,  // from LlmConfluenceAdvisor (ADR-004)
    StopLossAdvice? StopAdvice,              // from LlmStopLossAdvisor (ADR-005)
    
    // Execution details
    decimal EntryPrice,
    decimal StopLossPrice,
    decimal[] TakeProfitPrices,
    decimal PositionSize,
    decimal SizingMultiplier,               // from ConfluenceAssessment
    
    // Outcome
    decimal RealizedPnL,
    decimal RealizedPnLPct,
    string CloseReason,                     // "STOP_LOSS" | "TAKE_PROFIT_1" | ... | "MANUAL" | "RECONCILIATION"
    decimal MaxAdverseExcursion,            // worst price against position during trade
    decimal MaxFavourableExcursion,         // best price in favour of position during trade
    TimeSpan TradeDuration
);
```

### Post-trade review output:

```csharp
public sealed record TradeReview(
    string Outcome,                         // "WIN" | "LOSS" | "BREAKEVEN"
    string OutcomeAnalysis,                 // narrative: why did this trade win/lose?
    IReadOnlyList<string> CorrectSignals,   // signals that predicted the outcome correctly
    IReadOnlyList<string> MisleadingSignals,// signals that contradicted the outcome
    string ConfluenceAssessmentAccuracy,    // was the confluence score well-calibrated?
    string StopPlacementAssessment,         // was the stop-loss placement good?
    IReadOnlyList<string> LessonsLearned,   // actionable observations for future similar setups
    string OverallRating                    // "EXCELLENT_SETUP" | "GOOD_SETUP" | "MARGINAL" | "POOR_SETUP"
);
```

### Database schema for `trade_reviews`:

```sql
CREATE TABLE trade_reviews (
    review_id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    trade_id          UUID NOT NULL,
    alert_id          UUID REFERENCES alerts(alert_id),
    
    -- Outcome
    realized_pnl      DECIMAL(18,8) NOT NULL,
    realized_pnl_pct  DECIMAL(8,4) NOT NULL,
    close_reason      VARCHAR(50) NOT NULL,
    trade_duration_s  INTEGER NOT NULL,
    max_adverse_excursion  DECIMAL(18,8),
    max_favourable_excursion DECIMAL(18,8),
    
    -- LLM Review
    review_prompt     TEXT NOT NULL,
    raw_review        TEXT NOT NULL,
    outcome           VARCHAR(20) NOT NULL,    -- WIN | LOSS | BREAKEVEN
    outcome_analysis  TEXT NOT NULL,
    correct_signals   JSONB NOT NULL DEFAULT '[]',
    misleading_signals JSONB NOT NULL DEFAULT '[]',
    lessons_learned   JSONB NOT NULL DEFAULT '[]',
    overall_rating    VARCHAR(30) NOT NULL,
    confluence_accuracy TEXT,
    stop_assessment   TEXT,
    
    -- Metadata
    llm_provider      VARCHAR(50) NOT NULL,
    llm_model         VARCHAR(100),
    response_time_ms  INTEGER,
    reviewed_at_utc   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    CONSTRAINT fk_trade_review_alert FOREIGN KEY (alert_id) REFERENCES alerts(alert_id)
);

CREATE INDEX idx_trade_reviews_trade    ON trade_reviews(trade_id);
CREATE INDEX idx_trade_reviews_outcome  ON trade_reviews(outcome);
CREATE INDEX idx_trade_reviews_rating   ON trade_reviews(overall_rating);
CREATE INDEX idx_trade_reviews_time     ON trade_reviews(reviewed_at_utc DESC);
```

### Trigger in `ReconciliationWorker`:

```csharp
// In ReconciliationWorker.ProcessDiscrepancyAsync()
if (discrepancy.Type == DiscrepancyType.StatusMismatch
    && newStatus is OrderStatus.Filled or OrderStatus.Cancelled)
{
    if (IsTradeFullyClosed(tradeId))
    {
        // Fire-and-forget: do not await, do not block reconciliation
        _ = Task.Run(() => _tradeReviewService.ReviewAsync(tradeId, CancellationToken.None));
    }
}
```

### Post-trade review prompt concept:

```markdown
You are a trading strategy analyst reviewing a completed trade to extract lessons.

## Trade Result
- Symbol: BTCUSD.P | Direction: LONG | Wave: W3
- Entry: $95,500 | Stop: $94,200 (1.36% risk) | Target 1: $97,100 | Target 2: $98,800
- Outcome: STOP_LOSS at $94,200 | PnL: -$127 (-1.35%)
- Duration: 2h 14m
- Max adverse excursion: -1.4% (stop hit)
- Max favourable excursion: +0.8% (never reached T1)

## Pre-Trade Signals
Confluence score: 0.62 (HALF_SIZE)
Concerns flagged: ["RSI slightly elevated M15", "Volume below average H1"]
Elliott W3 confidence: 0.71

Indicators at entry:
  M5:  RSI 61 NEUTRAL | MACD BULLISH | Volume BELOW_AVG
  M15: RSI 58 NEUTRAL | StochRSI K=71 D=68 NEUTRAL | MACD BULLISH_CROSS | Volume BELOW_AVG
  H1:  RSI 55 NEUTRAL | MACD BULLISH | Volume ABOVE_AVG

## Analysis Questions
1. Were the pre-trade concerns (StochRSI elevated, low volume) predictive of the loss?
2. Was the confluence score (0.62) well-calibrated for this outcome?
3. Was the stop placement appropriate? Did MAE suggest it was too tight?
4. What would a better entry timing look like for this setup?
5. What lessons apply to future W3 LONG trades with similar indicator profile?
```

---

## Consequences

### Positive
- **Closes the feedback loop**: system now accumulates outcome-correlated signal data
- **Prompt calibration data**: `confluence_accuracy` field tells us whether score 0.62 correctly signals "weaker setup" over many trades
- **Strategy refinement input**: `lessons_learned` and `misleading_signals` feed directly into prompt iteration for ADR-004 and ADR-005
- **Operator intelligence**: `trade_reviews` table is a queryable strategy review database — "show me all LOSS trades with confluence score > 0.7" reveals where the system over-trusts its signals
- **Zero execution impact**: fully async, fire-and-forget, never blocks trading or reconciliation
- **Persistence-first**: even if the LLM review fails, the outcome data (PnL, close reason, MAE, MFE) is persisted — review can be retried

### Negative / Trade-offs
- **Delayed insight**: reviews happen after trade closes, not in real time — no intraday learning
- **Requires M9.7**: the review needs pre-trade advisory outputs (confluence score, stop advice) which must be persisted at trade time to be available post-trade; M9.7 (LLM adjudication persistence) is a prerequisite
- **Data availability**: `MaxAdverseExcursion` and `MaxFavourableExcursion` require tick-level or per-minute position tracking — currently not implemented; may need to use fill prices as proxy initially
- **Volume of reviews**: a busy day with 20 trades = 20 LLM calls to the review endpoint; token cost scales with trading activity
- **Actionability latency**: insights from post-trade review require human review of `trade_reviews` table to translate into prompt changes — no automated prompt update loop (by design)

### Neutral
- `ReconciliationWorker` already has the trade lifecycle information needed to trigger reviews
- Review quality improves over time as the prompt is refined based on aggregated insights

---

## Implementation Notes

### New service: `ITradeReviewService`
```csharp
public interface ITradeReviewService
{
    Task ReviewAsync(Guid tradeId, CancellationToken ct);
}
```

### Resilience: store outcome data first, then attempt LLM review
```csharp
// 1. Compute outcome metrics (deterministic, always succeeds)
var outcome = await _outcomeCalculator.ComputeAsync(tradeId, ct);
await _tradeOutcomeStore.SaveAsync(outcome, ct);  // persisted regardless of LLM

// 2. Attempt LLM review (may fail — that is OK)
try
{
    var review = await _llmReviewer.ReviewAsync(outcome, ct);
    await _tradeReviewStore.SaveAsync(review, ct);
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "Post-trade LLM review failed for trade {TradeId} — outcome data preserved", tradeId);
}
```

### Useful queries enabled by `trade_reviews`:
```sql
-- Confluence score calibration: does higher score → better outcomes?
SELECT
    ROUND(rs.confluence_score, 1) as score_bucket,
    COUNT(*) as trades,
    AVG(r.realized_pnl_pct) as avg_pnl_pct,
    SUM(CASE WHEN r.outcome = 'WIN' THEN 1 ELSE 0 END)::float / COUNT(*) as win_rate
FROM trade_reviews r
JOIN llm_adjudications rs ON rs.alert_id = r.alert_id
GROUP BY score_bucket ORDER BY score_bucket;

-- Most common misleading signals
SELECT jsonb_array_elements_text(misleading_signals) as signal, COUNT(*) as frequency
FROM trade_reviews WHERE outcome = 'LOSS'
GROUP BY signal ORDER BY frequency DESC LIMIT 10;
```

---

## Alternatives Considered

| Alternative | Reason Rejected |
|-------------|----------------|
| Real-time intrabar LLM monitoring of open positions (M8 concept) | On the execution path; adds latency and fragility; position monitoring is better done deterministically with predefined levels |
| Human-only post-trade review (no LLM) | Does not scale; manual review of 20 trades/day is unrealistic |
| Batch weekly LLM review (not per-trade) | Loses individual trade context; cross-trade pattern mixing reduces specificity |
| Automated prompt update based on review | Too risky without human oversight; LLM-generated prompts may introduce subtle biases; human review of `trade_reviews` → manual prompt iteration is safer |
| Skip MAE/MFE tracking initially | Reduces review quality significantly; at minimum, use best/worst fill prices as proxy |
