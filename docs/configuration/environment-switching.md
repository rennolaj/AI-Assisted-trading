# Environment Switching Guide

## Overview

The system supports three execution environments with **one-command switching** for safety:

| Environment | Mode | API | Money | Use Case |
|-------------|------|-----|-------|----------|
| **Simulated** | `SIMULATED` | None | Fake | Local testing, development |
| **Demo** | `KRAKEN_DEMO` | Kraken Demo API | Paper | Integration testing, E2E validation |
| **Production** | `KRAKEN_LIVE` | Kraken Live API | **REAL** | Live trading |

## Quick Switch

```bash
# Switch to simulated (no API calls)
./scripts/switch-env.sh simulated

# Switch to demo (Kraken demo account)
./scripts/switch-env.sh demo

# Switch to production (REQUIRES CONFIRMATION)
./scripts/switch-env.sh prod
```

## Environment Details

### 1. Simulated Environment

**When to use:**
- Local development and debugging
- Testing without external dependencies
- CI/CD pipelines

**Configuration:** (`config/execution.simulated.json`)
```json
{
  "mode": "SIMULATED",
  "slippageCapPct": 0.0,
  "heartbeatIntervalSeconds": 10,
  "staleThresholdSeconds": 30,
  "maxOrderRetries": 0
}
```

**Behavior:**
- ✅ No API calls to exchanges
- ✅ Orders instantly "filled" with mock data
- ✅ Full audit chain persisted to database
- ✅ Safe for testing without credentials

**Switch command:**
```bash
./scripts/switch-env.sh simulated
```

### 2. Demo Environment (Current Default)

**When to use:**
- Integration testing with live API
- E2E validation runs
- Smoke tests
- Training and demos

**Configuration:** (`config/execution.demo.json`)
```json
{
  "mode": "KRAKEN_DEMO",
  "slippageCapPct": 0.0025,
  "heartbeatIntervalSeconds": 10,
  "staleThresholdSeconds": 30,
  "maxOrderRetries": 1,
  "krakenBaseUrl": "https://demo-futures.kraken.com/derivatives",
  "krakenAuthBaseUrl": "https://demo-futures.kraken.com/derivatives"
}
```

**Required credentials in `.env`:**
```bash
KRAKEN_FUTURES_API_KEY=your-demo-api-key
KRAKEN_FUTURES_API_SECRET=your-demo-api-secret
```

**Behavior:**
- ✅ Real API calls to Kraken **demo** environment
- ✅ Paper money only (no real funds at risk)
- ✅ Orders execute on demo exchange
- ✅ Demo account balance tracked

**Get demo credentials:**
1. Sign up at https://demo-futures.kraken.com/
2. Create API keys with trading permissions
3. Add to `.env` file

**Switch command:**
```bash
./scripts/switch-env.sh demo
```

### 3. Production Environment ⚠️

**When to use:**
- **ONLY when ready for live trading with real money**
- After extensive testing in demo environment
- With proper risk management in place

**Configuration:** (`config/execution.prod.json`)
```json
{
  "mode": "KRAKEN_LIVE",
  "slippageCapPct": 0.001,
  "heartbeatIntervalSeconds": 10,
  "staleThresholdSeconds": 30,
  "maxOrderRetries": 2,
  "krakenBaseUrl": "https://futures.kraken.com/derivatives/api/v3",
  "krakenAuthBaseUrl": "https://futures.kraken.com/api/auth/v1",
  "_warning": "PRODUCTION MODE - REAL MONEY AT RISK"
}
```

**Required credentials in `.env`:**
```bash
# ⚠️ PRODUCTION CREDENTIALS - HANDLE WITH EXTREME CARE
KRAKEN_FUTURES_API_KEY=your-production-api-key
KRAKEN_FUTURES_API_SECRET=your-production-api-secret
```

**Behavior:**
- ⚠️ Real API calls to Kraken **production** environment
- ⚠️ **REAL MONEY** at risk
- ⚠️ Real trades executed on live market
- ⚠️ Account balance changes affect real funds

**Safety measures:**
- Requires typing "CONFIRM" to switch
- Explicit warning messages
- Different slippage cap (tighter: 0.1% vs 0.25%)
- More order retries (2 vs 1)

**Switch command:**
```bash
./scripts/switch-env.sh prod
# You will be prompted to type 'CONFIRM'
```

## Verification

Check current environment:

```bash
# Show current mode
./scripts/switch-env.sh

# Or inspect config directly
cat config/execution.json | grep mode
```

Verify in logs:
- Look for `SIMULATED`, `KRAKEN_DEMO`, or `KRAKEN_LIVE` in execution logs
- Check order receipts: simulated orders have `SIMULATED_FILLED` status

## Safety Checklist Before Production

- [ ] Extensive testing in **SIMULATED** environment
- [ ] Successful E2E runs in **DEMO** environment
- [ ] Demo account shows expected order behavior
- [ ] All tests passing (`dotnet test`)
- [ ] Risk limits configured (`config/account.json`)
- [ ] Production API keys created with correct permissions
- [ ] Kill switch tested (`M7.2` - if implemented)
- [ ] Monitoring and alerts configured (`M7.3` - if implemented)
- [ ] Team review and approval
- [ ] Backup plan for emergency shutdown

## Troubleshooting

**"Mode is KRAKEN_DEMO but orders aren't executing"**
- Check `.env` has valid demo API credentials
- Verify `KRAKEN_FUTURES_BASE_URL` points to demo
- Run integration tests: `dotnet test tests/Mvp.Trading.Execution.Tests/`

**"Switched to prod but still using demo API"**
- Ensure `.env` has production credentials
- Restart the application after switching
- Verify URLs in `config/execution.json` match production

**"Want to test locally without any API"**
- Switch to simulated: `./scripts/switch-env.sh simulated`
- No credentials needed
- Full system functionality with mock execution

## Best Practices

1. **Default to demo** - Keep `config/execution.json` as demo by default
2. **Never commit production credentials** - `.env` is gitignored
3. **Test the switch** - Verify behavior after each environment change
4. **Gradual rollout** - Simulated → Demo → Prod (small position) → Prod (full)
5. **Monitor closely** - Watch first production trades carefully
6. **Have a kill switch ready** - Manual order cancellation process prepared

## Related Documentation

- Integration Tests: `tests/Mvp.Trading.Execution.Tests/README.md`
- M6 Status: `docs/milestones/m6-risk-execution/m6-status.md`
- M7 Requirements: `docs/milestones/m7-hardening-observability/m7-requirements-overview.md` (kill switch, monitoring)
