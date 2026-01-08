# M9 Scenario Framework

## Overview

Created a flexible configuration framework to test LLM adjudication across different account sizes and contract specifications. This allows comprehensive testing of the position sizing algorithm with various equity levels and contract types.

## Motivation

During M9 fixture testing, we discovered that position sizing with Bitcoin perpetuals requires careful configuration:

**The Problem:**
- Bitcoin contracts at Kraken use `qtyStep=1` (whole contracts only)
- BTC price ~$82,000 means 1 contract = $82,000 notional
- Formula: `qty = (equity × riskPct) / stopDistance`
- With $100k equity and 1% risk = $1,000 risk budget
- For 3% stop: $1,000 / $2,460 = 0.407 contracts → rounds to 0 ❌

**The Solution:**
- Increased equity to $500k for whole-contract testing
- Created fractional contract scenarios (qtyStep=0.001) for smaller accounts
- Built switching framework to test all configurations

## Scenarios

### 1. Original - $500k Equity (Whole Contracts)
```json
{
  "equity": 500000,
  "qtyStep": 1,
  "minQty": 1
}
```

**Use Case:** Institutional/high-capital retail trader
**Status:** ✅ Proven working
**Position Example:**
- Risk: 1% = $5,000
- Stop: 3% = $2,460
- Qty: $5,000 / $2,460 = 2.03 → 2 contracts
- Notional: 2 × $82,000 = $164,000

### 2. Fractional - $100 Equity
```json
{
  "equity": 100,
  "qtyStep": 0.001,
  "minQty": 0.001
}
```

**Use Case:** Micro account testing
**Position Example:**
- Risk: 1% = $1
- Stop: 3% = $2,460
- Qty: $1 / $2,460 = 0.0004 contracts
- Notional: 0.0004 × $82,000 = $33

### 3. Fractional - $1,000 Equity
```json
{
  "equity": 1000,
  "qtyStep": 0.001,
  "minQty": 0.001
}
```

**Use Case:** Small retail account
**Position Example:**
- Risk: 1% = $10
- Stop: 3% = $2,460
- Qty: $10 / $2,460 = 0.0041 contracts
- Notional: 0.0041 × $82,000 = $336

### 4. Fractional - $10,000 Equity
```json
{
  "equity": 10000,
  "qtyStep": 0.001,
  "minQty": 0.001
}
```

**Use Case:** Medium retail account
**Position Example:**
- Risk: 1% = $100
- Stop: 3% = $2,460
- Qty: $100 / $2,460 = 0.0407 contracts
- Notional: 0.0407 × $82,000 = $3,337

## Usage

### Switching Scenarios

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

### After Switching

Always rebuild the Docker worker:
```bash
docker compose build worker
docker compose down && docker compose up -d
```

### Verifying Current Scenario

```bash
# Check equity
cat config/account.json | jq '.equity'

# Check qtyStep
cat config/instruments.json | jq '.instruments[] | select(.symbol == "BTCUSD.P") | .qtyStep'
```

## Directory Structure

```
config/
├── account.json              # Active configuration
├── instruments.json          # Active configuration
├── .backups/                 # Auto-created timestamped backups
│   └── YYYYMMDD-HHMMSS/
└── scenarios/                # Scenario templates
    ├── README.md
    ├── account.original-500k.json
    ├── account.fractional-100.json
    ├── account.fractional-1000.json
    ├── account.fractional-10000.json
    ├── instruments.original-qtystep1.json
    ├── instruments.fractional-qtystep0001.json
    └── risk-policy.original.json

scripts/fixtures/
└── switch-scenario.sh        # Scenario switching helper
```

## Position Sizing Math

### Formula
```
riskAmount = equity × (maxAccountRiskPctPerTrade / 100)
pointRisk = stopDistance × contractMultiplier
qty = RoundDownToStep(riskAmount / pointRisk, qtyStep)
```

### Comparison Table (3% Stop on BTC @ $82,000)

| Scenario | Equity | Risk % | Risk $ | Stop $ | Point Risk | Qty | Notional |
|----------|--------|--------|--------|--------|------------|-----|----------|
| Original | $500k | 1% | $5,000 | $2,460 | $2,460 | 2 | $164k |
| Frac-10k | $10k | 1% | $100 | $2,460 | $2,460 | 0.041 | $3,337 |
| Frac-1k | $1k | 1% | $10 | $2,460 | $2,460 | 0.004 | $336 |
| Frac-100 | $100 | 1% | $1 | $2,460 | $2,460 | 0.0004 | $33 |

## Real-World Considerations

### Whole Contracts (qtyStep=1)
- **Traditional futures exchanges**
- CME, ICE, traditional commodity futures
- Requires substantial capital
- Minimum BTC notional: ~$80-140k

### Fractional Contracts (qtyStep=0.001)
- **Modern crypto exchanges**
- Kraken, Binance, Bybit, FTX (historically)
- Accessible for retail traders
- Minimum notional: $10-100

### Our Testing Strategy
1. **Primary:** Use original $500k setup (matches institutional reality)
2. **Secondary:** Test fractional scenarios (validates algorithm flexibility)
3. **Edge Cases:** Use $100 scenario (stress test minimum viable sizing)

## Testing Checklist

When generating fixtures, test each scenario:

- [ ] Original ($500k, whole contracts) - baseline working config
- [ ] Fractional-10000 ($10k) - comfortable retail sizing
- [ ] Fractional-1000 ($1k) - tight but viable retail sizing
- [ ] Fractional-100 ($100) - extreme edge case testing

## Key Learnings

1. **Account equity must match instrument specifications**
   - Whole contracts need substantial capital
   - Fractional sizing enables smaller accounts

2. **Position sizing formula is universal**
   - Same algorithm works for all scenarios
   - Only inputs change (equity, qtyStep, minQty)

3. **Configuration flexibility is critical**
   - Different exchanges have different specs
   - Testing requires multiple realistic scenarios
   - Switching framework prevents errors

4. **Backups are essential**
   - Auto-backup before each switch
   - Timestamped directories preserve history
   - Easy rollback if needed

## Related Documentation

- [Position Sizing Fix](../commit-1cd6544.md) - Original $500k equity fix
- [Fixture Mode Switching](fixture-mode-switching.md) - Environment configuration
- [Fixture Mode Quick Reference](fixture-mode-quick-ref.md) - Common commands
- [M9 Status](m9-status.md) - Overall M9 progress

## Future Enhancements

1. **Add more instruments**
   - ETH perpetuals
   - Different contract sizes
   - Various exchanges

2. **Automated scenario testing**
   - Script to test all scenarios sequentially
   - Validation that all pass with same fixture
   - Comparison report

3. **Dynamic scenario generation**
   - Input desired equity level
   - Calculate appropriate qtyStep
   - Generate custom scenario files

4. **Exchange-specific presets**
   - Kraken configuration
   - Binance configuration
   - CME configuration
