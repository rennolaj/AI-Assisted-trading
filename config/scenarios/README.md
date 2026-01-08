# Configuration Scenarios for M9 Fixture Testing

This directory contains different configuration scenarios to test position sizing with various equity levels.

## Scenarios Overview

### 1. Original (Working Setup)
- **Equity:** $500,000
- **qtyStep:** 1 (whole contracts only)
- **minQty:** 1
- **Use Case:** Realistic institutional/high-capital retail setup
- **Status:** ✅ Working - can size BTC positions with 1-5% stops

Files:
- `account.original-500k.json`
- `instruments.original-qtystep1.json`
- `risk-policy.original.json`

### 2. Fractional - $100 Equity
- **Equity:** $100
- **qtyStep:** 0.0002 (1/5000th of whole contract)
- **minQty:** 0.0002
- **Use Case:** Testing micro-account position sizing
- **Risk:** 1% = $1.00
- **Expected BTC position:** ~0.0004 contracts (~$33 notional)
- **Risk Profile:** Same as $500k setup (proportionally scaled)

Files:
- `account.fractional-100.json`
- `instruments.fractional-100.json` (qtyStep=0.0002)

### 3. Fractional - $1,000 Equity
- **Equity:** $1,000
- **qtyStep:** 0.002 (1/500th of whole contract)
- **minQty:** 0.002
- **Use Case:** Testing small retail account
- **Risk:** 1% = $10.00
- **Expected BTC position:** ~0.004 contracts (~$330 notional)
- **Risk Profile:** Same as $500k setup (proportionally scaled)

Files:
- `account.fractional-1000.json`
- `instruments.fractional-1000.json` (qtyStep=0.002)

### 4. Fractional - $10,000 Equity
- **Equity:** $10,000
- **qtyStep:** 0.02 (1/50th of whole contract)
- **minQty:** 0.02
- **Use Case:** Testing medium retail account
- **Risk:** 1% = $100.00
- **Expected BTC position:** ~0.04 contracts (~$3,300 notional)
- **Risk Profile:** Same as $500k setup (proportionally scaled)

Files:
- `account.fractional-10000.json`
- `instruments.fractional-10000.json` (qtyStep=0.02)

## Position Sizing Math

Formula: `qty = riskAmount / pointRisk`

Where:
- `riskAmount = equity × (maxAccountRiskPctPerTrade / 100)`
- `pointRisk = stopDistance × contractMultiplier`

**KEY PRINCIPLE:** qtyStep scales proportionally with equity to maintain consistent risk profile:
```
qtyStep = (equity / 500000) × 1
```

### Example with 3% Stop on BTC @ $82,000

| Equity | Risk % | Risk $ | Stop Distance | qtyStep | Qty | Notional | Actual Risk % |
|--------|--------|--------|---------------|---------|-----|----------|---------------|
| $100 | 1% | $1 | $2,460 | 0.0002 | 0.0004 | $33 | 0.98% |
| $1,000 | 1% | $10 | $2,460 | 0.002 | 0.004 | $328 | 0.98% |
| $10,000 | 1% | $100 | $2,460 | 0.02 | 0.04 | $3,280 | 0.98% |
| $500,000 | 1% | $5,000 | $2,460 | 1 | 2 | $164,000 | 0.98% |

**Notice:** All scenarios achieve ~0.98% actual risk (consistent across all equity levels)

## Switching Scenarios

Use the helper script:

```bash
# Switch to fractional $100 setup
./scripts/fixtures/switch-scenario.sh fractional-100

# Switch to fractional $1,000 setup
./scripts/fixtures/switch-scenario.sh fractional-1000

# Switch to fractional $10,000 setup
./scripts/fixtures/switch-scenario.sh fractional-10000

# Switch back to original $500k setup
./scripts/fixtures/switch-scenario.sh original
```

## Testing Notes

### qtyStep Considerations
- **qtyStep = 1**: Can only trade whole contracts (traditional futures)
- **qtyStep = 0.001**: Can trade 1/1000th of a contract (like crypto exchanges)
- Most crypto exchanges support fractional contracts
- Kraken Futures supports fractional sizing

### minNotional
- Set to $10 minimum
- All fractional scenarios exceed this minimum
- Even $100 equity with 0.0004 BTC contracts = $33 > $10 ✓

### Testing Strategy
1. Start with original ($500k) - verify working ✅
2. Test fractional-10000 - should work easily
3. Test fractional-1000 - should work with tight stops
4. Test fractional-100 - edge case, very small positions

## Expected Results

All scenarios should pass position sizing if:
- Elliott provides valid pattern
- Stop loss is reasonable (2-5%)
- Risk policy allows the trade

The fractional scenarios test the lower bounds of the position sizing algorithm.
