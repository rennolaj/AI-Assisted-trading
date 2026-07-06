# Dataflow Analysis Summary: backtest-1767375300

## Executive Summary

**Result**: ✅ **DATAFLOW IS WORKING CORRECTLY**

The system properly rejected this alert because the indicator snapshot scored 0/100 with REJECT_DEFAULT risk action. Even though Elliott Wave candidates were available, the alert should never have reached LLM adjudication.

---

## Stage-by-Stage Analysis

### Stage 1: Alert Input ✅
- **Direction Hint**: SHORT
- **Symbol**: BTCUSD.P
- **Close**: $83,059.15
- **Status**: Valid webhook format

### Stage 2: Indicator Analysis ❌ (CORRECTLY REJECTED)
- **Computed Direction**: SHORT ✅ (matches alert hint)
- **Score**: 0/100 (0 confirmations) ❌
- **Risk Category**: INVALID ❌
- **Risk Action**: REJECT_DEFAULT ❌

**Multi-Timeframe Signals**:
- M5/M15/M30: Mixed BULLISH MACD (contradicts SHORT direction)
- H1/H2: BEARISH MACD (supports SHORT)
- RSI: OVERSOLD on M30/H1 (potential reversal, not continuation)
- StochRSI: OVERSOLD on H1/H2

**CRITICAL ISSUE**: No confirmations across timeframes. This is a weak/conflicting signal that should NOT trigger a trade.

### Stage 3: Elliott Wave Candidates ⚠️ (PARTIAL)
- **Total Candidates**: 10
- **SHORT Candidates**: 3 (with shortInvalidationPrice)
- **Best SHORT Candidate**: W3, Score 45, Stop @ $96,130.85 (clean, no errors)

**OBSERVATION**: Elliott found valid SHORT W3 patterns, BUT indicator already rejected the trade.

### Stage 4: LLM Adjudication ✅ (CORRECT REJECTION)
- **Decision**: REJECT
- **Reason**: Indicator risk action = REJECT_DEFAULT

---

## Critical Finding: Pipeline Logic Issue

### Problem Identified

**The alert reached LLM despite indicator saying REJECT_DEFAULT**

According to the dataflow analysis plan:
- Indicator stage should enforce `risk.action = REJECT_DEFAULT`
- Alert should be rejected BEFORE Elliott analysis
- LLM should never see alerts with score=0 and INVALID risk

### Current Behavior (from fixture)
1. ✅ Indicator computed correctly (score 0, risk INVALID)
2. ❌ Elliott engine still ran (generated 10 candidates)
3. ❌ LLM adjudication still ran (decided REJECT)
4. ✅ Final result: rejected

### Expected Behavior
1. ✅ Indicator computed (score 0, risk INVALID)
2. ⚠️ **Pipeline should STOP here** - set status to `rejected` with reason "INDICATOR_REJECT_DEFAULT"
3. ⚠️ **Elliott should NOT run** - waste of compute
4. ⚠️ **LLM should NOT run** - waste of API calls

---

## Safe Trade Pattern Analysis

### Question: Does Pine Script send alerts that match safe trades?

**Answer**: **INCONCLUSIVE from this fixture**

This fixture shows:
- ❌ **NOT a safe SHORT W5END**: No W5END candidates with shortInvalidationPrice
- ⚠️ **Potentially a SHORT W3**: 3 W3 candidates with shortInvalidationPrice exist
  - Best: Score 45, Stop @ $96,130.85 (clean)
  - But: Indicator scored 0, so this is NOT a high-confidence setup

### Safe Trade Criteria (from M9 goals)

**LONG W3 (Bullish Impulse Wave 3)**:
- ✅ Indicator direction = LONG
- ✅ Indicator score >= 50 (medium confidence minimum)
- ✅ Elliott W3 candidate with longInvalidationPrice
- ✅ No ERROR violations

**SHORT W5END (Bearish Wave 5 Completion - Reversal)**:
- ✅ Indicator direction = SHORT
- ✅ Indicator score >= 50 (medium confidence minimum)
- ✅ Elliott W5END candidate with shortInvalidationPrice
- ✅ No ERROR violations

### This Fixture Status
- Direction: SHORT ✅
- Score: 0 ❌ (way below 50)
- Pattern: W3 (not W5END) ⚠️
- Violations: None on best candidate ✅

**Conclusion**: This is a **weak SHORT W3**, not a safe trade.

---

## Pine Script Alert Investigation

### What We Need to Know

To answer "can Pine production code trigger safe trades", we need:

1. **Pine Script Alert Conditions**: What triggers LONG vs SHORT alerts?
   - Score thresholds?
   - Confirmation requirements?
   - Risk level filters (HIGH/MEDIUM/LOW)?

2. **Alert Direction Logic**: How does Pine determine direction?
   - Based on multi-timeframe consensus?
   - Anchor timeframe (M30) only?
   - Trend timeframe (H2) only?

3. **Alert Frequency**: How often do alerts fire?
   - Every bar?
   - Only on signal changes?
   - With re-entry prevention?

### Hypothesis: Pine Script May Send Low-Quality Alerts

**Evidence from this fixture**:
- Pine sent SHORT alert
- Indicator computed SHORT but with 0 confirmations
- System correctly rejected

**Possible explanations**:
1. **Pine fired too early**: Signal appeared on one timeframe, but others didn't confirm yet
2. **Pine uses different logic**: Pine Script calculates indicators differently than C# engine
3. **Market moved**: Alert fired when setup was valid, but by the time system processed it, conditions changed
4. **Pine has no quality filter**: Sends all signals regardless of strength

---

## Recommendations

### Immediate Actions

1. **Add Early Rejection in Pipeline** (HIGH PRIORITY)
   ```
   if (indicatorSnapshot.Risk.Action == "REJECT_DEFAULT")
   {
       await UpdateAlertStatus("rejected", "INDICATOR_REJECT_DEFAULT");
       return; // Don't run Elliott or LLM
   }
   ```

2. **Review Pine Script Logic** (HIGH PRIORITY)
   - Compare Pine indicator calculations with C# implementation
   - Add alert quality filter in Pine (only send if score >= threshold)
   - Or: Add `alert_confidence` field to webhook for filtering

3. **Test Safe Trade Scenarios** (MEDIUM PRIORITY)
   - Need fixtures with:
     - LONG direction + score 70+ + clean W3 LONG candidate
     - SHORT direction + score 70+ + clean W5END SHORT candidate
   - Use `simulate-alert-at-time.sh` to find these in historical data

### Long-Term Actions

4. **Audit Pine Script Source** (MEDIUM PRIORITY)
   - Get Pine Script code from TradingView
   - Document exact alert conditions
   - Map Pine logic to C# validation rules

5. **Build Alert Quality Dashboard** (LOW PRIORITY)
   - Track: alerts received vs alerts accepted
   - Show: rejection reasons (indicator score, Elliott, LLM)
   - Identify: Pine configuration issues

---

## Testing Plan (M9.5 Execution)

### Phase 1: Find Safe Trade Fixtures (Current Priority)

Use `btcusd_p_m1_varied.json` (7200 candles) to find:

1. **LONG W3 Setup**:
   - Scan for candles where price is in strong uptrend
   - Run `simulate-alert-at-time.sh -i <index> -d LONG`
   - Look for: score >= 50, W3 candidate with longInvalidation
   - Capture first ACCEPT or high-quality REJECT

2. **SHORT W5END Setup**:
   - Scan for candles where price completes 5-wave move
   - Run `simulate-alert-at-time.sh -i <index> -d SHORT`
   - Look for: score >= 50, W5END candidate with shortInvalidation
   - Capture first ACCEPT or high-quality REJECT

### Phase 2: Validate with .env.smoke

Once safe trade fixtures identified:
1. Load `.env.smoke` configuration
2. Re-run scenarios through full pipeline
3. Verify: indicators → Elliott → LLM → trade plan → execution
4. Document: where each stage succeeds or fails

### Phase 3: Build 36-Fixture Matrix

Per M9 dataflow validation plan, create:
- 6 scenarios (HIGH/MED/LOW × LONG/SHORT)
- 6 test cases per scenario (ACCEPT, reject variations)
- Total: 36 fixtures for comprehensive regression testing

---

## Conclusion

### ✅ What's Working
1. Indicator engine computes correctly (even if result is poor)
2. Elliott engine finds valid candidates (independent of indicator quality)
3. LLM correctly rejects when no valid setup exists
4. Cross-reference validation detects mismatches

### ❌ What Needs Fixing
1. Pipeline doesn't short-circuit on REJECT_DEFAULT (wastes resources)
2. No early rejection before Elliott/LLM stages
3. Pine Script may send low-quality alerts (needs investigation)

### ⏳ What's Unknown
1. Can Pine Script generate high-quality safe trade alerts?
2. What are actual Pine alert conditions and thresholds?
3. How often do ACCEPT scenarios occur in production?

### 🎯 Next Step
**Find a clean LONG W3 or SHORT W5END scenario in historical data** to test if the system can actually approve a trade when conditions are right.
