# ADR-003: Adopt Microsoft.Extensions.AI for LLM Provider Abstraction

| Field | Value |
|-------|-------|
| **ID** | ADR-003 |
| **Date** | 2026-05-15 |
| **Status** | PROPOSED |
| **Milestone** | M17 |
| **Author** | Analysis: GitHub Copilot CLI session 2026-05-15 |
| **Supersedes** | — |

---

## Context

The current LLM integration is built on a hand-rolled abstraction layer:

```
IMcpGateway
  └─ McpGatewayRouter           (provider routing: openai | local | auto)
       ├─ OpenAiMcpGateway      (OpenAI Responses API client, ~280 lines)
       └─ LocalLlmMcpGateway    (OpenAI-compatible local LLM client, ~356 lines)
            └─ ExtractJsonFromResponse()  (special-token cleanup for local models)

Supporting infrastructure:
  IOpenAiResponsesClient        (hand-rolled HTTP client wrapper)
  ILocalLlmResponsesClient      (hand-rolled HTTP client wrapper)
  IJsonSchemaValidator          (hand-rolled JSON schema validation against .schema.json files)
  IPromptTemplateStore          (file-based prompt template loading + {{token}} substitution)
  FilePromptTemplateStore       (reads from prompts/*.md)
  IMcpConfigStore               (reads from mcp-config.json)
```

**Total: ~900 lines of hand-rolled LLM infrastructure code** that reimplements what `Microsoft.Extensions.AI` provides out of the box.

Microsoft introduced `Microsoft.Extensions.AI` (NuGet: `Microsoft.Extensions.AI`) as the .NET standard abstraction for AI services, published stable releases starting in .NET 9 / .NET 10 timeframe. It provides:

- `IChatClient` — provider-agnostic chat interface (OpenAI, Azure OpenAI, Ollama, any OpenAI-compatible endpoint)
- `GetResponseAsync<T>()` — structured JSON output with compile-time type safety (see ADR-008)
- Built-in middleware pipeline: logging, distributed caching, retry, telemetry
- `FunctionInvokingChatClient` — automatic MCP tool invocation
- `UseDistributedCache()` — request deduplication and response caching
- `UseLogging()` — structured logging of all LLM calls (prompt + response + tokens + duration)
- Provider packages: `Microsoft.Extensions.AI.OpenAI`, `Microsoft.Extensions.AI.Ollama`

**The hand-rolled infrastructure duplicates all of this, with less reliability and more maintenance burden.**

### Current pain points in the hand-rolled layer:

| Pain Point | Impact |
|-----------|--------|
| `ExtractJsonFromResponse()` in `LocalLlmMcpGateway` — brittle string parsing for special tokens | Local LLM model updates can break this silently |
| No retry built in — transient HTTP 5xx fails immediately | M14.5.5 tracking this as CRITICAL |
| No response caching — identical inputs call the LLM every time | Token waste; latency on repeated scenarios |
| `DateTimeOffset.UtcNow` used directly (M14 PERF-1) | Not testable with `TimeProvider` |
| `string.Substring()` instead of range syntax (M14 anti-pattern) | Minor but consistent with tech debt |
| Schema validation requires `.schema.json` files on disk | Deployment coupling; errors only at runtime |
| Provider routing logic duplicated in both gateways | DRY violation; bug must be fixed in two places |

---

## Decision

**Replace the hand-rolled gateway infrastructure with `Microsoft.Extensions.AI` (`IChatClient`) as the provider abstraction layer.** The domain-level `IMcpGateway` interface is retained as a thin facade over `IChatClient`, keeping the domain API stable while the infrastructure changes underneath.

### Target architecture after this ADR:

```
IMcpGateway (domain interface — unchanged)
  └─ LlmAdvisoryGateway (new, replaces OpenAiMcpGateway + LocalLlmMcpGateway)
       └─ IChatClient (from Microsoft.Extensions.AI)
            ├─ UseLogging()          — structured logging built-in
            ├─ UseDistributedCache() — Redis-backed response caching
            └─ Provider: OpenAI | Ollama (configured via DI, not routing code)
```

### DI registration (replacing current McpGatewayRouter logic):

```csharp
// Program.cs (Api + Worker)
builder.Services
    .AddOpenAIChatClient(options =>
    {
        options.Endpoint = new Uri(config["OpenAi:BaseUrl"]!);
        options.ApiKey = config["OpenAi:ApiKey"]!;
        options.ModelId = config["OpenAi:Model"] ?? "gpt-4o";
    })
    .UseDistributedCache()   // IDistributedCache (Redis already registered)
    .UseLogging();           // structured logging via ILogger

// For local LLM fallback — register as named variant
builder.Services
    .AddOllamaChatClient(options =>
    {
        options.Endpoint = new Uri(config["LocalLlm:BaseUrl"]!);
        options.ModelId = config["LocalLlm:ModelOverride"] ?? "mistral";
    })
    .UseLogging();
```

### What is removed vs what is kept:

| Component | Action | Reason |
|-----------|--------|--------|
| `IOpenAiResponsesClient` | ❌ Remove | Replaced by `IChatClient` from MEA |
| `ILocalLlmResponsesClient` | ❌ Remove | Replaced by `IChatClient` with Ollama provider |
| `IJsonSchemaValidator` | ❌ Remove | Replaced by `GetResponseAsync<T>()` (ADR-008) |
| `OpenAiMcpGateway` | ❌ Remove | Replaced by `LlmAdvisoryGateway` |
| `LocalLlmMcpGateway` | ❌ Remove | Replaced by `LlmAdvisoryGateway` |
| `McpGatewayRouter` | ❌ Remove | Provider selection moves to DI config |
| `InProcessMcpGateway` | ❌ Remove | No longer needed (was placeholder) |
| `FilePromptTemplateStore` | ⚠️ Simplify | Keep for prompt loading; remove `{{token}}` substitution (use C# interpolation) |
| `IMcpConfigStore` | ⚠️ Simplify | Only keep schema version tracking; tool config moves to options |
| `IMcpGateway` | ✅ Keep | Domain interface is stable; callers do not change |
| `prompts/*.md` | ✅ Keep | Prompt files remain; loaded and formatted in C# |
| `schemas/*.schema.json` | ⚠️ Archive | Kept as documentation only; runtime validation replaced by MEA |

---

## Consequences

### Positive
- **~700 lines of hand-rolled infrastructure removed** — replaced by well-tested, maintained library code
- **Retry + resilience built in** — eliminates M14.5.5 (no HTTP resilience policies) for LLM clients
- **Distributed caching** — identical prompts served from Redis; no duplicate LLM calls (cost + latency)
- **Structured logging** — every LLM call logged automatically with prompt, response, tokens, duration (partial M9.7)
- **Provider portability** — swapping OpenAI → Azure OpenAI → Ollama → Anthropic is a DI config change, not a code change
- **Structured output** — `GetResponseAsync<T>()` eliminates manual JSON parsing (ADR-008)
- **Testability** — `IChatClient` is an interface; mock implementations available for unit tests
- **Standards alignment** — `Microsoft.Extensions.AI` is the .NET 10 standard; community support, documentation, and examples

### Negative / Trade-offs
- **Migration effort** — existing gateway code must be rewritten; ~2–4 days of work
- **New NuGet dependency** — `Microsoft.Extensions.AI`, `Microsoft.Extensions.AI.OpenAI`, `Microsoft.Extensions.AI.Ollama`
- **Learning curve** — MEA middleware pipeline is new to the codebase; team needs to understand `UseLogging().UseDistributedCache()` ordering
- **Local LLM special token handling** — `ExtractJsonFromResponse()` in `LocalLlmMcpGateway` handles model-specific response formatting; MEA structured output should make this unnecessary, but model compatibility must be verified

### Neutral
- All tests that mock `IMcpGateway` remain unaffected — the domain interface does not change
- `McpAdjudicationResult` contract is preserved for `llm_adjudications` persistence (M9.7)

---

## Implementation Notes

### NuGet packages to add:
```xml
<!-- Mvp.Trading.Api.csproj and Mvp.Trading.Worker.csproj -->
<PackageReference Include="Microsoft.Extensions.AI" Version="10.*" />
<PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="10.*" />
<PackageReference Include="Microsoft.Extensions.AI.Ollama" Version="10.*" />
```

### Migration order:
1. Add packages; keep existing gateways running in parallel
2. Implement `LlmAdvisoryGateway` using `IChatClient` + `GetResponseAsync<T>()`
3. Wire `LlmAdvisoryGateway` behind `IMcpGateway` interface
4. Test against demo LLM endpoints
5. Remove `OpenAiMcpGateway`, `LocalLlmMcpGateway`, `McpGatewayRouter`, hand-rolled clients
6. Remove `IJsonSchemaValidator` and schema file runtime loading

### Provider selection strategy:
Replace `McpProviderOptions.Provider` routing code with DI-keyed registration:
```csharp
// config: "McpProvider:Provider": "openai" | "local"
if (provider == "openai")
    services.AddSingleton<IMcpGateway, LlmAdvisoryGateway>(sp =>
        new LlmAdvisoryGateway(sp.GetRequiredKeyedService<IChatClient>("openai"), ...));
else
    services.AddSingleton<IMcpGateway, LlmAdvisoryGateway>(sp =>
        new LlmAdvisoryGateway(sp.GetRequiredKeyedService<IChatClient>("local"), ...));
```

### Caching strategy:
- Cache key: `SHA256(prompt)` — same indicator state + same candidates = same response
- TTL: 60 seconds — market conditions change; do not cache across bars
- Invalidation: Redis key expiry only; no manual invalidation needed

---

## Alternatives Considered

| Alternative | Reason Rejected |
|-------------|----------------|
| Keep hand-rolled gateways | Continues accumulating tech debt; misses retry, caching, logging; duplicates library features |
| Use Semantic Kernel instead of MEA | Semantic Kernel is higher-level orchestration; MEA is the correct abstraction for raw LLM client access; both can coexist, but MEA is the foundation |
| Use LangChain.NET | Less maintained, less idiomatic for .NET 10; not a Microsoft-first solution |
| Swap only one gateway at a time | Leaves routing complexity in place; better to migrate completely |
| Keep `McpGatewayRouter` and inject `IChatClient` inside existing gateways | Partial improvement; still leaves hand-rolled JSON validation and prompt rendering code |
