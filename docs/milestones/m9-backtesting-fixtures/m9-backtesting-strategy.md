# M9: Historical Backtesting Strategy

## Date: 2026-01-08

## Objective
Generate LLM test fixtures using **real historical market data** from past months to capture authentic Elliott Wave patterns and indicator conditions, enabling comprehensive testing without waiting for current market structures.

---

## Problem Statement

### Current Situation
- **ForceAllow cannot generate synthetic fixtures** - requires valid Elliott candidates (5+ pivots)
- **Real-time alerts take too long** - waiting 2-3 days for market conditions is inefficient
- **No baseline for ALLOW scenarios** - 100% of test cases have been REJECTED
- **Cannot validate prompt engineering** - no examples of what LLM should accept

### Solution: Time-Travel Testing
Use Kraken Futures **Charts API** to fetch historical OHLC data from 5-6 months ago, replay it through the system in fixture mode, and capture complete decision contexts.

---

## Available Infrastructure

### 1. Fixture Market Data Provider ✅
**Location**: `src/Mvp.Trading.Integrations.Kraken/FixtureMarketDataProvider.cs`

**Capabilities**:
- Loads JSON fixtures from `tests/fixtures/kraken-futures/`
- Supports multiple timeframes (M1, M5, M15, M30, H1, H2, H4, H12, D1)
- Auto-aggregates lower timeframes to higher (e.g., M1 → M5)
- Extends short fixtures to meet lookback requirements
- Falls back to real Kraken API for instruments/tickers

**Fixture Format**:
```json
{
  "symbol": "PF_XBTUSD",
  "intervalMinutes": 1,
  "candles": [
    [timestamp, open, high, low, close, vwap, volume, count],
    ...
  ]
}
```

**Configuration**:
```bash
MARKETDATA_MODE=fixtures
MARKETDATA_FIXTURE_PATH=tests/fixtures/kraken-futures
MARKETDATA_EXTEND_FIXTURES=true
```

### 2. Charts API Integration ✅
**Location**: `src/Mvp.Trading.Integrations.Kraken/KrakenFuturesMarketDataProvider.cs`

**Capabilities**:
- Fetches OHLC candles from Kraken Charts API
- Supports pagination (`to` parameter for walking back in time)
- Batch fetching (up to 500 candles per request, configurable max batches)
- Handles multiple resolutions: `1m`, `5m`, `15m`, `30m`, `1h`, `4h`, `12h`, `1d`, `1w`
- Production endpoint: `https://futures.kraken.com/api/charts/v1/{tick_type}/{symbol}/{resolution}`

**Usage**:
```bash
# Fetch 5000 candles (10 batches × 500) of BTC 1-minute data
GET /api/charts/v1/trade/PF_XBTUSD/1m?count=500
GET /api/charts/v1/trade/PF_XBTUSD/1m?count=500&to=1767657540
# ... repeat with decreasing `to` timestamps
```

### 3. Existing Capture Script ✅
**Location**: `scripts/fixtures/capture-futures-history.sh`

**Current Limitation**: Uses `/history` endpoint (trade fills, ~100 max) - insufficient for backtesting

**Needs Enhancement**: Replace with Charts API to fetch thousands of historical candles

---

## Implementation Plan

### Phase 1: Historical Data Capture Tool (NEW)

**Create**: `scripts/fixtures/fetch-historical-candles.sh`

**Purpose**: Fetch large historical datasets (weeks/months) from Kraken Charts API

**Features**:
- Accept date range (e.g., "August 2025")
- Fetch multiple timeframes in parallel (M1, M5, M15, M30, H1)
- Handle pagination automatically
- Save in FixtureMarketDataProvider format
- Validate data integrity (gaps, outliers)

**Example Usage**:
```bash
# Fetch BTC data from August 2025 (5 months ago)
./scripts/fixtures/fetch-historical-candles.sh \
  --symbol PF_XBTUSD \
  --from 2025-08-01 \
  --to 2025-08-31 \
  --resolutions "1m,5m,15m,30m,1h" \
  --output tests/fixtures/historical/btc_aug2025

# Result: 5 fixture files with ~44,000 1m candles (31 days × 1440 minutes)
```

**Implementation Steps**:
1. Query Charts API with `count=500` and `to` pagination
2. Walk backwards from end date to start date
3. Aggregate candles into fixture format
4. Validate timestamp continuity
5. Save per-timeframe JSON files

---

### Phase 2: Backtesting Webhook Simulator (NEW)

**Create**: `scripts/fixtures/simulate-alert-at-time.sh`

**Purpose**: Generate synthetic TradingView alert at specific historical timestamp

**Process**:
1. **Input**: Fixture file + target timestamp
2. **Extract Context**:
   - Get candle at target time
   - Build indicator context (RSI, MACD, Stoch, Volume)
   - Analyze Elliott patterns in lookback window
3. **Generate Webhook**: Create TradingView-like payload
4. **Submit**: POST to API with synthetic idempotency key

**Example Usage**:
```bash
# Simulate alert on August 15, 2025 at 14:30 UTC
./scripts/fixtures/simulate-alert-at-time.sh \
  --fixture tests/fixtures/historical/btc_aug2025_m15.json \
  --timestamp "2025-08-15T14:30:00Z" \
  --direction LONG \
  --reason "Strong 5-wave impulse completion"

# Output: Webhook posted, alert processing started
```

**Webhook Payload**:
```json
{
  "idempotencyKey": "backtest-aug2025-1723732200",
  "ticker": "BTC/USD",
  "exchange": "KRAKEN",
  "interval": "15m",
  "close": 45123.50,
  "volume": 234.5,
  "directionHint": "LONG",
  "symbolHint": "PF_XBTUSD",
  "reason": "Historical backtest: Strong 5-wave impulse completion"
}
```

---

### Phase 3: Automated Scenario Discovery (NEW)

**Create**: `scripts/fixtures/scan-historical-patterns.sh`

**Purpose**: Automatically identify interesting Elliott patterns in historical data

**Algorithm**:
1. Load fixture with full month of data
2. Slide window every N candles (e.g., every 100 candles)
3. For each window:
   - Run Elliott engine
   - Check for valid candidates (5+ pivots)
   - Score patterns (impulse vs corrective, confidence)
   - Identify potential entry points
4. Save top 20 scenarios with metadata

**Example Usage**:
```bash
# Scan August 2025 for strong patterns
./scripts/fixtures/scan-historical-patterns.sh \
  --fixture tests/fixtures/historical/btc_aug2025_m15.json \
  --min-pivots 5 \
  --min-score 0.6 \
  --patterns "impulse,corrective" \
  --limit 20

# Output: scenarios.json with timestamps of best patterns
```

**Output Format**:
```json
{
  "scannedPeriod": "2025-08-01 to 2025-08-31",
  "totalWindows": 2976,
  "candidateScenarios": [
    {
      "timestamp": "2025-08-15T14:30:00Z",
      "pattern": "impulse",
      "waveLabel": "WAVE_5",
      "score": 0.85,
      "pivotCount": 7,
      "confidence": 0.78,
      "rsiM15": 72.3,
      "macdHistogram": 0.045,
      "volumeSpike": 2.3,
      "reason": "Clear 5-wave impulse with wave 5 completion"
    },
    ...
  ]
}
```

---

### Phase 4: Batch Fixture Generation (NEW)

**Create**: `scripts/fixtures/generate-fixtures-from-scenarios.sh`

**Purpose**: Process all discovered scenarios through the full pipeline

**Process**:
1. Load scenarios from scan output
2. For each scenario:
   - Configure system to use fixture mode
   - Set "current time" to scenario timestamp
   - Submit simulated webhook
   - Wait for processing (Elliott + Indicators + LLM)
   - Capture full decision with `capture-llm-decision.sh`
3. Categorize results (ALLOW vs REJECT)
4. Generate summary report

**Example Usage**:
```bash
# Generate fixtures for top 20 scenarios
./scripts/fixtures/generate-fixtures-from-scenarios.sh \
  --scenarios scenarios.json \
  --fixture-base tests/fixtures/historical/btc_aug2025 \
  --output-dir tests/fixtures/llm-decisions \
  --max-parallel 5

# Result: 20 fixtures (hopefully mix of ALLOW/REJECT)
```

**Expected Outcomes**:
- 10-15 REJECT cases (weak patterns, rule violations)
- 5-10 ALLOW cases (strong patterns, valid setups)
- Full context for each: webhook, indicators, Elliott, LLM decision

---

## Technical Requirements

### 1. Fetch Historical Candles Script

**API Details**:
```
GET https://futures.kraken.com/api/charts/v1/trade/PF_XBTUSD/1m?count=500&to=<unix_timestamp>

Response:
{
  "candles": [
    [timestamp, open, high, low, close, volume],
    ...
  ],
  "more_candles": true
}
```

**Pagination Logic**:
```python
def fetch_historical_range(symbol, resolution, from_time, to_time):
    candles = []
    current_to = to_time
    
    while True:
        url = f"{BASE_URL}/trade/{symbol}/{resolution}?count=500&to={current_to}"
        response = requests.get(url).json()
        batch = response.get("candles", [])
        
        if not batch:
            break
            
        candles.extend(batch)
        earliest = batch[0][0]  # timestamp of earliest candle
        
        if earliest <= from_time:
            break
            
        current_to = earliest - 1
        
        if not response.get("more_candles"):
            break
    
    # Filter to exact range
    candles = [c for c in candles if from_time <= c[0] <= to_time]
    candles.sort(key=lambda x: x[0])
    return candles
```

### 2. Time-Travel Configuration

**Approach 1: Mock System Clock** (Complex)
- Requires intercepting `DateTime.UtcNow` calls
- High risk of breaking other components
- **NOT RECOMMENDED**

**Approach 2: Fixture Mode + Manual Timestamp** (Simple)
- System runs in fixture mode (MARKETDATA_MODE=fixtures)
- Worker reads from fixture files (no real API calls)
- Webhook includes explicit timestamp context
- Elliott engine uses webhook timestamp as "evaluation time"
- **RECOMMENDED**

**Configuration**:
```bash
# Enable fixture mode
MARKETDATA_MODE=fixtures
MARKETDATA_FIXTURE_PATH=tests/fixtures/historical/btc_aug2025

# Worker processes alerts normally
# Indicators read from fixtures
# Elliott analyzes fixture data
# LLM adjudicates as usual
```

### 3. Indicator Snapshot Requirements

**Lookback Needs** (from configuration):
- M5: 1 day = 288 candles
- M15: 1 day = 96 candles  
- M30: 1 day = 48 candles
- H1: 2 days = 48 candles
- H2: 3 days = 36 candles

**Fixture Size Requirements**:
- **Minimum**: 3 days × 1440 min = 4,320 M1 candles
- **Recommended**: 7 days × 1440 min = 10,080 M1 candles (1 week)
- **Ideal**: 31 days × 1440 min = 44,640 M1 candles (1 month)

**Charts API Fetch**:
- 44,640 candles ÷ 500 per request = ~90 API calls
- With 1s delay between calls: ~90 seconds total
- **Feasible for batch processing**

### 4. Elliott Wave Pattern Requirements

**For Valid Candidates**:
- Minimum 5 pivots (typically needs 300-500 M1 candles)
- ZigZag parameters: Depth=1, Deviation=5%
- Lookback: 20-200 bars (configurable)

**Historical Data Advantages**:
- Can select periods with known strong patterns
- Can scan for specific wave structures
- Can capture both bullish and bearish scenarios
- Can test different market regimes (trending, ranging, volatile)

---

## Validation Strategy

### Pre-Flight Checks
1. **Fixture Integrity**:
   ```bash
   # Verify fixture has sufficient data
   jq '.candles | length' fixture.json  # Should be 10,000+
   
   # Check for gaps in timestamps
   jq -r '.candles[][0]' fixture.json | sort -n | awk 'NR>1{print $1-prev}{prev=$1}' | uniq
   ```

2. **Elliott Engine Test**:
   ```bash
   # Run existing unit test with new fixture
   dotnet test tests/Mvp.Trading.Elliott.Tests/ --filter "ElliottFuturesFixtureTests"
   ```

3. **Indicator Calculation**:
   ```bash
   # Verify indicators can calculate with fixture data
   # (requires test harness - may need new integration test)
   ```

### Post-Processing Checks
1. **Decision Distribution**:
   ```bash
   # Count ALLOW vs REJECT
   find tests/fixtures/llm-decisions -name "*.json" | xargs jq -r '.metadata.processingStatus' | sort | uniq -c
   ```

2. **Quality Metrics**:
   ```bash
   # Check Elliott candidate scores
   find tests/fixtures/llm-decisions -name "*.json" | xargs jq '.elliott_candidates.candidates[0].score'
   
   # Check indicator data integrity
   find tests/fixtures/llm-decisions -name "*.json" | xargs jq '.indicator_snapshot.multiTimeframe | to_entries[] | select(.value.dataIntegrity == false)'
   ```

---

## Risk Mitigation

### Potential Issues

1. **Charts API Rate Limits**
   - **Risk**: 90+ API calls for 1 month of data
   - **Mitigation**: Add 1-2s delay between requests, implement exponential backoff
   - **Fallback**: Split into smaller date ranges, fetch over multiple days

2. **Fixture Data Quality**
   - **Risk**: Missing candles, outliers, data gaps
   - **Mitigation**: Validate timestamps are continuous, flag anomalies
   - **Fallback**: Skip problematic periods, use different date range

3. **Unrealistic Scenarios**
   - **Risk**: Historical patterns don't reflect real trading conditions
   - **Mitigation**: Use recent history (5-6 months ago, not years)
   - **Validation**: Cross-reference with TradingView charts for period

4. **LLM Behavior Drift**
   - **Risk**: LLM trained on newer data may not match historical context
   - **Mitigation**: Focus on Elliott/indicator quality, not macro news
   - **Acceptance**: Some drift is expected, fixtures still valuable for regression testing

5. **Processing Time**
   - **Risk**: 20 scenarios × 30s processing = 10 minutes
   - **Mitigation**: Run in parallel (5 workers), complete in 2-3 minutes
   - **Optimization**: Cache indicator calculations, reuse Elliott analyses

---

## Success Criteria

### Phase 1 Complete When:
- ✅ Can fetch 1 month of historical data (44,000+ candles) in <2 minutes
- ✅ Fixtures load correctly in FixtureMarketDataProvider
- ✅ Validation shows no gaps or anomalies

### Phase 2 Complete When:
- ✅ Can simulate alert at any timestamp in fixture range
- ✅ System processes synthetic alert through full pipeline
- ✅ Indicators calculate correctly from fixture data
- ✅ Elliott engine produces candidates

### Phase 3 Complete When:
- ✅ Scanner identifies 20+ potential scenarios in 1 month
- ✅ Mix of impulse and corrective patterns detected
- ✅ Scores and confidence metrics look reasonable

### Phase 4 Complete When:
- ✅ Generated 20+ LLM decision fixtures
- ✅ At least 5 ALLOW cases captured
- ✅ At least 10 REJECT cases captured
- ✅ Full context stored (webhook, indicators, Elliott, LLM)
- ✅ Fixtures pass validation (JSON schema, data integrity)

### Overall Success When:
- ✅ Have baseline of ALLOW scenarios to validate prompt engineering
- ✅ Can reproduce test cases consistently
- ✅ Regression test suite established
- ✅ Process documented and repeatable
- ✅ Can generate new fixtures for different periods/symbols in <30 min

---

## Timeline Estimate

| Phase | Task | Estimated Time |
|-------|------|----------------|
| 1 | Build historical fetch script | 2-3 hours |
| 1 | Test with August 2025 data | 30 minutes |
| 1 | Validate fixtures | 30 minutes |
| 2 | Build webhook simulator | 1-2 hours |
| 2 | Test end-to-end flow | 1 hour |
| 3 | Build pattern scanner | 2-3 hours |
| 3 | Run scan on historical data | 30 minutes |
| 3 | Review and curate scenarios | 1 hour |
| 4 | Build batch processor | 1 hour |
| 4 | Generate fixtures | 30 minutes |
| 4 | Validate and categorize | 1 hour |
| **TOTAL** | **Full implementation** | **12-16 hours** |

**Target Completion**: 2 working days (with testing and refinement)

---

## Alternative: Shortcut Approach

If timeline is critical, start with **manual scenario selection**:

1. **Identify Good Period** (1 hour)
   - Review TradingView BTC/USD charts for August 2025
   - Find 3-5 clear Elliott patterns visually
   - Note exact timestamps

2. **Manual Fixture Creation** (2 hours)
   - Use existing `capture-futures-history.sh` for small windows
   - Or manually query Charts API for specific days
   - Create 3-5 fixture files (1 day each)

3. **Manual Simulation** (1 hour)
   - Configure system to fixture mode
   - Manually trigger alerts via curl
   - Capture decisions with existing script

4. **Result**: 3-5 fixtures in 4 hours vs 20+ fixtures in 16 hours

**Recommendation**: Start with shortcut to validate approach, then build automation if successful.

---

## Next Steps

1. **Decide on approach**: Full automation vs manual shortcut
2. **Select target period**: August 2025? September 2025? (check TradingView for good patterns)
3. **Build Phase 1 script**: Fetch historical data from Charts API
4. **Test fixture loading**: Verify FixtureMarketDataProvider works
5. **Build Phase 2 script**: Simulate alert at timestamp
6. **Generate first fixture**: Validate full pipeline
7. **Scale up**: Either build scanner or manually select more scenarios
8. **Review results**: Categorize ALLOW/REJECT, assess quality

**Shall we start with Phase 1 - building the historical data fetch script?**
