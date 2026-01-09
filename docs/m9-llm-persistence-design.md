# LLM Decision Persistence Design

## Problem
Currently, we cannot see:
1. The exact prompt sent to the LLM
2. The raw LLM response (before parsing)
3. The LLM's reasoning text
4. Token usage and timing metrics

This makes debugging LLM rejections impossible.

## Database Schema Changes

### New Table: `llm_adjudications`
```sql
CREATE TABLE llm_adjudications (
    adjudication_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    alert_id UUID NOT NULL REFERENCES alerts(alert_id),
    correlation_id UUID NOT NULL,
    
    -- LLM Request
    prompt_text TEXT NOT NULL,
    prompt_tokens INTEGER,
    
    -- LLM Response
    raw_response TEXT NOT NULL,
    completion_tokens INTEGER,
    total_tokens INTEGER,
    
    -- Parsed Decision
    decision VARCHAR(50) NOT NULL, -- ALLOW_LONG_W3, REJECT, etc.
    reasoning TEXT NOT NULL,
    confidence DECIMAL(5,2),
    
    -- Metadata
    llm_provider VARCHAR(50) NOT NULL, -- openai, local, etc.
    llm_model VARCHAR(100),
    response_time_ms INTEGER,
    adjudicated_at_utc TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    
    -- Error tracking
    parse_error TEXT,
    validation_errors JSONB
);

CREATE INDEX idx_llm_adjudications_alert ON llm_adjudications(alert_id);
CREATE INDEX idx_llm_adjudications_decision ON llm_adjudications(decision);
CREATE INDEX idx_llm_adjudications_time ON llm_adjudications(adjudicated_at_utc);
```

## Code Changes Required

### 1. Store LLM Request/Response in Worker
**File**: `src/Mvp.Trading.Worker/AlertWorker.cs`

```csharp
// After calling LLM adjudication
var llmResult = await _mcpGateway.AdjudicateAsync(input, ct);

// NEW: Persist full LLM interaction
await _llmAdjudicationStore.SaveAsync(new LlmAdjudication
{
    AlertId = alert.AlertId,
    CorrelationId = correlationId,
    PromptText = llmResult.PromptSent,
    PromptTokens = llmResult.PromptTokens,
    RawResponse = llmResult.RawResponse,
    CompletionTokens = llmResult.CompletionTokens,
    TotalTokens = llmResult.TotalTokens,
    Decision = llmResult.Decision.Decision,
    Reasoning = llmResult.Decision.Reasoning,
    Confidence = llmResult.Decision.Confidence,
    LlmProvider = llmResult.Provider,
    LlmModel = llmResult.Model,
    ResponseTimeMs = llmResult.DurationMs,
    ParseError = llmResult.ParseError,
    ValidationErrors = llmResult.ValidationErrors
}, ct);
```

### 2. Update MCP Gateway to Return Full Context
**File**: `src/Mvp.Trading.Api/Mcp/McpGatewayRouter.cs`

```csharp
public async Task<LlmAdjudicationResult> AdjudicateAsync(...)
{
    var prompt = BuildPrompt(input); // Capture this
    var startTime = DateTime.UtcNow;
    
    var response = await _client.ChatAsync(...);
    var endTime = DateTime.UtcNow;
    
    return new LlmAdjudicationResult
    {
        PromptSent = prompt,
        RawResponse = response.Content,
        Decision = ParseDecision(response.Content),
        Provider = _provider,
        Model = response.Model,
        DurationMs = (int)(endTime - startTime).TotalMilliseconds,
        PromptTokens = response.Usage?.PromptTokens,
        CompletionTokens = response.Usage?.CompletionTokens,
        TotalTokens = response.Usage?.TotalTokens,
        ParseError = parseError,
        ValidationErrors = validationErrors
    };
}
```

### 3. Update Fixture Capture Script
**File**: `scripts/fixtures/capture-llm-decision.sh`

Add LLM adjudication data to fixture:

```sql
SELECT 
    prompt_text,
    raw_response,
    reasoning,
    decision,
    confidence,
    response_time_ms,
    parse_error
FROM llm_adjudications
WHERE alert_id = '<ALERT_ID>';
```

## Benefits

1. **Full Debugging**: See exactly what LLM saw and what it said
2. **Prompt Engineering**: Analyze which prompts work/fail
3. **Cost Tracking**: Monitor token usage per alert
4. **Performance**: Track LLM response times
5. **Error Analysis**: Understand JSON parsing failures
6. **Test Fixtures**: Complete data for test generation

## Migration Path

1. Create `llm_adjudications` table (new migration)
2. Add `ILlmAdjudicationStore` interface
3. Implement `PostgresLlmAdjudicationStore`
4. Update `AlertWorker` to persist after LLM call
5. Update `capture-llm-decision.sh` to include LLM data
6. Update fixture JSON schema to include `llmAdjudication` section

## Fixture JSON Update

```json
{
  "input": {
    "alert": {...},
    "indicatorSnapshot": {...},
    "elliottCandidates": [...]
  },
  "llmAdjudication": {
    "prompt": "You are an expert...",
    "rawResponse": "{\"decision\": \"REJECT\", ...}",
    "decision": "REJECT",
    "reasoning": "We need direction from indicatorSnapshot.direction but not present.",
    "confidence": 90,
    "parseError": null,
    "responseTimeMs": 1245,
    "provider": "openai",
    "model": "gpt-4",
    "promptTokens": 1850,
    "completionTokens": 150,
    "totalTokens": 2000
  },
  "output": {
    "decision": "REJECT",
    "status": "rejected"
  }
}
```
