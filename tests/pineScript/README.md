# Pine Script Collection

This directory contains TradingView Pine Scripts for the MVP trading system.

## Scripts

### 1. `mvp-smoke-test-alerts.pine` - Testing & Validation
**Purpose**: High-frequency alert generation for system testing  
**Status**: ⚠️ Testing Only - NOT for production trading  

**Characteristics:**
- Generates many alerts quickly (pulses, micro-moves, crossovers)
- Multiple trigger conditions for pipeline stress testing
- Flood mode with realtime alerts
- No quality filters or gates

**Use Cases:**
- Testing webhook ingestion pipeline
- Validating Redis queue processing
- Stress testing worker capacity
- Database persistence verification
- End-to-end system validation

**Setup Instructions:**
See `README.md` main project file under "Smoke test (ngrok)" section.

---

### 2. `mvp-production-alerts.pine` - Production Trading ✅
**Purpose**: Quality trading signals aligned with MVP indicator logic  
**Status**: ✅ Production Ready

**Characteristics:**
- RSI extreme zones required (oversold ≤30 for LONG, overbought ≥70 for SHORT)
- Multiple confirmation signals (RSI multi-TF, Stoch RSI reversal, MACD momentum, trend, volume)
- Minimum 3 out of 5 confirmations required
- Optional trend alignment and volume filters
- Bar close only (reliable signals)
- Rich JSON payload with all indicator values

**Use Cases:**
- Real trading signal generation
- Production deployment
- Demo/paper trading
- Live trading with Kraken Futures

**Setup Instructions:**
See `docs/pine-script-production-guide.md` for complete documentation.

---

## Quick Comparison

| Feature | Smoke Test | Production |
|---------|-----------|------------|
| **Alert Frequency** | Very High (100s/hour) | Low (0-10/day) |
| **Quality Gates** | None | RSI extremes required |
| **Confirmations** | Single indicators | Multiple (3+ required) |
| **Use Case** | System testing | Real trading |
| **Risk Level** | N/A (testing) | Production trading |

---

## Choosing the Right Script

### Use Smoke Test When:
- ✅ Testing new deployment
- ✅ Validating webhook connectivity
- ✅ Stress testing the system
- ✅ Checking alert processing pipeline
- ✅ Verifying database persistence
- ✅ Development and debugging

### Use Production When:
- ✅ Live trading (demo or production)
- ✅ Generating real entry signals
- ✅ Testing with real market conditions
- ✅ Validating LLM adjudication quality
- ✅ Backtesting strategy performance
- ✅ Production deployment

---

## Common Setup Steps (Both Scripts)

### 1. Create TradingView Alert
1. Load script in TradingView
2. Click "Alert" button
3. Condition: "Any alert() function call"
4. Webhook URL: `https://your-domain.com/webhooks/tradingview/YOUR_SECRET`
5. Message: `{{alert_message}}`
6. Frequency: Once Per Bar Close (Production) or All (Smoke Test)

### 2. Configure Backend
Ensure `.env` or `.env.smoke` has:
```bash
TRADINGVIEW_WEBHOOK_SECRET=your-secret-here
SYMBOL_HINT=BTCUSD.P
TICKER=BTCUSD.P
EXCHANGE=krakenfutures
```

### 3. Start Services
```bash
# For smoke testing
docker compose --env-file .env.smoke up -d --build api worker

# For production
docker compose up -d --build
```

### 4. Verify Alert Reception
```bash
# Watch logs
docker compose logs -f api worker

# Check database
docker compose exec postgres psql -U mvptrading -c "SELECT alert_id, ticker, direction_hint, created_utc FROM alerts ORDER BY created_utc DESC LIMIT 10;"

# Check metrics
curl http://localhost:8080/metrics | grep trading_alerts_received_total
```

---

## JSON Payload Format

Both scripts send JSON payloads with similar structure:

### Minimum Required Fields
```json
{
  "idempotencyKey": "BTCUSD.P-5-LONG-1736208300000",
  "ticker": "BTCUSD.P",
  "exchange": "krakenfutures",
  "interval": "5",
  "close": 50000.50,
  "volume": 1234567.89,
  "directionHint": "LONG",
  "symbolHint": "BTCUSD.P"
}
```

### Production Script Additional Fields
```json
{
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

---

## Testing Workflow

### 1. Start with Smoke Test
```bash
# Use smoke test to validate system
./scripts/smoke.sh

# Verify alerts flowing through
docker compose logs -f api worker
```

### 2. Switch to Production Script
```bash
# Stop smoke test
docker compose down

# Start with production config
docker compose up -d --build

# Monitor for quality signals (may take hours/days)
docker compose logs -f api worker
```

### 3. Monitor Performance
```bash
# Check Prometheus metrics
curl http://localhost:8080/metrics

# View Grafana dashboards
open http://localhost:3000

# Query recent alerts
docker compose exec postgres psql -U mvptrading -c "
  SELECT 
    alert_id, 
    ticker, 
    direction_hint, 
    status, 
    created_utc 
  FROM alerts 
  ORDER BY created_utc DESC 
  LIMIT 20;
"
```

---

## Troubleshooting

### Alerts Not Reaching Backend

1. **Check TradingView Alert Settings**
   - Webhook URL correct?
   - Message set to `{{alert_message}}`?
   - Alert still active (not expired)?

2. **Check Network Connectivity**
   ```bash
   # Test webhook endpoint
   curl -X POST http://localhost:8080/webhooks/tradingview/YOUR_SECRET \
     -H "Content-Type: application/json" \
     -d '{"ticker":"BTCUSD.P","exchange":"krakenfutures","interval":"5","close":50000,"volume":1000000,"directionHint":"LONG","symbolHint":"BTCUSD.P","idempotencyKey":"test-123"}'
   ```

3. **Check API Logs**
   ```bash
   docker compose logs api | grep "webhook"
   docker compose logs api | grep "alert"
   ```

### Production Script Not Firing

**This is EXPECTED behavior** - production script only fires on genuine setups.

To verify it's working:
1. Check status table on TradingView chart
2. Temporarily lower `minConfirmations` to 1 for testing
3. Wait for RSI to reach extreme zones (30/70)
4. Check historical bars for past signals

### Smoke Test Not Generating Enough Alerts

Check settings:
- `floodMode` enabled?
- `microMovePct` low enough (0.01%)?
- `pulseMs` interval reasonable (250ms)?
- Market has price movement?

---

## Best Practices

### Development
- ✅ Use smoke test for rapid iteration
- ✅ Test with `localhost:8080` first
- ✅ Use ngrok for TradingView webhooks
- ✅ Monitor logs during testing
- ✅ Check database for alert persistence

### Production
- ✅ Use production script only
- ✅ Start with demo environment (`KRAKEN_FUTURES_ENV=demo`)
- ✅ Verify LLM adjudication quality
- ✅ Monitor alert frequency (should be low)
- ✅ Review Grafana dashboards daily
- ✅ Check execution outcomes
- ✅ Never commit webhook secrets to git

### Safety
- ⚠️ Never use smoke test for real trading
- ⚠️ Test thoroughly in demo mode first
- ⚠️ Start with small position sizes
- ⚠️ Monitor kill switch functionality
- ⚠️ Review reconciliation discrepancies
- ⚠️ Keep `.env` files secure and untracked

---

## Documentation

- **Production Script Guide**: `docs/pine-script-production-guide.md`
- **Command Reference**: `docs/command-reference.md`
- **Smoke Testing**: `README.md` (Smoke test section)
- **Alert Dataflow**: `docs/alert-dataflow-overview.md`
- **Backend Indicators**: `src/Mvp.Trading.Indicators/IndicatorEngine.cs`

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | Jan 2026 | Initial smoke test script |
| 2.0 | Jan 2026 | Added production script with MVP alignment |

---

**Note**: Always use the production script for real trading. The smoke test script is designed to generate high volumes of test data and should never be used with real money.
