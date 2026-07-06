# M5 Status Snapshot

## Scope (M5)
- Embedded MCP gateway boundary
- MCP tools: adjudicateElliott + explainStopLoss
- OpenAI Responses integration with strict schema validation
- Provider routing (OpenAI/local) with optional fallback
- File-based policy/config/prompt loading
- Orchestration tests + fail-closed matrix

## Current Implementation
- Gateway + client:
  - `src/Mvp.Trading.Api/Mcp/IMcpGateway.cs`
  - `src/Mvp.Trading.Api/Mcp/OpenAiMcpGateway.cs`
  - `src/Mvp.Trading.Api/Mcp/OpenAiResponsesClient.cs`
  - `src/Mvp.Trading.Api/Mcp/LocalLlmMcpGateway.cs`
  - `src/Mvp.Trading.Api/Mcp/LocalLlmResponsesClient.cs`
  - `src/Mvp.Trading.Api/Mcp/McpGatewayRouter.cs`
  - `src/Mvp.Trading.Api/Mcp/JsonSchemaValidator.cs`
  - `src/Mvp.Trading.Api/Mcp/OpenAiOptions.cs`
  - `src/Mvp.Trading.Api/Mcp/LocalLlmOptions.cs`
  - `src/Mvp.Trading.Api/Mcp/McpProviderOptions.cs`
- File-based config/policy/prompts:
  - `config/mcp.json`
  - `config/risk-policy.json`
  - `prompts/adjudicate-elliott.md`
  - `prompts/explain-stoploss.md`
- Schemas:
  - `schemas/LlmDecision.schema.json`
  - `schemas/StopLossSuggestion.schema.json`
- DI wiring:
  - `src/Mvp.Trading.Api/Program.cs`
  - `src/Mvp.Trading.Worker/Program.cs`
- Worker adjudication gate:
  - `src/Mvp.Trading.Worker/AlertWorker.cs`
  - On MCP error -> status `adjudication_failed`
  - On non-ALLOW decision -> status `rejected`
  - On ALLOW -> continue to `succeeded`
- Tests:
  - `tests/Mvp.Trading.Api.Tests/OpenAiMcpGatewayTests.cs`
  - `tests/Mvp.Trading.Indicators.Tests/ElliottPipelineIntegrationTests.cs` (updated with MCP stubs)

## Runtime Configuration (M5)
- Environment variables:
  - `OpenAI__ApiKey`
  - `OpenAI__BaseUrl` (optional)
  - `OpenAI__Organization` (optional)
  - `OpenAI__Project` (optional)
  - `McpProvider__Provider` (openai, local, auto)
  - `McpProvider__FallbackOnOpenAi429`
  - `LocalLlm__BaseUrl`
  - `LocalLlm__ApiKey` (optional)
  - `LocalLlm__ResponsesPath`
  - `LocalLlm__ModelOverride` (optional)
- Docker:
  - `docker-compose.yml` includes `OpenAI__ApiKey` for API + worker
- Local env:
  - `.env` (gitignored) should include `OPENAI_API_KEY`

## Open Items / Risks
- No feature flag to bypass MCP (MCP is enforced by worker).
- No end-to-end test using real OpenAI network calls (intentional).
- No explicit persistence of MCP decisions yet (only status + error message).
- Local LLM must expose an OpenAI-compatible Responses API (or add an adapter).

## Suggested Next Steps (Post-M5)
1) Add a feature flag for MCP enforcement (allow dry-run).
2) Persist LLM decision payloads for audit trail (new table + API query).
3) Wire MCP decision into M6 risk engine and trade plan generation.
4) Add docs for MCP flow and decision outcomes in `README.md`.
