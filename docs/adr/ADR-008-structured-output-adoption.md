# ADR-008: Adopt Structured Output via Microsoft.Extensions.AI

| Field | Value |
|-------|-------|
| **ID** | ADR-008 |
| **Date** | 2026-05-15 |
| **Status** | PROPOSED |
| **Milestone** | M17 |
| **Author** | Analysis: GitHub Copilot CLI session 2026-05-15 |
| **Supersedes** | Hand-rolled JSON schema validation pipeline |
| **Depends on** | ADR-003 (Microsoft.Extensions.AI adoption) |

---

## Context

Every LLM call in the current system follows this pipeline to go from a prompt to a typed domain object:

```
1. Render prompt (FilePromptTemplateStore + {{token}} substitution)
2. Send to LLM via HttpClient (OpenAI Responses API or local LLM endpoint)
3. Receive raw string response
4. LocalLLM only: ExtractJsonFromResponse() — parse special tokens like <|channel|>final<|message|>{...}
5. Load JSON schema file from disk (IJsonSchemaValidator reads .schema.json)
6. Validate raw response string against JSON schema
7. Deserialize to typed record (JsonSerializer.Deserialize<LlmDecision>)
8. Handle null (deserialize failure → synthetic REJECT)
9. Return Result<McpAdjudicationResult>
```

**This is 9 steps to do what a single API call can do with structured output.**

`Microsoft.Extensions.AI` (MEA) — adopted in ADR-003 — provides `GetResponseAsync<T>()`:

```csharp
// With structured output — the entire pipeline above collapses to:
var assessment = await _chatClient.GetResponseAsync<ConfluenceAssessment>(prompt, new ChatOptions
{
    ResponseFormat = ChatResponseFormat.ForType<ConfluenceAssessment>()
});
```

Under the hood, `GetResponseAsync<T>()`:
- Generates a JSON schema from the C# type using `System.Text.Json` source generation
- Sends the schema to the LLM as `response_format: { type: "json_schema", json_schema: {...} }` (OpenAI structured output)
- Forces the model to produce schema-valid JSON (no hallucinated fields, no missing fields)
- Deserialises the response directly to `T`
- Throws `JsonException` on parse failure (handled by MEA middleware)

**The result: steps 4, 5, 6, 7, 8 collapse into a single type-safe API call with compile-time return type guarantees.**

### Current pain points in the hand-rolled validation pipeline:

| Pain Point | Location | Impact |
|-----------|----------|--------|
| `.schema.json` files must exist on disk at runtime | `IJsonSchemaValidator` + deploy config | Deployment failure if files missing; no compile-time safety |
| Schema files can diverge from C# records | Manual sync required | Silent runtime failures if record changed but schema not updated |
| `Substring(0, 500)` for prompt preview logging | `LocalLlmMcpGateway:85` | M14 anti-pattern (use range syntax `[..500]`) |
| `DateTimeOffset.UtcNow` used directly | Both gateways | M14 PERF-1 anti-pattern — not testable |
| `ExtractJsonFromResponse()` brittle JSON extraction | `LocalLlmMcpGateway:338` | Model-specific format changes can silently break |
| `{{input}}` token substitution in prompt templates | `FilePromptTemplateStore` | Hand-rolled string replacement; prone to injection if input contains `{{` |
| Schema version tracking in `IMcpConfigStore` | `mcp-config.json` | Extra config file to maintain; diverges from code reality |

---

## Decision

**Replace the hand-rolled JSON schema validation pipeline with `Microsoft.Extensions.AI`'s `GetResponseAsync<T>()` and `ChatResponseFormat.ForType<T>()` for all LLM calls that expect typed output.**

### Before (current pattern, OpenAiMcpGateway):

```csharp
// ~80 lines of pipeline per tool call
var schemaName = SanitizeSchemaName($"{toolName}-v{schemaVersion}");
var response = await _client.CreateResponseAsync(new OpenAiResponsesRequest(
    model, prompt, temperature, maxTokens, schemaName, schemaFileName), ct);

if (!response.Ok || string.IsNullOrWhiteSpace(response.Value))
    return Fail<McpAdjudicationResult>("...", "...");

var validation = _schemaValidator.Validate(schemaFileName, response.Value);
if (!validation.Ok)
    return Fail<McpAdjudicationResult>("...", validation.Error?.Message);

var decision = JsonSerializer.Deserialize<LlmDecision>(response.Value, _outputOptions);
if (decision is null)
    return Fail<McpAdjudicationResult>("...", "Failed to deserialize");

return Ok(new McpAdjudicationResult { Decision = decision, ... });
```

### After (with MEA structured output):

```csharp
// ~15 lines per tool call
var response = await _chatClient.GetResponseAsync<ConfluenceAssessment>(
    prompt,
    new ChatOptions { ResponseFormat = ChatResponseFormat.ForType<ConfluenceAssessment>() },
    ct);

return response.Result is { } assessment
    ? Result<ConfluenceAssessment>.Ok(assessment)
    : Result<ConfluenceAssessment>.Fail("LLM_DESERIALIZE_FAILED", "Structured output parsing failed");
```

### What changes in the typed response records:

Response records gain `[RegularExpression]` attributes from `System.ComponentModel.DataAnnotations` to constrain enum-like string values (note: `[JsonSchema]` does not exist in the BCL; use DataAnnotations for validation attributes):

```csharp
public sealed record ConfluenceAssessment(
    [property: JsonPropertyName("score")]
    [property: Description("Confluence quality score from 0.0 (poor) to 1.0 (excellent)")]
    decimal Score,

    [property: JsonPropertyName("recommendation")]
    [property: Description("Position sizing recommendation")]
    [property: RegularExpression("^(FULL_SIZE|HALF_SIZE|QUARTER_SIZE|SKIP)$")]
    string Recommendation,

    [property: JsonPropertyName("sizingMultiplier")]
    [property: Description("Numeric multiplier applied to base position size: 1.0 = full, 0.5 = half, 0.25 = quarter, 0.0 = skip")]
    decimal SizingMultiplier,

    [property: JsonPropertyName("alignedTimeframes")]
    IReadOnlyList<string> AlignedTimeframes,

    [property: JsonPropertyName("concerns")]
    IReadOnlyList<string> Concerns,

    [property: JsonPropertyName("notes")]
    string Notes
);
```

The `[Description]` and `[RegularExpression]` attributes become part of the schema sent to the LLM, providing the same constraint information previously in `.schema.json` files — but now co-located with the C# type definition and automatically kept in sync.

### Prompt template simplification:

Replace `{{token}}` substitution with C# string interpolation:

```csharp
// Before (FilePromptTemplateStore):
var template = await File.ReadAllTextAsync("prompts/confluence-score.md");
var prompt = template.Replace("{{direction}}", input.Direction)
                     .Replace("{{symbol}}", input.Symbol)
                     .Replace("{{indicator_table}}", RenderIndicatorTable(input.Snapshot));

// After (direct interpolation in the advisor class):
var prompt = $"""
    You are a multi-timeframe indicator confluence analyst for {input.Direction} signals.
    Symbol: {input.Symbol} | Timeframe: {input.AlertTimeframe} | Wave: {input.ElliottContext.WaveLabel}
    
    {RenderIndicatorTable(input.Snapshot)}
    
    Score the confluence quality from 0.0 to 1.0...
    """;
```

Benefits of C# interpolation over file-based templates:
- Compile-time validation of interpolated expressions
- IDE refactoring support (rename a property → prompt updates automatically)
- No injection risk from user data containing `{{` (C# interpolation is not recursive)
- No disk I/O per call
- Prompt versioning via git history (same as code) — no separate schema version tracking

> **Note**: Prompt files in `prompts/` are kept as **documentation and design artifacts** — not as runtime-loaded templates. The canonical prompt lives in the C# advisor class; the `.md` file is a human-readable specification.

---

## Consequences

### Positive
- **~300 lines of hand-rolled pipeline removed** per replaced tool (validation, schema loading, deserialization)
- **Schema always in sync with code**: `[Description]` attributes are the schema — if the record changes, the schema changes automatically
- **Compile-time type safety**: `GetResponseAsync<T>()` return type is `T`, not `string`
- **No disk deployment dependency**: `.schema.json` files no longer required at runtime
- **Eliminates `ExtractJsonFromResponse()`**: OpenAI structured output mode forces valid JSON from the model; no special-token cleanup needed (applies to Ollama when using structured output mode too)
- **Local LLM compatibility**: modern Ollama releases support `response_format: json_schema`; `Microsoft.Extensions.AI.Ollama` handles this transparently
- **Reduced error surface**: parse errors are `JsonException` (standard exception); no custom `parseError` string handling

### Negative / Trade-offs
- **Local LLM model compatibility**: older or smaller local models may not support structured output mode; `ExtractJsonFromResponse()` may need to remain as a fallback for incompatible models
  - Mitigation: feature flag `"LocalLlm:UseStructuredOutput": true/false`
  - Mitigation: `Microsoft.Extensions.AI` gracefully falls back to prompt-based JSON enforcement when model does not support `response_format`
- **`[Description]` attributes are not standard JSON Schema**: some edge cases (regex patterns, min/max values) require `[RegularExpression]` from `System.ComponentModel.DataAnnotations` — available in all .NET versions
- **Prompt files become documentation only**: engineers must remember to update both the C# interpolation AND the `.md` file when prompts change — risk of documentation drift
- **Test complexity**: mocking `IChatClient.GetResponseAsync<T>()` requires MEA test helpers or custom mock; less straightforward than mocking `IMcpGateway` (which remains unchanged)

### Neutral
- `IMcpGateway` interface is unchanged — all callers (AlertWorker) are unaffected
- `McpAdjudicationResult` is preserved for backward compat with `llm_adjudications` persistence
- `FilePromptTemplateStore` and `IMcpConfigStore` are removed; their functionality subsumes into advisor classes

---

## Implementation Notes

### NuGet package:
```xml
<!-- Already included from ADR-003 -->
<PackageReference Include="Microsoft.Extensions.AI" Version="10.*" />
<PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="10.*" />
```

### Local LLM compatibility check:
```csharp
// IOptions<LocalLlmOptions>
public class LocalLlmOptions
{
    [Required] public string BaseUrl { get; set; } = "";
    public bool UseStructuredOutput { get; set; } = true;  // set false for older models
    public string ModelOverride { get; set; } = "";
}
```

### Files to remove after migration:
- `src/Mvp.Trading.Api/Mcp/IJsonSchemaValidator.cs`
- `src/Mvp.Trading.Api/Mcp/JsonSchemaValidator.cs` (implementation)
- `src/Mvp.Trading.Api/Mcp/FilePromptTemplateStore.cs`
- `src/Mvp.Trading.Api/Mcp/IPromptTemplateStore.cs`
- `src/Mvp.Trading.Api/Mcp/IMcpConfigStore.cs`
- `src/Mvp.Trading.Api/Mcp/McpConfiguration.cs`
- `schemas/LlmDecision.schema.json` → archive to `docs/schemas-archive/`
- `schemas/StopLossSuggestion.schema.json` → archive

### Files to keep:
- `prompts/adjudicate-elliott.md` → archive to `docs/prompts-archive/` (historical reference)
- `prompts/explain-stoploss.md` → archive (replaced by `prompts/stop-loss-advice.md` as documentation)
- `prompts/confluence-score.md` → new file (documentation artifact)
- `prompts/stop-loss-advice.md` → new file (documentation artifact)

### Migration validation:
- For each replaced tool: capture 10 real LLM responses before migration
- Run same inputs through new `GetResponseAsync<T>()` pipeline
- Assert output equivalence (decision, score, reasoning)
- Confirms structured output produces same results as manual schema validation

---

## Alternatives Considered

| Alternative | Reason Rejected |
|-------------|----------------|
| Keep hand-rolled validation, just use MEA for HTTP | Partial improvement; loses structured output benefits; still 9-step pipeline |
| Use `Newtonsoft.Json.Schema` for runtime schema validation | Adds dependency; doesn't solve the schema-code sync problem; not .NET 10 idiomatic |
| Keep `.schema.json` files and validate with `System.Text.Json.Schema` | Keeps disk deployment dependency; schema still diverges from code |
| Use prompt-based JSON enforcement ("you must output only JSON matching this schema") | Less reliable than true structured output mode; LLMs occasionally add prose despite instructions |
| Source-generate the schema from C# types to `.schema.json` at build time | Complex build pipeline; still needs `IJsonSchemaValidator`; MEA does this natively at runtime |
