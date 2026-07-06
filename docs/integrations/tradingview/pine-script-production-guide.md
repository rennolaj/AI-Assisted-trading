# MVP Production Trading Alerts - Pine Script Documentation

## Overview

This production-ready Pine Script generates trading alerts that **align with the MVP system's indicator-based entry requirements**. Unlike the smoke test script which fires alerts on trivial conditions, this script implements the actual trading logic that the MVP backend expects.

## Design Principles

### 1. **Alignment with MVP Indicator Engine**
The script replicates the same confirmation signals that the backend `IndicatorEngine.cs` evaluates:
- RSI extreme zones (oversold/overbought gates)
- Multi-timeframe RSI alignment
- Stochastic RSI reversal patterns
- MACD momentum confirmation
- Trend alignment via EMAs
- Volume spike confirmation

### 2. **Quality Over Quantity**
- Only fires alerts when **meaningful setups** exist
- Requires RSI to be in extreme zones (primary gate)
- Requires minimum number of confirmations (default: 3 out of 5)
- Optional filters for trend alignment and volume

### 3. **Production Ready**
- Alerts only on bar close (`barstate.isconfirmed`) for reliability
- Rich JSON payload with all indicator values
- Idempotency keys for deduplication
- Visual indicators on chart for validation
- Status table showing current conditions

---

## Comparison: Smoke Test vs Production Script

| Feature | Smoke Test Script | Production Script |
|---------|-------------------|-------------------|
| **Purpose** | Generate high volume of alerts for testing ingestion pipeline | Generate quality trading signals that match MVP logic |
| **Alert Frequency** | Very high (pulses, micro-moves, every crossover) | Low (only when RSI extreme + confirmations) |
| **RSI Gate** | ❌ No RSI extreme check | ✅ Requires RSI ≤30 (LONG) or ≥70 (SHORT) |
| **Confirmations** | ❌ Single indicator events | ✅ Multiple confirmations required (3/5) |
| **Stochastic RSI** | ❌ Not used | ✅ Reversal pattern detection |
| **MACD** | ✅ Simple crossover | ✅ Momentum + histogram growth |
| **Trend Filter** | ❌ Only for direction hint | ✅ Optional trend alignment requirement |
| **Volume** | ✅ Basic spike detection | ✅ Configurable spike threshold |
| **Alert Timing** | Mix of bar close + realtime | Bar close only (reliable) |
| **Use Case** | System testing, pipeline validation | **Real trading signals** |

---

## Alert Conditions

### LONG Signal Requirements
1. **RSI Gate** (Required): RSI ≤ 30 (oversold)
2. **Minimum Confirmations** (default 3 of 5):
   - **RSI Multi-TF**: RSI consistently oversold
   - **Stochastic RSI Reversal**: Was oversold, K crossed above D
   - **MACD Momentum**: MACD > Signal, histogram positive and growing
   - **Trend Alignment**: Fast EMA > Slow EMA (uptrend)
   - **Volume Spike**: Volume ≥ 1.2x average
3. **Optional Filters**:
   - Require trend alignment (default: ON)
   - Require volume confirmation (default: OFF)

### SHORT Signal Requirements
1. **RSI Gate** (Required): RSI ≥ 70 (overbought)
2. **Minimum Confirmations** (default 3 of 5):
   - **RSI Multi-TF**: RSI consistently overbought
   - **Stochastic RSI Reversal**: Was overbought, K crossed below D
   - **MACD Momentum**: MACD < Signal, histogram negative and declining
   - **Trend Alignment**: Fast EMA < Slow EMA (downtrend)
   - **Volume Spike**: Volume ≥ 1.2x average
3. **Optional Filters**:
   - Require trend alignment (default: ON)
   - Require volume confirmation (default: OFF)

---

## Configuration

### Recommended Settings

#### For 5-Minute Timeframe (Default)
```
RSI Period: 14
RSI Oversold: 30
RSI Overbought: 70

Stochastic RSI Period: 14
Stochastic %K Smoothing: 3
Stochastic %D Smoothing: 3
Stoch Oversold: 20
Stoch Overbought: 80

MACD Fast: 12
MACD Slow: 26
MACD Signal: 9

Trend EMA Fast: 21
Trend EMA Slow: 50

Volume SMA Period: 20
Volume Spike Threshold: 1.2x

Minimum Confirmations: 3
Require Trend Alignment: YES
Require Volume Confirmation: NO
```

#### For 15-Minute Timeframe (More Selective)
```
Same as above, but:
Minimum Confirmations: 4
Require Volume Confirmation: YES
```

#### For 1-Hour Timeframe (High Quality)
```
RSI Period: 14
Trend EMA Fast: 50
Trend EMA Slow: 200
Volume Spike Threshold: 1.5x
Minimum Confirmations: 4
Require Trend Alignment: YES
Require Volume Confirmation: YES
```

### Tuning for Different Strategies

**Conservative (Fewer Alerts, Higher Quality)**
- Minimum Confirmations: 4-5
- Require Trend Alignment: YES
- Require Volume Confirmation: YES
- Volume Threshold: 1.5x+

**Aggressive (More Alerts, Lower Quality)**
- Minimum Confirmations: 2-3
- Require Trend Alignment: NO
- Require Volume Confirmation: NO
- Volume Threshold: 1.2x

**Scalping (Fast Moves)**
- Use 1-5 minute timeframes
- Minimum Confirmations: 2-3
- Trend EMA Fast: 9
- Trend EMA Slow: 21

**Swing Trading (Higher Timeframes)**
- Use 1H-4H timeframes
- Minimum Confirmations: 4-5
- Trend EMA Fast: 50
- Trend EMA Slow: 200
- Volume Threshold: 2.0x

---

## TradingView Alert Setup

### 1. Load the Script
1. Open TradingView
2. Create new Pine Script
3. Copy contents of `mvp-production-alerts.pine`
4. Save and add to chart

### 2. Configure Timeframe
- Recommended: **5-minute** for active trading
- Alternative: 15m, 1H, 4H for swing trading

### 3. Create Alert
1. Click "Alert" button (alarm icon)
2. Condition: **"MVP Production Trading Alerts"** → **"Any alert() function call"**
3. Alert name: `MVP Production Signal - {{ticker}} {{interval}}`
4. Webhook URL: `https://your-domain.com/webhooks/tradingview/YOUR_SECRET`
5. Message: `{{alert_message}}` (this sends the JSON payload)
6. Options:
   - ✅ Once Per Bar Close
   - Expiration: Open-ended

### 4. Test Alert
- Wait for a legitimate setup (may take hours/days depending on market)
- Or temporarily lower `minConfirmations` to 1 for testing
- Check API logs to verify alert reception

---

## JSON Payload Structure

Each alert sends a JSON payload with full indicator context:

```json
{
  "idempotencyKey": "BTCUSD.P-5-LONG-1736208300000",
  "ticker": "BTCUSD.P",
  "exchange": "krakenfutures",
  "interval": "5",
  "close": 50000.50,
  "volume": 1234567.89,
  "directionHint": "LONG",
  "symbolHint": "BTCUSD.P",
  "reason": "MVP_PRODUCTION_SIGNAL",
  "confirmations": "RSI_MULTITF,STOCH_REVERSAL,MACD_MOMENTUM,TREND_ALIGNED,",
  "confirmationCount": 4,
  "rsi": 28.45,
  "stochK": 22.15,
  "stochD": 18.90,
  "macd": 15.50,
  "macdSignal": 12.30,
  "macdHist": 3.20,
  "emaFast": 50100.00,
  "emaSlow": 49900.00
}
```

### Payload Fields

| Field | Type | Description |
|-------|------|-------------|
| `idempotencyKey` | string | Unique key: {symbol}-{interval}-{direction}-{timestamp} |
| `ticker` | string | Trading symbol (e.g., "BTCUSD.P") |
| `exchange` | string | Exchange name (e.g., "krakenfutures") |
| `interval` | string | Timeframe (e.g., "5" for 5-minute) |
| `close` | number | Closing price at alert time |
| `volume` | number | Volume at alert bar |
| `directionHint` | string | "LONG" or "SHORT" |
| `symbolHint` | string | Symbol hint for backend lookup |
| `reason` | string | "MVP_PRODUCTION_SIGNAL" |
| `confirmations` | string | Comma-separated list of active confirmations |
| `confirmationCount` | number | Number of confirmations (0-5) |
| `rsi` | number | RSI value at alert time |
| `stochK` | number | Stochastic RSI %K value |
| `stochD` | number | Stochastic RSI %D value |
| `macd` | number | MACD line value |
| `macdSignal` | number | MACD signal line value |
| `macdHist` | number | MACD histogram value |
| `emaFast` | number | Fast EMA value |
| `emaSlow` | number | Slow EMA value |

---

## Backend Processing

When the MVP system receives this alert:

1. **Webhook Ingestion** (`/webhooks/tradingview/{secret}`)
   - Validates payload
   - Creates `AlertEvent` record
   - Enqueues to Redis

2. **Worker Processing**
   - Dequeues alert
   - Fetches OHLCV data for configured timeframes (M5, M15, H1, etc.)
   - Recomputes indicators with multi-timeframe analysis
   - Generates Elliott Wave candidates
   - Persists `IndicatorSnapshot` and `ElliottCandidates`

3. **LLM Adjudication** (MCP)
   - Reviews indicator confirmations
   - Analyzes Elliott wave patterns
   - Returns ALLOW/REJECT decision with reasoning

4. **Trade Plan Building**
   - If ALLOW: Builds risk-sized trade plan
   - Entry price with slippage cap
   - Stop-loss from Elliott invalidation level
   - 3 take-profit targets (1R/2R/3R)

5. **Execution**
   - Places entry order (limit or market)
   - Places stop-loss order
   - Places take-profit orders
   - Records receipts in database

---

## Visual Indicators

### On-Chart Display
- **Green triangle up**: LONG signal
- **Red triangle down**: SHORT signal
- **Blue line**: Fast EMA (trend)
- **Orange line**: Slow EMA (trend)
- **Green background**: RSI oversold zone
- **Red background**: RSI overbought zone

### Status Table (Top-Right)
Shows current market conditions:
- RSI value (colored if extreme)
- Stochastic K/D values
- MACD histogram (colored)
- Trend direction (UP/DOWN/NEUTRAL)
- Volume ratio (highlighted if spike)
- LONG confirmations count
- SHORT confirmations count
- Current signal status

---

## Testing Strategy

### 1. Historical Backtesting
1. Load script on TradingView
2. Review past signals visually
3. Check if signals align with actual good entry points
4. Adjust `minConfirmations` and filters as needed

### 2. Forward Testing (Paper Trading)
1. Set up alert with webhook to demo environment
2. Monitor alerts in real-time
3. Verify backend processes alerts correctly
4. Check if LLM approves quality setups
5. Review execution outcomes

### 3. Live Testing Checklist
Before production:
- [ ] Verified alert frequency is reasonable (not too many/few)
- [ ] Confirmed RSI gates are working (no alerts without extremes)
- [ ] Validated trend filter reduces counter-trend trades
- [ ] Checked volume filter improves quality
- [ ] Backend successfully processes 100% of alerts
- [ ] LLM adjudication working as expected
- [ ] Execution places orders correctly
- [ ] Stop-loss and take-profit orders placed

---

## Troubleshooting

### No Alerts Firing

**Possible Causes:**
1. **RSI not reaching extremes**: Market not volatile enough
   - Solution: Lower RSI thresholds (28/72) or wait for better market conditions
   
2. **Too many confirmations required**: Hard to get 4-5 signals aligned
   - Solution: Lower `minConfirmations` to 2-3
   
3. **Trend filter too strict**: Ranging market
   - Solution: Set `requireTrend = false`

### Too Many Alerts

**Possible Causes:**
1. **Not enough confirmations required**
   - Solution: Increase `minConfirmations` to 4-5
   
2. **Volatile/ranging market triggering RSI extremes frequently**
   - Solution: Enable trend filter, increase volume threshold
   
3. **Lower timeframe noise** (1m, 5m)
   - Solution: Use higher timeframes (15m, 1H)

### Alerts Don't Match Backend Analysis

**Expected Behavior:**
- Pine Script uses **single timeframe** indicators
- Backend uses **multi-timeframe** analysis
- Slight differences are normal and expected

**The backend will:**
- Recompute all indicators from fresh OHLCV data
- Analyze M5, M15, H1, H4 timeframes (configurable)
- Apply more sophisticated confirmation logic
- Use Elliott Wave analysis for additional validation

**Result:** Backend may REJECT alerts that look good on single timeframe if multi-timeframe analysis disagrees.

---

## Production Deployment Checklist

### Pre-Deployment
- [ ] Test script on TradingView with historical data
- [ ] Verify JSON payload format matches backend expectations
- [ ] Test webhook endpoint with sample alerts
- [ ] Confirm idempotency keys prevent duplicates
- [ ] Validate alert frequency is acceptable (not spamming)

### Configuration Review
- [ ] RSI thresholds appropriate for market (30/70 default)
- [ ] Minimum confirmations set (3-4 recommended)
- [ ] Trend filter enabled (YES for production)
- [ ] Volume threshold reasonable (1.2x-1.5x)
- [ ] Symbol and exchange names correct
- [ ] Timeframe matches strategy (5m/15m/1H)

### TradingView Alert Setup
- [ ] Alert condition: "Any alert() function call"
- [ ] Webhook URL with correct secret
- [ ] Message: `{{alert_message}}`
- [ ] Frequency: Once Per Bar Close
- [ ] Expiration: Open-ended
- [ ] Notifications: Webhook only (disable email/push for production)

### Backend Verification
- [ ] Webhook endpoint accessible
- [ ] Alerts appearing in database
- [ ] Worker processing alerts
- [ ] Indicators computed successfully
- [ ] Elliott candidates generated
- [ ] LLM adjudication working
- [ ] Execution flow tested
- [ ] Prometheus metrics recording alerts

### Monitoring Setup
- [ ] Grafana dashboard showing alert rate
- [ ] Prometheus alerts for queue depth
- [ ] Log monitoring for errors
- [ ] Database query for recent alerts
- [ ] Health check endpoints responding

---

## Maintenance

### Regular Reviews
- **Weekly**: Check alert frequency and quality
- **Monthly**: Review LLM acceptance rate vs Pine Script signals
- **Quarterly**: Backtest parameter adjustments

### Parameter Tuning
Based on performance data:
1. If too many false signals: Increase `minConfirmations`
2. If missing good trades: Lower thresholds or confirmations
3. If too many counter-trend trades: Enable trend filter
4. If alerts during low liquidity: Increase volume threshold

### Version Control
- Keep Pine Script in git repository
- Document any parameter changes
- Tag versions with dates
- Link to performance reports

---

## Support & References

### Related Documentation
- `docs/backlog/backlog.md` - MVP requirements
- `docs/milestones/m6-risk-execution/m6-overview.md` - Risk engine and execution
- `docs/architecture/alert-dataflow-overview.md` - Alert processing pipeline
- `src/Mvp.Trading.Indicators/IndicatorEngine.cs` - Backend indicator logic
- `docs/development/command-reference.md` - Testing commands

### Key Differences from Backend
| Aspect | Pine Script | Backend |
|--------|-------------|---------|
| Timeframes | Single (current chart) | Multiple (M5, M15, H1, H4, D1) |
| RSI Multi-TF | Simulated (recent bars) | Actual (separate timeframe fetches) |
| Volume | Current timeframe only | Anchor timeframe only |
| Trend | Simple EMA cross | Multi-timeframe EMA analysis |
| Purpose | **Signal generation** | **Signal validation & scoring** |

### Contact
For issues or questions:
- Check logs: `docker compose logs -f api worker`
- Review metrics: http://localhost:3000 (Grafana)
- Database queries: `docker compose exec postgres psql -U mvptrading`
- Documentation: `/docs` directory

---

**Version**: 1.0  
**Last Updated**: January 2026  
**Status**: Production Ready ✅
