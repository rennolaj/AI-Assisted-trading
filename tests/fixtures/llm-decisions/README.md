# LLM Decision Fixtures

This directory contains captured LLM decision fixtures for testing and validation.

## Purpose

- **Regression Testing**: Ensure prompts and LLM behavior remain consistent
- **Validation**: Verify that acceptance criteria actually produce ALLOW decisions
- **Documentation**: Record what scenarios the LLM accepts vs rejects
- **Model Comparison**: Compare responses across different LLM models/versions

## Directory Structure

```
llm-decisions/
├── accept/           # Fixtures where LLM decided ALLOW
│   ├── strong-impulse-long/
│   ├── clear-correction-short/
│   └── ...
├── reject/           # Fixtures where LLM decided REJECT
│   ├── insufficient-confidence/
│   ├── ambiguous-count/
│   └── ...
└── templates/        # Templates for creating new fixtures
```

## Fixture Format

Each fixture is a JSON file containing:

```json
{
  "metadata": {
    "captureDate": "2026-01-08T14:30:00Z",
    "llmProvider": "local",
    "llmModel": "openai/gpt-oss-20b",
    "promptVersion": "1.0",
    "schemaVersion": "1.0.0",
    "description": "Strong 5-wave impulse with clear wave structure",
    "scenario": "LONG entry on completed impulse wave",
    "expectedDecision": "ALLOWLONGDEMO"
  },
  "input": {
    "signalSnapshot": {
      "symbol": "PF_XBTUSD",
      "timestamp": "2026-01-08T13:00:00Z",
      "timeframes": {
        "M1": { "rsi": 45.2, "macd": 15.3, ... },
        "M5": { "rsi": 48.1, "macd": 22.1, ... },
        ...
      }
    },
    "elliottCandidates": {
      "candidates": [
        {
          "candidateId": "abc123",
          "waveLabel": "FIVE",
          "confidence": 85,
          "score": 92,
          "invalidation": {
            "longInvalidationPrice": 42500.00
          },
          ...
        }
      ]
    },
    "riskPolicy": {
      "maxLossPerTradePercent": 1.0,
      ...
    }
  },
  "output": {
    "decision": "ALLOWLONGDEMO",
    "confidence": 85,
    "selectedCandidateId": "abc123",
    "stopLossStrategy": "WAVEINVALIDATION",
    "reasoning": "Strong 5-wave impulse completed with clear wave structure..."
  },
  "validation": {
    "schemaValid": true,
    "decisionMatches": true,
    "confidenceInRange": true,
    "candidateIdValid": true
  }
}
```

## Metadata Fields

- **captureDate**: When the fixture was captured (ISO 8601)
- **llmProvider**: Provider used (local, openai, azure)
- **llmModel**: Specific model name/version
- **promptVersion**: Version of the prompt template used
- **schemaVersion**: LlmDecision schema version
- **description**: Human-readable description of the scenario
- **scenario**: Trading scenario type (LONG/SHORT entry, correction, impulse)
- **expectedDecision**: What decision we expect (for test validation)

## Capture Process

1. Run real alert through system
2. Capture input (indicators + Elliott candidates)
3. Capture LLM output (decision + reasoning)
4. Validate schema compliance
5. Tag with metadata
6. Save to appropriate directory (accept/ or reject/)

## Usage in Tests

```csharp
[Theory]
[MemberData(nameof(LoadAcceptFixtures))]
public async Task LlmDecision_AcceptFixture_ShouldProduceAllowDecision(LlmFixture fixture)
{
    // Arrange
    var input = fixture.Input;
    
    // Act
    var result = await _mcpGateway.AdjudicateElliottAsync(input, CancellationToken.None);
    
    // Assert
    Assert.True(result.Ok);
    Assert.StartsWith("ALLOW", result.Value.Decision);
    Assert.Equal(fixture.Output.SelectedCandidateId, result.Value.SelectedCandidateId);
}
```

## Fixture Categories

### Accept Scenarios (Target: 10+)
- Strong 5-wave impulse (LONG)
- Strong 5-wave impulse (SHORT)
- Clear ABC correction completion
- High confidence wave 3 extension
- Clean wave structure with low violations
- Multiple timeframe alignment
- Strong volume confirmation

### Reject Scenarios (Target: 5+)
- Insufficient wave confidence
- Ambiguous wave count
- Multiple rule violations
- Conflicting timeframe signals
- No valid invalidation level
- Risk-reward below threshold
- Insufficient pivot data

## Maintenance

- Review fixtures when prompts change
- Update expectedDecision if criteria evolve
- Add new fixtures for edge cases discovered in production
- Retire outdated fixtures when schemas change
- Version fixtures alongside prompt versions

## Tools

- `scripts/fixtures/capture-llm-decision.sh` - Capture from running alert
- `scripts/fixtures/validate-fixture.sh` - Validate fixture schema
- `scripts/fixtures/compare-llm-responses.sh` - Compare across models
