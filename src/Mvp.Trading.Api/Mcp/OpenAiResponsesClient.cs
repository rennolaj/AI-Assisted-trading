using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// OpenAI Responses API client with JSON schema response format.
/// </summary>
public sealed class OpenAiResponsesClient : IOpenAiResponsesClient
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;
    private readonly ConcurrentDictionary<string, JsonNode> _schemaCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _schemaRoot;

    public OpenAiResponsesClient(HttpClient httpClient, IOptions<OpenAiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _schemaRoot = Path.Combine(AppContext.BaseDirectory, "schemas");
    }

    public async Task<Result<string>> CreateResponseAsync(OpenAiResponsesRequest request, CancellationToken ct)
    {
        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Fail<string>("OPENAI_API_KEY_MISSING", "OpenAI API key is not configured.");
        }

        var schemaResult = LoadSchema(request.SchemaFileName);
        if (!schemaResult.Ok || schemaResult.Value is null)
        {
            return Fail<string>(
                schemaResult.Error?.Code ?? "OPENAI_SCHEMA_NOT_FOUND",
                schemaResult.Error?.Message ?? $"Schema file '{request.SchemaFileName}' was not found.",
                schemaResult.Error?.Meta);
        }

        var payload = BuildPayload(request, schemaResult.Value);
        using var message = new HttpRequestMessage(HttpMethod.Post, "responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (!string.IsNullOrWhiteSpace(_options.Organization))
        {
            message.Headers.Add("OpenAI-Organization", _options.Organization);
        }

        if (!string.IsNullOrWhiteSpace(_options.Project))
        {
            message.Headers.Add("OpenAI-Project", _options.Project);
        }

        message.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(message, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var details = TryExtractError(body) ?? Truncate(body, 2000);
            var errorMessage = $"OpenAI request failed with status {(int)response.StatusCode}.";
            if (!string.IsNullOrWhiteSpace(details))
            {
                errorMessage = $"{errorMessage} Details: {details}";
            }
            return Fail<string>(
                "OPENAI_REQUEST_FAILED",
                errorMessage,
                new Dictionary<string, string?>
                {
                    ["statusCode"] = ((int)response.StatusCode).ToString(),
                    ["details"] = details
                });
        }

        var output = TryExtractOutputText(body);
        if (string.IsNullOrWhiteSpace(output))
        {
            return Fail<string>("OPENAI_OUTPUT_MISSING", "OpenAI response did not include output text.");
        }

        return new Result<string>(true, output, null);
    }

    private static Result<T> Fail<T>(string code, string message, IReadOnlyDictionary<string, string?>? meta = null)
    {
        return new Result<T>(false, default, new Error(code, message, meta));
    }

    private Result<JsonNode> LoadSchema(string schemaFileName)
    {
        if (string.IsNullOrWhiteSpace(schemaFileName))
        {
            return Fail<JsonNode>("OPENAI_SCHEMA_INVALID", "Schema file name is required.");
        }

        if (_schemaCache.TryGetValue(schemaFileName, out var cached))
        {
            return new Result<JsonNode>(true, cached, null);
        }

        var path = Path.Combine(_schemaRoot, schemaFileName);
        if (!File.Exists(path))
        {
            return Fail<JsonNode>("OPENAI_SCHEMA_NOT_FOUND", $"Schema file '{schemaFileName}' was not found.");
        }

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path));
            if (node is null)
            {
                return Fail<JsonNode>("OPENAI_SCHEMA_INVALID", $"Schema file '{schemaFileName}' is empty.");
            }

            _schemaCache.TryAdd(schemaFileName, node);
            return new Result<JsonNode>(true, node, null);
        }
        catch (JsonException)
        {
            return Fail<JsonNode>("OPENAI_SCHEMA_INVALID", $"Schema file '{schemaFileName}' is not valid JSON.");
        }
    }

    private static JsonObject BuildPayload(OpenAiResponsesRequest request, JsonNode schema)
    {
        return new JsonObject
        {
            ["model"] = request.Model,
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

    private string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return _options.ApiKey;
        }

        return Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
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

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
