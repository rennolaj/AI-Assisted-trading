using System.Text.Json;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// MCP gateway backed by OpenAI Responses API.
/// </summary>
public sealed class OpenAiMcpGateway : IMcpGateway
{
    private const string AdjudicateTool = "adjudicateElliott";
    private const string ExplainStopLossTool = "explainStopLoss";
    private const string LlmDecisionSchemaFile = "LlmDecision.schema.json";
    private const string StopLossSchemaFile = "StopLossSuggestion.schema.json";
    private static readonly JsonSerializerOptions InputJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly JsonSerializerOptions OutputJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IOpenAiResponsesClient _client;
    private readonly IMcpConfigStore _configStore;
    private readonly IPolicyStore _policyStore;
    private readonly IPromptTemplateStore _promptStore;
    private readonly IJsonSchemaValidator _schemaValidator;

    public OpenAiMcpGateway(
        IOpenAiResponsesClient client,
        IMcpConfigStore configStore,
        IPolicyStore policyStore,
        IPromptTemplateStore promptStore,
        IJsonSchemaValidator schemaValidator)
    {
        _client = client;
        _configStore = configStore;
        _policyStore = policyStore;
        _promptStore = promptStore;
        _schemaValidator = schemaValidator;
    }

    public async Task<Result<McpAdjudicationResult>> AdjudicateElliottAsync(ElliottAdjudicationInput input, CancellationToken ct)
    {
        var policy = _policyStore.GetRiskPolicy();
        var normalizedInput = new ElliottAdjudicationInput(input.Direction, input.Snapshot, input.Candidates, policy);
        var inputJson = JsonSerializer.Serialize(normalizedInput, InputJsonOptions);
        var prompt = _promptStore.RenderAdjudicateElliottPrompt(inputJson);

        return await InvokeWithContextAsync(
            AdjudicateTool,
            prompt,
            LlmDecisionSchemaFile,
            schemaVersion: _configStore.GetConfig().SchemaVersions.LlmDecision,
            payload => JsonSerializer.Deserialize<LlmDecision>(payload, OutputJsonOptions),
            ct);
    }

    public async Task<Result<StopLossSuggestion>> ExplainStopLossAsync(StopLossExplainInput input, CancellationToken ct)
    {
        var policy = _policyStore.GetRiskPolicy();
        var normalizedInput = new StopLossExplainInput(input.Side, input.ChosenCandidate, input.Snapshot, policy);
        var inputJson = JsonSerializer.Serialize(normalizedInput, InputJsonOptions);
        var prompt = _promptStore.RenderExplainStopLossPrompt(inputJson);

        return await InvokeAsync(
            ExplainStopLossTool,
            prompt,
            StopLossSchemaFile,
            schemaVersion: _configStore.GetConfig().SchemaVersions.StopLossSuggestion,
            payload => JsonSerializer.Deserialize<StopLossSuggestion>(payload, OutputJsonOptions),
            ct);
    }

    private async Task<Result<McpAdjudicationResult>> InvokeWithContextAsync(
        string toolName,
        string prompt,
        string schemaFileName,
        string schemaVersion,
        Func<string, LlmDecision?> deserialize,
        CancellationToken ct)
    {
        var startTime = DateTimeOffset.UtcNow;
        string? rawResponse = null;
        string? parseError = null;
        string? validationErrors = null;
        LlmDecision? decision = null;

        try
        {
            var toolConfig = _configStore.GetToolConfig(toolName);
            if (toolConfig is null)
            {
                return Fail<McpAdjudicationResult>("MCP_TOOL_CONFIG_MISSING", $"Tool configuration '{toolName}' was not found.");
            }

            var schemaName = SanitizeSchemaName($"{toolName}-v{schemaVersion}");
            var response = await _client.CreateResponseAsync(new OpenAiResponsesRequest(
                toolConfig.Model,
                prompt,
                toolConfig.Temperature,
                toolConfig.MaxOutputTokens,
                schemaName,
                schemaFileName), ct);

            var durationMs = (int)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds;

            rawResponse = response.Value ?? string.Empty;

            if (!response.Ok || string.IsNullOrWhiteSpace(response.Value))
            {
                parseError = response.Error?.Message ?? "Upstream MCP response failed.";
                decision = new LlmDecision("REJECT", 0, null, "NONE", parseError);
                
                return new Result<McpAdjudicationResult>(true, new McpAdjudicationResult
                {
                    PromptSent = prompt,
                    RawResponse = rawResponse,
                    Decision = decision,
                    Provider = "openai",
                    Model = toolConfig.Model,
                    DurationMs = durationMs,
                    ParseError = parseError
                }, null);
            }

            var validation = _schemaValidator.Validate(schemaFileName, response.Value);
            if (!validation.Ok)
            {
                validationErrors = JsonSerializer.Serialize(new { error = validation.Error?.Message ?? "Schema validation failed" });
                decision = new LlmDecision("REJECT", 0, null, "NONE", validation.Error?.Message ?? "Schema validation failed");
                
                return new Result<McpAdjudicationResult>(true, new McpAdjudicationResult
                {
                    PromptSent = prompt,
                    RawResponse = rawResponse,
                    Decision = decision,
                    Provider = "openai",
                    Model = toolConfig.Model,
                    DurationMs = durationMs,
                    ValidationErrors = validationErrors
                }, null);
            }

            decision = deserialize(response.Value);
            if (decision is null)
            {
                parseError = "Failed to deserialize LLM payload.";
                decision = new LlmDecision("REJECT", 0, null, "NONE", parseError);
            }

            return new Result<McpAdjudicationResult>(true, new McpAdjudicationResult
            {
                PromptSent = prompt,
                RawResponse = rawResponse,
                Decision = decision,
                Provider = "openai",
                Model = toolConfig.Model,
                DurationMs = durationMs,
                ParseError = parseError,
                ValidationErrors = validationErrors
            }, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var durationMs = (int)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
            parseError = ex.Message;
            decision = new LlmDecision("REJECT", 0, null, "NONE", $"Gateway error: {ex.Message}");
            
            return new Result<McpAdjudicationResult>(true, new McpAdjudicationResult
            {
                PromptSent = prompt,
                RawResponse = rawResponse ?? string.Empty,
                Decision = decision,
                Provider = "openai",
                DurationMs = durationMs,
                ParseError = parseError
            }, null);
        }
    }

    private async Task<Result<T>> InvokeAsync<T>(
        string toolName,
        string prompt,
        string schemaFileName,
        string schemaVersion,
        Func<string, T?> deserialize,
        CancellationToken ct)
    {
        try
        {
            var toolConfig = _configStore.GetToolConfig(toolName);
            if (toolConfig is null)
            {
                return Fail<T>("MCP_TOOL_CONFIG_MISSING", $"Tool configuration '{toolName}' was not found.");
            }

            var schemaName = SanitizeSchemaName($"{toolName}-v{schemaVersion}");
            var response = await _client.CreateResponseAsync(new OpenAiResponsesRequest(
                toolConfig.Model,
                prompt,
                toolConfig.Temperature,
                toolConfig.MaxOutputTokens,
                schemaName,
                schemaFileName), ct);

            if (!response.Ok || string.IsNullOrWhiteSpace(response.Value))
            {
                return Fail<T>(
                    response.Error?.Code ?? "MCP_UPSTREAM_ERROR",
                    response.Error?.Message ?? "Upstream MCP response failed.",
                    response.Error?.Meta);
            }

            var validation = _schemaValidator.Validate(schemaFileName, response.Value);
            if (!validation.Ok)
            {
                return Fail<T>(
                    validation.Error?.Code ?? "MCP_SCHEMA_INVALID",
                    validation.Error?.Message ?? "Schema validation failed.",
                    validation.Error?.Meta);
            }

            var payload = deserialize(response.Value);
            if (payload is null)
            {
                return Fail<T>("MCP_DESERIALIZE_FAILED", "Failed to deserialize LLM payload.");
            }

            return new Result<T>(true, payload, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail<T>("MCP_GATEWAY_ERROR", ex.Message, new Dictionary<string, string?>
            {
                ["exception"] = ex.GetType().Name
            });
        }
    }

    private static string SanitizeSchemaName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "schema";
        }

        var buffer = new char[value.Length];
        var length = 0;

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
            {
                buffer[length++] = ch;
            }
            else
            {
                buffer[length++] = '_';
            }
        }

        return new string(buffer, 0, length);
    }

    private static Result<T> Fail<T>(string code, string message, IReadOnlyDictionary<string, string?>? meta = null)
    {
        return new Result<T>(false, default, new Error(code, message, meta));
    }
}
