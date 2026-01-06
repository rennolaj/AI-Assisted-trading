using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// Local LLM Responses API client with JSON schema response format.
/// </summary>
public sealed class LocalLlmResponsesClient : ILocalLlmResponsesClient
{
    private readonly HttpClient _httpClient;
    private readonly LocalLlmOptions _options;
    private readonly ConcurrentDictionary<string, JsonNode> _schemaCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _schemaRoot;

    public LocalLlmResponsesClient(HttpClient httpClient, IOptions<LocalLlmOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _schemaRoot = Path.Combine(AppContext.BaseDirectory, "schemas");
    }

    public async Task<Result<string>> CreateResponseAsync(OpenAiResponsesRequest request, CancellationToken ct)
    {
        var schemaResult = LoadSchema(request.SchemaFileName);
        if (!schemaResult.Ok || schemaResult.Value is null)
        {
            return Fail<string>(
                schemaResult.Error?.Code ?? "LOCAL_LLM_SCHEMA_NOT_FOUND",
                schemaResult.Error?.Message ?? $"Schema file '{request.SchemaFileName}' was not found.",
                schemaResult.Error?.Meta);
        }

        return IsChatMode()
            ? await CreateChatResponseAsync(request, schemaResult.Value, ct)
            : await CreateResponsesResponseAsync(request, schemaResult.Value, ct);
    }

    private static Result<T> Fail<T>(string code, string message, IReadOnlyDictionary<string, string?>? meta = null)
    {
        return new Result<T>(false, default, new Error(code, message, meta));
    }

    private Result<JsonNode> LoadSchema(string schemaFileName)
    {
        if (string.IsNullOrWhiteSpace(schemaFileName))
        {
            return Fail<JsonNode>("LOCAL_LLM_SCHEMA_INVALID", "Schema file name is required.");
        }

        if (_schemaCache.TryGetValue(schemaFileName, out var cached))
        {
            return new Result<JsonNode>(true, cached, null);
        }

        var path = Path.Combine(_schemaRoot, schemaFileName);
        if (!File.Exists(path))
        {
            return Fail<JsonNode>("LOCAL_LLM_SCHEMA_NOT_FOUND", $"Schema file '{schemaFileName}' was not found.");
        }

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path));
            if (node is null)
            {
                return Fail<JsonNode>("LOCAL_LLM_SCHEMA_INVALID", $"Schema file '{schemaFileName}' is empty.");
            }

            _schemaCache.TryAdd(schemaFileName, node);
            return new Result<JsonNode>(true, node, null);
        }
        catch (JsonException)
        {
            return Fail<JsonNode>("LOCAL_LLM_SCHEMA_INVALID", $"Schema file '{schemaFileName}' is not valid JSON.");
        }
    }

    private async Task<Result<string>> CreateResponsesResponseAsync(
        OpenAiResponsesRequest request,
        JsonNode schema,
        CancellationToken ct)
    {
        var model = ResolveModel(request.Model);
        var payload = BuildResponsesPayload(request, schema, model);
        return await SendRequestAsync(
            path: NormalizePath(_options.ResponsesPath, "responses"),
            payload: payload,
            outputExtractor: TryExtractOutputText,
            missingOutputCode: "LOCAL_LLM_OUTPUT_MISSING",
            missingOutputMessage: "Local LLM response did not include output text.",
            ct);
    }

    private async Task<Result<string>> CreateChatResponseAsync(
        OpenAiResponsesRequest request,
        JsonNode schema,
        CancellationToken ct)
    {
        var model = ResolveModel(request.Model);
        var payload = BuildChatPayload(request, schema, model, _options.UseResponseFormat);
        return await SendRequestAsync(
            path: NormalizePath(_options.ChatCompletionsPath, "chat/completions"),
            payload: payload,
            outputExtractor: TryExtractChatContent,
            missingOutputCode: "LOCAL_LLM_OUTPUT_MISSING",
            missingOutputMessage: "Local LLM response did not include chat content.",
            ct);
    }

    private async Task<Result<string>> SendRequestAsync(
        string path,
        JsonObject payload,
        Func<string, string?> outputExtractor,
        string missingOutputCode,
        string missingOutputMessage,
        CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path);
        var apiKey = ResolveApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        message.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(message, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var details = TryExtractError(body) ?? Truncate(body, 2000);
            var errorMessage = $"Local LLM request failed with status {(int)response.StatusCode}.";
            if (!string.IsNullOrWhiteSpace(details))
            {
                errorMessage = $"{errorMessage} Details: {details}";
            }

            return Fail<string>(
                "LOCAL_LLM_REQUEST_FAILED",
                errorMessage,
                new Dictionary<string, string?>
                {
                    ["statusCode"] = ((int)response.StatusCode).ToString(),
                    ["details"] = details
                });
        }

        var output = outputExtractor(body) ?? TryExtractOutputText(body);
        if (string.IsNullOrWhiteSpace(output))
        {
            return Fail<string>(missingOutputCode, missingOutputMessage);
        }

        return new Result<string>(true, output, null);
    }

    private static JsonObject BuildResponsesPayload(OpenAiResponsesRequest request, JsonNode schema, string model)
    {
        return new JsonObject
        {
            ["model"] = model,
            ["input"] = request.Prompt,
            ["temperature"] = (double)request.Temperature,
            ["max_output_tokens"] = request.MaxOutputTokens,
            ["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = request.SchemaName,
                    ["schema"] = schema.DeepClone(),
                    ["strict"] = true
                }
            }
        };
    }

    private static JsonObject BuildChatPayload(
        OpenAiResponsesRequest request,
        JsonNode schema,
        string model,
        bool useResponseFormat)
    {
        var systemPrompt = $"Return only JSON that matches this schema:\\n{schema.ToJsonString()}";
        var payload = new JsonObject
        {
            ["model"] = model,
            ["temperature"] = (double)request.Temperature,
            ["max_tokens"] = request.MaxOutputTokens,
            ["stream"] = false,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = systemPrompt
                },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = request.Prompt
                }
            }
        };

        if (useResponseFormat)
        {
            var sanitizedSchema = SanitizeSchemaForResponseFormat(schema.DeepClone());
            payload["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = request.SchemaName,
                    ["schema"] = sanitizedSchema,
                    ["strict"] = true
                }
            };
        }

        return payload;
    }

    private string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return _options.ApiKey;
        }

        return Environment.GetEnvironmentVariable("LOCAL_LLM_API_KEY") ?? string.Empty;
    }

    private string ResolveModel(string fallback)
    {
        return string.IsNullOrWhiteSpace(_options.ModelOverride) ? fallback : _options.ModelOverride;
    }

    private static string NormalizePath(string? path, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(path) ? fallback : path;
        return value.StartsWith('/') ? value[1..] : value;
    }

    private bool IsChatMode()
    {
        return string.Equals(_options.Mode, "chat", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryExtractOutputText(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
            {
                return outputText.GetString();
            }

            if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
            {
                foreach (var outputItem in output.EnumerateArray())
                {
                    if (!outputItem.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var contentItem in content.EnumerateArray())
                    {
                        if (contentItem.TryGetProperty("type", out var type) &&
                            type.ValueKind == JsonValueKind.String &&
                            string.Equals(type.GetString(), "output_text", StringComparison.OrdinalIgnoreCase) &&
                            contentItem.TryGetProperty("text", out var text) &&
                            text.ValueKind == JsonValueKind.String)
                        {
                            return text.GetString();
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string? TryExtractError(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                if (error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                {
                    return message.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string? TryExtractChatContent(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.TryGetProperty("message", out var message) &&
                    message.ValueKind == JsonValueKind.Object &&
                    message.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.String)
                {
                    return content.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static JsonNode SanitizeSchemaForResponseFormat(JsonNode schema)
    {
        if (schema is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("type", out var typeNode) && typeNode is JsonArray typeArray)
            {
                string? primaryType = null;
                foreach (var item in typeArray)
                {
                    if (item is JsonValue value && value.TryGetValue<string>(out var typeValue))
                    {
                        if (!string.Equals(typeValue, "null", StringComparison.OrdinalIgnoreCase))
                        {
                            primaryType = typeValue;
                            break;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(primaryType))
                {
                    obj["type"] = primaryType;
                }
            }

            foreach (var entry in obj)
            {
                if (entry.Value is not null)
                {
                    SanitizeSchemaForResponseFormat(entry.Value);
                }
            }
        }
        else if (schema is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null)
                {
                    SanitizeSchemaForResponseFormat(item);
                }
            }
        }

        return schema;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
