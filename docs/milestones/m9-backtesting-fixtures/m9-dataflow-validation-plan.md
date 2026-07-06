# M9: Complete Dataflow Analysis & Validation Plan

**Date**: January 9, 2026  
**Purpose**: Document the complete end-to-end dataflow from TradingView alert to trade execution, identify validation checkpoints, and create a plan to verify logical consistency across all pipeline stages.

---

## Executive Summary

This document provides a comprehensive analysis of the trading system's data pipeline to support M9 Story 9.5 and 9.6. It maps every transformation, decision point, and validation checkpoint from the moment a TradingView webhook arrives until a trade is executed or rejected.

**Key Questions Answered:**
1. What data flows through each stage?
2. What transformations occur at each boundary?
3. What are the validation checkpoints?
4. How do we verify the pipeline is logically consistent?
5. What test scenarios are needed to validate correctness?

---

## 1. End-to-End Pipeline Overview

```
┌─────────────────┐
│ TradingView     │
│ Pine Script     │
│ Alert Trigger   │
└────────┬────────┘
         │ Webhook (JSON)
         ↓
┌─────────────────┐
│ Stage 1:        │
│ API Ingestion   │──→ alerts table (raw + normalized)
│ (AlertService)  │──→ idempotency_keys table
└────────┬────────┘
         │ Redis Queue
         ↓
┌─────────────────┐
│ Stage 2:        │
│ Worker Dequeue  │──→ alert_processing table (status tracking)
│ (AlertWorker)   │
└────────┬────────┘
         │
         ↓
┌─────────────────┐
│ Stage 3:        │
│ Indicator       │──→ indicator_snapshots table
│ Computation     │    (RSI/MACD/Stoch/Volume per timeframe)
│ (IndicatorEng.) │
└────────┬────────┘
         │ IndicatorSnapshot (score, direction, risk)
         ↓
┌─────────────────┐
│ Stage 4:        │
│ Elliott Wave    │──→ elliott_candidates table
│ Analysis        │    (pivots, patterns, invalidations)
│ (ElliottEngine) │
└────────┬────────┘
         │ ElliottCandidates (10 max candidates)
         ↓
┌─────────────────┐
│ Stage 5:        │
│ LLM             │──→ alert_processing (llm reasoning - NOT STORED YET)
│ Adjudication    │──→ Decision: ALLOW*/REJECT/UNCERTAIN
│ (McpService)    │
└────────┬────────┘
         │ LlmDecision (decision enum + selected candidate)
         ↓
┌─────────────────┐
│ Stage 6:        │
│ Trade Plan      │──→ trade_plan table
│ Building        │    (entry, stop, targets, quantities)
│ (TradePlanBldr) │
└────────┬────────┘
         │ TradePlan
         ↓
┌─────────────────┐
│ Stage 7:        │
│ Demo Execution  │──→ execution_intent table
│ (ExecutionSvc)  │──→ order_receipt table
└────────┬────────┘──→ fill_receipt table
         │             execution_heartbeat table
         ↓
┌─────────────────┐
│ Kraken Futures  │
│ Demo API        │
└─────────────────┘
```

---

## 2. Stage-by-Stage Data Transformations

### Stage 1: TradingView → API Ingestion

**Input**: TradingView webhook JSON
```json
{
  "ticker": "BTC/USD:BTCUSD.P",
  "exchange": "KRAKEN",
  "interval": "1",
  "close": "95234.56",
  "volume": "1234.56",
  "alert_direction": "LONG",  // Optional
  "reason": "HIGH CONFIDENCE LONG"
}
```

**Transformations**:
1. **Authentication**: Verify shared secret
2. **Normalization**: Parse to `AlertEvent` contract
   - Extract symbol from ticker (BTCUSD.P)
   - Parse interval to TimeSpan
   - Store alert direction hint (LONG/SHORT)
3. **Idempotency**: Check `idempotency_key` (hash of ticker+close+timestamp)
4. **Persistence**: 
   - `alerts` table: raw_payload + alert_json (normalized AlertEvent)
   - `idempotency_keys` table: deduplication tracking
5. **Enqueue**: Push to Redis queue

**Output**: Alert ID + enqueued status

**Validation Checkpoints**:
- ✅ Webhook received within 3s ACK window
- ✅ JSON schema valid
- ✅ Ticker format valid (symbol:kraken_symbol)
- ✅ Not duplicate (idempotency check)

---

### Stage 2: Worker Dequeue → Alert Processing

**Input**: Alert ID from Redis queue

**Transformations**:
1. **Dequeue**: Pop from Redis
2. **Load Alert**: Fetch from `alerts` table
3. **Status Tracking**: Create/update `alert_processing` record
4. **Orchestration**: Coordinate indicator → Elliott → LLM → plan → execution pipeline

**Output**: Alert processing context

**Validation Checkpoints**:
- ✅ Alert exists in database
- ✅ Not already processing (concurrent guard)
- ✅ Status transitions tracked (received → indicators → elliott → adjudication → plan → execution)

---

### Stage 3: Indicator Computation

**Input**: 
- Symbol (BTCUSD.P)
- Timeframes ([5m, 15m, 30m, 1h, 2h] - configurable)
- Alert direction hint (LONG/SHORT/null)

**Data Collection**:
- Fetches OHLCV data per timeframe from Kraken Charts API
- Lookback periods (configurable):
  - M5: 288 bars (1 day)
  - M15: 96 bars (1 day)
  - M30: 48 bars (1 day)
  - H1: 24 bars (1 day)
  - H2: 12 bars (1 day)

**Transformations**:
1. **RSI Calculation**: 14-period RSI per timeframe
   - Overbought: >70
   - Oversold: <30
2. **Stochastic RSI**: %K and %D lines
   - Oversold: %K < 20
   - Overbought: %K > 80
3. **MACD**: Signal line crossovers
   - Bullish: MACD > Signal
   - Bearish: MACD < Signal
4. **Volume Analysis**: Relative volume vs average
5. **Confirmations**: Count bullish/bearish signals across timeframes
6. **Scoring**: 0-100 based on confirmation count and strength
7. **Direction**: LONG (bullish bias) or SHORT (bearish bias)
8. **Risk Category**: HIGH/MEDIUM/LOW based on score thresholds

**Output**: `IndicatorSnapshot`
```json
{
  "symbol": "BTCUSD.P",
  "direction": "LONG",
  "score": 75,
  "confirmations": 4,
  "risk": "MEDIUM",
  "timeframes": {
    "M5": { "rsi": 65, "stochRsi": 72, "macd": "bullish", ... },
    "M15": { ... },
    ...
  }
}
```

**Validation Checkpoints**:
- ✅ All timeframes computed successfully
- ✅ Minimum bars requirement met (or explicit low-data fallback)
- ✅ Score in range [0, 100]
- ✅ Direction is LONG or SHORT
- ✅ Risk is HIGH/MEDIUM/LOW
- ✅ Alert direction hint matches indicator direction (if provided)

**Critical Cross-Reference**:
- **Pine Script Alert Direction** vs **Computed Indicator Direction**
  - If Pine says LONG but indicators say SHORT → potential mismatch
  - System should handle this gracefully (LLM will see both)

---

### Stage 4: Elliott Wave Analysis

**Input**:
- Symbol (BTCUSD.P)
- Base timeframe (M1 default)
- Indicator direction (LONG/SHORT)

**Data Collection**:
- Fetches 1440 bars (1 day) of M1 OHLCV data
- ZigZag deviation: 3% (configurable)

**Transformations**:
1. **Pivot Extraction**: ZigZag algorithm identifies swing highs/lows
2. **Pattern Recognition**: Search for Wave 3 (W3) and Wave 5 End (W5END) patterns
3. **Rule Validation**: Check Elliott Wave rules
   - Wave relationships (2 retraces 1, 3 extends 1, etc.)
   - Violations tracked: ERROR (critical), WARNING (minor), INFO
4. **Invalidation Calculation**: 
   - **Long invalidation**: Stop below last pivot (for bullish patterns)
   - **Short invalidation**: Stop above last pivot (for bearish patterns)
   - W3 patterns: Can be bullish OR bearish (IsUptrend flag determines)
   - W5END patterns: Reversal signals (bullish W5END → short invalidation)
5. **Scoring**: 0-100 based on rule adherence
6. **Ranking**: Top 10 candidates by score

**Output**: `ElliottCandidates` (array of up to 10 candidates)
```json
{
  "candidates": [
    {
      "pattern": "W3",
      "score": 85,
      "isUptrend": true,
      "longInvalidationPrice": 82000.50,
      "shortInvalidationPrice": null,
      "violations": ["EW_W3_NOT_LONGEST:INFO"],
      "pivots": [ ... ]
    },
    {
      "pattern": "W3",
      "score": 78,
      "isUptrend": false,
      "longInvalidationPrice": null,
      "shortInvalidationPrice": 86000.75,
      "violations": [],
      "pivots": [ ... ]
    },
    ...
  ]
}
```

**Validation Checkpoints**:
- ✅ Sufficient pivots extracted (minimum 5 for W3, 7 for W5END)
- ✅ Candidates sorted by score (descending)
- ✅ Max 10 candidates returned
- ✅ Each candidate has valid invalidation price (long OR short, not both)
- ✅ Pattern type is W3 or W5END
- ✅ Violations properly categorized (ERROR/WARNING/INFO)

**Critical Cross-Reference**:
- **Indicator Direction** vs **Elliott Candidates**
  - LONG alert should prefer candidates with `longInvalidationPrice != null`
  - SHORT alert should prefer candidates with `shortInvalidationPrice != null`
  - Mismatch is VALID (bearish Elliott during uptrend) - LLM decides

---

### Stage 5: LLM Adjudication

**Input**:
```json
{
  "alertPayload": { ... },           // Original webhook data
  "indicatorSnapshot": { ... },      // Stage 3 output
  "elliottCandidates": [ ... ]       // Stage 4 output (up to 10)
}
```

**Transformations**:
1. **Context Assembly**: Combine all inputs into LLM prompt
2. **Prompt Rendering**: Load `prompts/adjudicate-elliott.md` template
3. **LLM Invocation**: OpenAI/Local LLM call with strict schema
4. **Schema Validation**: Enforce `LlmDecision.schema.json`
5. **Decision Extraction**: Parse JSON response

**LLM Decision Logic** (as defined in prompt):
```
Rules:
1. Check indicatorSnapshot.direction (LONG or SHORT)
2. For LONG trades:
   - ONLY consider candidates with longInvalidationPrice != null
   - Choose best scoring candidate
   - Decision: ALLOWLONGW3 or ALLOWLONGW5END
3. For SHORT trades:
   - ONLY consider candidates with shortInvalidationPrice != null
   - Choose best scoring candidate
   - Decision: ALLOWSHORTW3 or ALLOWSHORTW5END
4. If no matching candidates:
   - Decision: REJECT
5. If inputs insufficient/inconsistent:
   - Decision: UNCERTAIN (fail-closed)
```

**Output**: `LlmDecision`
```json
{
  "decision": "ALLOWLONGW3",           // Enum: ALLOWLONGW3, ALLOWLONGW5END, ALLOWSHORTW3, ALLOWSHORTW5END, REJECT, UNCERTAIN
  "candidateId": "candidate_2",        // Selected Elliott candidate
  "stopLossAnchor": "WAVEINVALIDATION", // Always WAVEINVALIDATION
  "notes": "Strong W3 pattern with 4 confirmations, no critical violations"
}
```

**Validation Checkpoints**:
- ✅ LLM output is valid JSON
- ✅ Decision enum is valid (6 possible values)
- ✅ Selected candidateId exists in input candidates
- ✅ Decision matches candidate invalidation price:
  - ALLOWLONG* → candidate has longInvalidationPrice
  - ALLOWSHORT* → candidate has shortInvalidationPrice
- ✅ StopLossAnchor is WAVEINVALIDATION (only valid option currently)
- ✅ Notes field present (for debugging/audit)

**Critical Cross-References**:
- **Indicator Direction** vs **LLM Decision Direction**
  - indicatorSnapshot.direction = "LONG" → expect ALLOWLONG* or REJECT
  - indicatorSnapshot.direction = "SHORT" → expect ALLOWSHORT* or REJECT
  - Mismatch → REJECT or UNCERTAIN (LLM safety)
- **Alert Direction Hint** vs **LLM Decision**
  - alertPayload.alert_direction = "LONG" → should align with ALLOWLONG*
  - If mismatch → valid if indicators/Elliott support it
- **Elliott Candidate** vs **LLM Decision**
  - Selected candidate MUST have matching invalidation price for decision type

**Known Issue** (M9.5 investigation):
- LLM currently struggles to find `indicatorSnapshot.direction` field
- Prompt recently updated to use full JSON path
- Need to validate LLM can reliably extract direction from nested JSON

---

### Stage 6: Trade Plan Building

**Input**:
- `LlmDecision` (decision, candidateId, stopLossAnchor)
- Selected `ElliottCandidate` (invalidation price, pattern)
- `IndicatorSnapshot` (risk category)
- Risk policy configuration (1% risk per trade default)
- Account equity ($500,000 default)

**Transformations**:
1. **Side Determination**: 
   - ALLOWLONG* → Side = LONG (BUY)
   - ALLOWSHORT* → Side = SHORT (SELL)
2. **Entry Price**: Current market price (from ticker close)
3. **Stop Loss**: 
   - LONG: longInvalidationPrice from Elliott candidate
   - SHORT: shortInvalidationPrice from Elliott candidate
4. **Risk Calculation**:
   - Risk amount = equity × risk_percentage (e.g., $500k × 1% = $5,000)
   - Point risk = |entry - stop| (e.g., |95000 - 94000| = 1000 points)
   - Quantity = risk_amount / point_risk (e.g., 5000 / 1000 = 5 contracts)
5. **Quantity Rounding**: Round to instrument qtyStep (e.g., 1.0 for whole contracts)
6. **Take-Profit Targets**: 3+ targets at multiples of point risk
   - Target 1: entry + (2 × point_risk) - 40% allocation
   - Target 2: entry + (3 × point_risk) - 30% allocation
   - Target 3: entry + (4 × point_risk) - 30% allocation

**Output**: `TradePlan`
```json
{
  "planId": "uuid",
  "alertId": "uuid",
  "side": "LONG",
  "symbol": "BTCUSD.P",
  "entryPrice": 95000.00,
  "stopLossPrice": 94000.00,
  "quantity": 5.0,
  "targets": [
    { "price": 97000.00, "quantity": 2.0 },  // 40% at 2R
    { "price": 98000.00, "quantity": 1.5 },  // 30% at 3R
    { "price": 99000.00, "quantity": 1.5 }   // 30% at 4R
  ],
  "riskAmount": 5000.00,
  "riskPercentage": 1.0,
  "llmDecision": "ALLOWLONGW3"
}
```

**Validation Checkpoints**:
- ✅ Side matches LLM decision (LONG for ALLOWLONG*, SHORT for ALLOWSHORT*)
- ✅ Stop loss matches Elliott invalidation price
- ✅ Quantity > 0 (not rounded to zero)
- ✅ Risk amount ≤ max risk per trade (from policy)
- ✅ At least 3 take-profit targets
- ✅ Target quantities sum to total quantity
- ✅ Targets are beyond entry in correct direction:
  - LONG: targets > entry
  - SHORT: targets < entry
- ✅ Stop is beyond entry in opposite direction:
  - LONG: stop < entry
  - SHORT: stop > entry

**Critical Cross-References**:
- **LLM Decision** vs **Trade Side**: Must match (LONG/SHORT)
- **Elliott Invalidation** vs **Stop Loss**: Must be identical
- **Account Equity** vs **Position Size**: Risk % must be enforced
- **Instrument qtyStep** vs **Rounded Quantity**: Must align (no partial contracts if qtyStep=1)

---

### Stage 7: Demo Execution

**Input**: `TradePlan`

**Transformations**:
1. **Execution Intent**: Create record with mode=DEMO, status=PENDING
2. **Order Placement**: 
   - Entry order (market or limit)
   - Stop loss order
   - Take-profit orders (3+ limit orders)
3. **Order Receipts**: Capture order IDs from Kraken response
4. **Fill Receipts**: Monitor fills and capture execution prices
5. **Heartbeat**: Dead-man's switch tracking (5-minute intervals)
6. **Status Tracking**: PENDING → SUBMITTED → FILLED → MONITORING

**Output**: 
- `execution_intent` record
- `order_receipt` records (entry + stop + 3+ targets)
- `fill_receipt` records (as orders fill)
- `execution_heartbeat` records (monitoring)

**Validation Checkpoints**:
- ✅ All orders submitted successfully
- ✅ Order IDs captured in receipts
- ✅ Fills match expected quantities
- ✅ Execution prices within acceptable slippage
- ✅ Heartbeat tracking active
- ✅ Kill switch not triggered

**Critical Cross-References**:
- **Trade Plan** vs **Submitted Orders**: Exact match on prices/quantities
- **Order Receipts** vs **Fill Receipts**: All orders eventually filled/cancelled
- **Execution Heartbeat** vs **Open Positions**: Monitoring continues while positions open

---

## 3. Complete Validation Matrix

### Cross-Stage Validation Points

| Stage Transition | Validation Check | Expected Behavior |
|------------------|------------------|-------------------|
| **Pine → API** | Alert direction matches intent | LONG alert for bullish setup, SHORT for bearish |
| **API → Indicators** | Symbol valid on exchange | Reject if instrument not found |
| **Indicators → Elliott** | Direction consistency | Both can analyze same candles independently |
| **Elliott → LLM** | Candidate invalidation alignment | LLM selects candidate matching indicator direction |
| **LLM → Trade Plan** | Decision type matches trade side | ALLOWLONG* → LONG trade, ALLOWSHORT* → SHORT trade |
| **Trade Plan → Execution** | All prices/quantities preserved | No transformation loss during order submission |

### Pine Script → Indicator Validation

**Question**: Does Pine Script alert direction always match indicator computation?

**Analysis**:
- Pine Script sends `alert_direction` hint (optional)
- Indicator engine computes independent direction from OHLCV data
- **Potential mismatches**:
  - Pine Script alert fires on specific trigger (e.g., RSI crossover on 5m)
  - Indicator engine evaluates multiple timeframes (~seconds later)
  - Market may have moved between Pine trigger and API computation
  - **This is VALID behavior** - indicator snapshot is "truth" at processing time

**Validation Plan**:
- Capture fixtures where alert_direction ≠ indicatorSnapshot.direction
- Document expected behavior (indicator direction wins)
- LLM should use indicatorSnapshot.direction, not alert hint

### Indicator → Elliott Direction Alignment

**Question**: Should Elliott candidates match indicator direction?

**Analysis**:
- Indicators show current momentum (bullish/bearish)
- Elliott shows wave structure (can have bearish W3 during uptrend)
- **Mismatches are VALID**:
  - LONG indicator + bearish W3 candidate = counter-trend trade opportunity
  - SHORT indicator + bullish W5END = reversal trade opportunity
- LLM decides if mismatch is acceptable or REJECT

**Validation Plan**:
- Test scenario: LONG indicator with ONLY bearish Elliott candidates → expect REJECT
- Test scenario: SHORT indicator with ONLY bullish Elliott candidates → expect REJECT
- Test scenario: Mixed candidates (both directions) → expect LLM to select matching direction

### LLM Decision Consistency

**Question**: Does LLM reliably match decision type with candidate invalidation?

**Current Issues** (discovered in M9.5):
- LLM struggles to find `indicatorSnapshot.direction` in JSON
- Prompt field name mismatch (Direction vs direction, Snapshot vs indicatorSnapshot)
- LLM sometimes ignores direction requirement and picks wrong candidate

**Validation Plan**:
- Test all 4 decision types (ALLOWLONGW3, ALLOWLONGW5END, ALLOWSHORTW3, ALLOWSHORTW5END)
- Verify selected candidate has matching invalidation price:
  - ALLOWLONG* → candidate.longInvalidationPrice != null
  - ALLOWSHORT* → candidate.shortInvalidationPrice != null
- Test REJECT scenarios:
  - LONG indicator + no candidates with longInvalidationPrice
  - SHORT indicator + no candidates with shortInvalidationPrice
  - No candidates at all (insufficient pivots)
  - All candidates have ERROR violations

---

## 4. Test Scenario Matrix

### Pine Script Alert Combinations

Based on production Pine Script (assumed to send these combinations):

| Risk Level | Direction | Alert Sent | Expected Indicator | Expected Elliott |
|------------|-----------|------------|-------------------|------------------|
| HIGH | LONG | `alert_direction: LONG` | direction: LONG, score: 80+ | Candidates with longInvalidation |
| HIGH | SHORT | `alert_direction: SHORT` | direction: SHORT, score: 80+ | Candidates with shortInvalidation |
| MEDIUM | LONG | `alert_direction: LONG` | direction: LONG, score: 50-79 | Mixed candidates possible |
| MEDIUM | SHORT | `alert_direction: SHORT` | direction: SHORT, score: 50-79 | Mixed candidates possible |
| LOW | LONG | `alert_direction: LONG` | direction: LONG, score: <50 | Any candidates or none |
| LOW | SHORT | `alert_direction: SHORT` | direction: SHORT, score: <50 | Any candidates or none |

### Fixture Requirements (M9.5)

For EACH scenario above, create fixtures for:

1. **ACCEPT: Perfect Alignment**
   - Alert direction = Indicator direction = Elliott candidates available
   - LLM decision: ALLOW{direction}{pattern}
   - Trade plan built successfully
   - Execution attempted

2. **REJECT: No Matching Candidates**
   - Alert direction = Indicator direction
   - Elliott candidates exist BUT no matching invalidation prices
   - LLM decision: REJECT with notes "No candidates match requested direction"

3. **REJECT: Direction Mismatch**
   - Alert direction ≠ Indicator direction (market moved)
   - LLM decision: REJECT with notes "Direction inconsistency"

4. **REJECT: Insufficient Pivots**
   - Alert and indicators valid
   - Elliott engine returns empty candidate list (not enough pivots)
   - LLM decision: REJECT with notes "No Elliott candidates available"

5. **REJECT: All Candidates Have Errors**
   - Elliott candidates exist
   - All have ERROR violations
   - LLM decision: REJECT with notes "No valid candidates (all have errors)"

6. **UNCERTAIN: Invalid Input**
   - Indicator snapshot missing required fields
   - Elliott candidates malformed
   - LLM decision: UNCERTAIN (fail-closed)

**Total Fixtures Needed**: 6 alert scenarios × 6 fixture types = **36 fixtures minimum**

---

## 5. Dataflow Correctness Validation Plan

### Phase 1: Unit Testing (Per Stage)

**Completed**:
- ✅ Indicator engine determinism (fixed fixtures)
- ✅ Elliott engine determinism (fixed fixtures)
- ✅ Risk policy enforcement (position sizing)

**TODO**:
- ⏳ LLM schema validation (all decision types accepted)
- ⏳ Trade plan builder edge cases (zero quantity, insufficient targets)

### Phase 2: Integration Testing (Stage Boundaries)

**TODO**:
- ⏳ API → Indicator: Symbol validation, alert normalization
- ⏳ Indicator → Elliott: Data handoff, timeframe consistency
- ⏳ Elliott → LLM: Candidate selection, invalidation matching
- ⏳ LLM → Trade Plan: Decision type → side mapping
- ⏳ Trade Plan → Execution: Order submission, receipt capture

### Phase 3: End-to-End Testing (M9.5)

**Approach**:
1. Use `simulate-alert-at-time.sh` to inject alerts at specific candle indices
2. Capture full pipeline execution with `capture-llm-decision.sh`
3. Validate each stage output:
   - Indicators: Check direction, score, confirmations
   - Elliott: Check candidate count, invalidations, patterns
   - LLM: Check decision matches candidates
   - Trade Plan: Check side, stop, targets
4. Build test matrix covering all 36 scenarios
5. Automate fixture replay and validation

**Test Data Requirements**:
- ✅ Historical OHLCV fixture: `btcusd_p_m1_varied.json` (7200 candles)
- ⏳ Identify candles for each scenario:
  - Strong uptrends (for LONG tests)
  - Strong downtrends (for SHORT tests)
  - Ranging markets (for REJECT tests)
  - Low pivot count periods (for insufficient Elliott tests)

### Phase 4: Regression Testing

**Once fixture library complete**:
- Replay all 36+ fixtures on each code change
- Validate outputs match expected decisions
- Flag any behavioral changes for review
- Use fixtures as acceptance criteria for LLM prompt changes

---

## 6. Known Issues & Investigation Items

### Issue 1: LLM Cannot Find Direction Field

**Status**: ACTIVE INVESTIGATION (M9.5)  
**Symptoms**: LLM says "Snapshot.Direction not given" or picks wrong direction  
**Root Cause**: Prompt field name mismatch (Snapshot.Direction vs indicatorSnapshot.direction)  
**Resolution**: Update prompt to use exact JSON path `indicatorSnapshot.direction`  
**Verification**: Test with SHORT alert, expect ALLOWSHORTW3 decision

### Issue 2: Missing LLM Decision Types

**Status**: RESOLVED  
**Symptoms**: LLM could only accept LONG W3 or SHORT W5END  
**Root Cause**: Schema only had 2 of 4 valid decision types  
**Resolution**: Added ALLOWLONGW5END and ALLOWSHORTW3 to schema  
**Verification**: Captured fixtures showing plan_failed (proves LLM accepted, later stage failed)

### Issue 3: Position Sizing Edge Cases

**Status**: UNDER INVESTIGATION  
**Symptoms**: "Quantity rounded to zero" or "Unable to allocate take-profit targets"  
**Root Cause**: Unknown - happens even with $500k equity  
**Investigation**: Check if extreme stop distances or unusual Elliott invalidations trigger edge cases  
**Priority**: MEDIUM (doesn't block LLM validation)

### Issue 4: qtyStep Proportionality

**Status**: RESOLVED  
**Symptoms**: Different risk profiles across equity levels  
**Root Cause**: Same qtyStep (0.001) used for all scenarios  
**Resolution**: Formula: qtyStep = (equity / 500000) × 1  
**Verification**: All scenarios now achieve ~0.98% actual risk

### Issue 5: LLM Reasoning Not Persisted

**Status**: IDENTIFIED, NOT IMPLEMENTED  
**Symptoms**: Cannot debug LLM decisions without console access  
**Impact**: Hard to validate LLM logic, capture process incomplete  
**Resolution**: Add `llm_notes` column to alert_processing or separate llm_decisions table  
**Priority**: HIGH (needed for M9.5 validation)

---

## 7. Next Steps (M9.5 & M9.6 Execution Plan)

### Immediate Actions (Session 1 - Current)

1. ✅ Update backlog with M9.5 and M9.6 stories
2. ✅ Create this comprehensive dataflow analysis document
3. ⏳ Fix LLM prompt field name issue
4. ⏳ Test SHORT alert with corrected prompt
5. ⏳ Capture first ACCEPT fixture (if successful)

### Short-Term Actions (Session 2-3)

6. ⏳ Add LLM reasoning persistence to database
7. ⏳ Document Pine Script alert combinations (requires TradingView access)
8. ⏳ Identify 36 candle indices in fixture data for test scenarios
9. ⏳ Build automated test harness for fixture replay
10. ⏳ Create first wave of fixtures (12 scenarios: 2 directions × 6 cases)

### Medium-Term Actions (Next Week)

11. ⏳ Complete 36-fixture test matrix
12. ⏳ Implement fixture-based integration tests (M9.3)
13. ⏳ Add regression test suite to CI
14. ⏳ Document validation results and edge cases
15. ⏳ Mark M9.5 and M9.6 as complete

### Long-Term Actions (Future)

16. ⏳ Connect to live TradingView alerts (requires production Pine Script)
17. ⏳ Monitor for 2-3 days to capture real ACCEPT cases
18. ⏳ Expand fixture library with production scenarios
19. ⏳ Build ML analysis of LLM decision patterns
20. ⏳ Optimize Elliott engine based on learned patterns

---

## 8. Success Criteria

### M9.5: Pipeline Integration Validation

**Done When**:
- ✅ All 36 test scenarios documented
- ✅ Pine Script → Indicator → Elliott → LLM dataflow mapped
- ✅ Cross-reference validation points identified
- ✅ Known issues documented with resolution status
- ✅ At least 12 fixtures captured (2 directions × 6 cases)

### M9.6: Dataflow Analysis

**Done When**:
- ✅ Complete end-to-end dataflow documented (this document)
- ✅ Every transformation mapped with validation checkpoints
- ✅ Cross-stage validation matrix complete
- ✅ Test harness can replay fixtures and validate outputs
- ✅ Behavioral acceptance criteria defined for each stage

---

## Appendix A: Data Schemas Reference

### AlertEvent (Input)
```json
{
  "ticker": "string",          // BTC/USD:BTCUSD.P format
  "exchange": "string",        // KRAKEN
  "interval": "string",        // Timeframe (1, 5, 15, etc.)
  "close": "decimal",          // Optional price
  "volume": "decimal",         // Optional volume
  "alert_direction": "string"  // Optional: LONG/SHORT
}
```

### IndicatorSnapshot (Stage 3 Output)
```json
{
  "symbol": "string",
  "direction": "LONG|SHORT",
  "score": 0-100,
  "confirmations": 0-N,
  "risk": "HIGH|MEDIUM|LOW",
  "anchorTimeframe": "string",
  "trendTimeframe": "string",
  "timeframes": {
    "M5": { "rsi": {}, "stochRsi": {}, "macd": {}, "volume": {} },
    ...
  }
}
```

### ElliottCandidates (Stage 4 Output)
```json
{
  "candidates": [
    {
      "candidateId": "string",
      "pattern": "W3|W5END",
      "score": 0-100,
      "isUptrend": boolean,
      "longInvalidationPrice": decimal?,
      "shortInvalidationPrice": decimal?,
      "violations": ["string"],
      "pivots": [...]
    }
  ]
}
```

### LlmDecision (Stage 5 Output)
```json
{
  "decision": "ALLOWLONGW3|ALLOWLONGW5END|ALLOWSHORTW3|ALLOWSHORTW5END|REJECT|UNCERTAIN",
  "candidateId": "string",
  "stopLossAnchor": "WAVEINVALIDATION",
  "notes": "string"
}
```

### TradePlan (Stage 6 Output)
```json
{
  "planId": "uuid",
  "side": "LONG|SHORT",
  "symbol": "string",
  "entryPrice": decimal,
  "stopLossPrice": decimal,
  "quantity": decimal,
  "targets": [
    { "price": decimal, "quantity": decimal }
  ],
  "riskAmount": decimal,
  "llmDecision": "string"
}
```

---

## Appendix B: Configuration Reference

### Key Config Parameters

**Indicator Engine**:
- `Indicator:LookbackDays`: Default 1 day
- `Indicator:LookbackDaysByTimeframe`: Per-timeframe overrides
- `Indicator:LookbackBars`: Fallback bar count
- `Indicator:Timeframes`: [5m, 15m, 30m, 1h, 2h]

**Elliott Engine**:
- `Elliott:LookbackDays`: Default 1 day (1440 M1 bars)
- `Elliott:BaseTimeframe`: M1
- `Elliott:ZigZagDeviation`: 3% (fixture mode)
- `Elliott:MaxCandidates`: 10

**Risk Policy**:
- `RiskPercentagePerTrade`: 1.0% default
- `MaxRiskPercentagePerTrade`: 3.0%
- `AccountEquity`: $500,000 (original), $100-$10k (fractional scenarios)

**Instruments**:
- `qtyStep`: 1 (original), 0.0002-0.02 (fractional scenarios)
- `Formula`: qtyStep = (equity / 500000) × 1

---

**Document Version**: 1.0  
**Last Updated**: January 9, 2026  
**Author**: AI-Assisted Trading System Team  
**Related Documents**: 
- `docs/architecture/alert-dataflow-overview.md` (high-level overview)
- `docs/milestones/m9-backtesting-fixtures/m9-scenario-framework.md` (multi-equity testing)
- `docs/backlog/backlog.md` (M9 stories)
