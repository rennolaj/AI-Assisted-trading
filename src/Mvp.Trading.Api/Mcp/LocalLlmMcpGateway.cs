using System.Text.Json;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// MCP gateway backed by a local LLM with an OpenAI-compatible Responses API.
/// </summary>
public sealed class LocalLlmMcpGateway : IMcpGateway
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

    private readonly ILocalLlmResponsesClient _client;
    private readonly IMcpConfigStore _configStore;
    private readonly IPolicyStore _policyStore;
    private readonly IPromptTemplateStore _promptStore;
    private readonly IJsonSchemaValidator _schemaValidator;
    private readonly ILogger<LocalLlmMcpGateway> _logger;

    public LocalLlmMcpGateway(
        ILocalLlmResponsesClient client,
        IMcpConfigStore configStore,
        IPolicyStore policyStore,
        IPromptTemplateStore promptStore,
        IJsonSchemaValidator schemaValidator,
        ILogger<LocalLlmMcpGateway> logger)
    {
        _client = client;
        _configStore = configStore;
        _policyStore = policyStore;
        _promptStore = promptStore;
        _schemaValidator = schemaValidator;
        _logger = logger;
    }

    public async Task<Result<McpAdjudicationResult>> AdjudicateElliottAsync(ElliottAdjudicationInput input, CancellationToken ct)
    {
        var policy = _policyStore.GetRiskPolicy();
        
        // PRE-FILTER: Only send candidates without ERROR violations to reduce prompt size
        var validCandidates = input.Candidates.Candidates
            .Where(c => c.RuleViolations.All(v => v.Severity != "ERROR"))
            .ToList();
        
        // Log filtering and candidate details in a single log entry
        _logger.LogInformation(
            "DEBUG MCP: Filtered {TotalCount} candidates down to {ValidCount} without ERROR violations for LLM. Sending {CandidateCount} valid candidates to LLM. Direction={Direction}, Candidates={@Candidates}",
            input.Candidates.Candidates.Count,
            validCandidates.Count,
            validCandidates.Count,
            input.Direction,
            validCandidates.Select(c => new {
                c.CandidateId,
                c.WaveLabel,
                c.Score,
                c.Confidence,
                LongPrice = c.Invalidation.LongInvalidationPrice,
                ShortPrice = c.Invalidation.ShortInvalidationPrice
            }).ToList());
        
        // Create filtered input with only valid candidates
        var filteredCandidates = new ElliottCandidates(input.Candidates.BaseTimeframe, validCandidates);
        var normalizedInput = new ElliottAdjudicationInput(input.Direction, input.Snapshot, filteredCandidates, policy);
        
        var inputJson = JsonSerializer.Serialize(normalizedInput, InputJsonOptions);
        var prompt = _promptStore.RenderAdjudicateElliottPrompt(inputJson);
        
        // Debug: Log prompt being sent (truncated)
        var promptPreview = prompt.Length > 500 ? prompt.Substring(0, 500) + "..." : prompt;
        _logger.LogInformation(
            "DEBUG MCP: Sending prompt to LLM (length={Length} chars, est {TokenCount} tokens): {PromptPreview}",
            prompt.Length,
            prompt.Length / 4,  // Rough estimate: 4 chars per token
            promptPreview);

        var result = await InvokeWithContextAsync(
            AdjudicateTool,
            prompt,
            LlmDecisionSchemaFile,
            schemaVersion: _configStore.GetConfig().SchemaVersions.LlmDecision,
            payload => JsonSerializer.Deserialize<LlmDecision>(payload, OutputJsonOptions),
            ct);
            
        // Debug: Log result
        if (result.Ok && result.Value?.Decision != null)
        {
            _logger.LogInformation(
                "DEBUG MCP: LLM returned decision={Decision}, confidence={Confidence}, notes={Notes}",
                result.Value.Decision.Decision,
                result.Value.Decision.Confidence,
                result.Value.Decision.Notes);
        }
        else
        {
            _logger.LogWarning(
                "DEBUG MCP: LLM call failed or returned null. Error={Error}",
                result.Error?.Message ?? "Unknown");
        }
        
        return result;
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
            
            // Extract JSON from LocalLLM response that may contain special tokens like <|channel|>final <|constrain|>JSON<|message|>{...}
            var cleanedResponse = ExtractJsonFromResponse(rawResponse);

            if (!response.Ok || string.IsNullOrWhiteSpace(cleanedResponse))
            {
                parseError = response.Error?.Message ?? "Upstream MCP response failed.";
                decision = new LlmDecision("REJECT", 0, null, "NONE", parseError);
                
                return new Result<McpAdjudicationResult>(true, new McpAdjudicationResult
                {
                    PromptSent = prompt,
                    RawResponse = rawResponse,
                    Decision = decision,
                    Provider = "local",
                    Model = toolConfig.Model,
                    DurationMs = durationMs,
                    ParseError = parseError
                }, null);
            }

            var validation = _schemaValidator.Validate(schemaFileName, cleanedResponse);
            if (!validation.Ok)
            {
                validationErrors = JsonSerializer.Serialize(new { error = validation.Error?.Message ?? "Schema validation failed" });
                decision = new LlmDecision("REJECT", 0, null, "NONE", validation.Error?.Message ?? "Schema validation failed");
                
                return new Result<McpAdjudicationResult>(true, new McpAdjudicationResult
                {
                    PromptSent = prompt,
                    RawResponse = rawResponse,
                    Decision = decision,
                    Provider = "local",
                    Model = toolConfig.Model,
                    DurationMs = durationMs,
                    ValidationErrors = validationErrors
                }, null);
            }

            decision = deserialize(cleanedResponse);
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
                Provider = "local",
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
                Provider = "local",
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

    private static Result<T> Fail<T>(string code, string message, IReadOnlyDictionary<string, string?>? meta = null)
    {
        return new Result<T>(false, default, new Error(code, message, meta));
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

    private static string ExtractJsonFromResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return response;
        }

        // LocalLLM may add special tokens like: <|channel|>final <|constrain|>JSON<|message|>{actual json}
        // Find the first { and last } to extract the JSON
        var firstBrace = response.IndexOf('{');
        var lastBrace = response.LastIndexOf('}');

        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return response.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        return response;
    }
}
