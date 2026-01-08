# M9: LLM Test Fixtures - Findings

## Date: 2026-01-08

## Objective
Create a library of LLM decision fixtures (both ALLOW and REJECT) to establish source of truth for testing and validating prompt engineering.

## Findings

### ForceAllow Limitations
**Issue**: ForceAllow mode cannot generate positive fixtures with synthetic webhooks.

**Root Cause**: 
- ForceAllow only works when valid Elliott candidates exist
- Elliott engine requires 5+ pivots to form valid wave patterns
- Synthetic webhooks contain only single price points without historical context
- Error: "Only 3 pivots available" - insufficient data for wave analysis

**Log Evidence**:
```
MCP force-allow enabled but no eligible candidate found; falling back to LLM.
MCP adjudication failed for alert: LOCAL_LLM_OUTPUT_MISSING
```

**Elliott Candidates JSON**:
```json
{
  "candidates": [{
    "score": 0,
    "waveLabel": "OTHER",
    "confidence": 0,
    "ruleViolations": [{
      "rule": "EW_PIVOTS_INSUFFICIENT",
      "details": "Only 3 pivots available.",
      "severity": "ERROR"
    }]
  }]
}
```

### Alternative Approaches

#### 1. Use Real TradingView Alerts (RECOMMENDED)
**Process**:
1. Configure TradingView to send alerts for various market conditions
2. Let system process naturally with LLM
3. Manually review LLM decisions (ALLOW/REJECT)
4. Use `capture-llm-decision.sh` to save as fixtures
5. Curate best examples for regression testing

**Pros**:
- Real market data with valid Elliott patterns
- Authentic LLM decision-making context
- Captures actual system behavior
- Natural distribution of scenarios

**Cons**:
- Requires waiting for market conditions
- Can't control exact scenarios
- Need manual curation

#### 2. Mock Historical Data Pipeline
**Process**:
1. Create historical candle fixtures (1000+ candles)
2. Mock Kraken API responses in test environment
3. Send webhook triggering indicator calculation
4. Elliott engine analyzes mocked history
5. ForceAllow bypasses LLM with valid candidates

**Pros**:
- Full control over scenarios
- Can generate specific wave patterns
- Reproducible test cases

**Cons**:
- Complex to implement (mock entire data pipeline)
- Risk of unrealistic scenarios
- High maintenance burden

#### 3. Replay Production Scenarios
**Process**:
1. Capture production alerts with all context
2. Store as integration test fixtures
3. Replay in test environment with mocked LLM
4. Validate system behavior matches expectations

**Pros**:
- Real-world scenarios
- Complete context available
- Validates entire pipeline

**Cons**:
- Requires production data
- Privacy/security considerations
- Limited scenario coverage

## Recommendation

**Phase 1: Real Alert Capture (IMMEDIATE)**
- Use existing `capture-llm-decision.sh` script
- Monitor production for 2-3 days
- Capture 10+ REJECT and hopefully some ALLOW cases
- Build initial fixture library organically

**Phase 2: Scenario Engineering (FUTURE)**
- Identify gaps in fixture coverage
- Create TradingView strategies for specific patterns
- Target missing scenarios (strong wave 5, clear corrections, etc.)
- Supplement with real alerts

**Phase 3: Mock Pipeline (IF NEEDED)**
- Only if critical scenarios can't be captured naturally
- Focus on edge cases and specific rule violations
- Keep scope minimal to reduce maintenance

## Current Status

### Completed
- ✅ Created fixture directory structure (`tests/fixtures/llm-decisions/`)
- ✅ Built `capture-llm-decision.sh` (captures full context from DB)
- ✅ Built `generate-positive-fixture.sh` (explored ForceAllow approach)
- ✅ Captured 2 REJECT fixtures
- ✅ Identified ForceAllow limitations

### Blocked
- ❌ Cannot generate synthetic ALLOW fixtures without historical data
- ❌ LLM connection issues resolved (BASE_URL fixed: http://10.10.50.16:1234)

### Next Steps
1. Monitor production alerts for natural ALLOW decisions
2. Manually trigger TradingView alerts with strong patterns
3. Capture diverse scenarios over 2-3 days
4. Build regression test suite from captured fixtures
5. Document fixture metadata standards

## Technical Notes

### Configuration Fixed
- **Database Name**: Changed from `mvp_trading` to `ai-trading-db` in `.env`
- **LLM URL**: Changed from `http://host.docker.internal:11434` to `http://10.10.50.16:1234`
- **Environment File**: Docker Compose uses `.env` (not `.env.prod.local`)
- **Container Restarts**: Must use `docker compose restart` (not `docker restart`) to pick up env changes

### LLM Validation
```bash
# Test LM Studio directly
curl -X POST http://10.10.50.16:1234/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{"model": "openai/gpt-oss-20b", "messages": [{"role": "user", "content": "Say hello"}]}'

# Response includes "reasoning" field unique to LM Studio
{
  "choices": [{
    "message": {
      "role": "assistant",
      "content": "Hello!",
      "reasoning": "User says \"Say hello\". Just respond."
    }
  }]
}
```

### Database Schema Notes
- `alerts` table: Contains `alert_json` JSONB with webhook payload
- `elliott_candidates` table: One row per alert with `candidates_json` array
- `alert_processing` table: Tracks status and error messages
- Join key: `alert_id` (UUID) across all tables

## Lessons Learned

1. **ForceAllow is not a fixture generator** - it's a production bypass for urgent scenarios
2. **Elliott Wave requires real data** - minimum 5 pivots across sufficient timeframe
3. **Synthetic data insufficient** - can't replicate complex market patterns easily
4. **Real alerts are best fixtures** - authentic context, natural LLM decisions
5. **Infrastructure debugging crucial** - database name and LLM URL issues blocked initial testing
