using System.Collections.Concurrent;
using System.Text.Json;
using Json.Schema;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// Uses JsonSchema.Net to enforce strict JSON schema validation.
/// </summary>
public sealed class JsonSchemaValidator : IJsonSchemaValidator
{
    private readonly string _schemaRoot;
    private readonly ConcurrentDictionary<string, JsonSchema> _schemas = new(StringComparer.OrdinalIgnoreCase);

    public JsonSchemaValidator()
    {
        _schemaRoot = Path.Combine(AppContext.BaseDirectory, "schemas");
    }

    public Result<bool> Validate(string schemaFileName, string json)
    {
        if (string.IsNullOrWhiteSpace(schemaFileName))
        {
            return Fail("MCP_SCHEMA_MISSING", "Schema file name is required.");
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return Fail("MCP_OUTPUT_EMPTY", "LLM output was empty.");
        }

        var schemaPath = Path.Combine(_schemaRoot, schemaFileName);
        if (!File.Exists(schemaPath))
        {
            return Fail("MCP_SCHEMA_NOT_FOUND", $"Schema file '{schemaFileName}' was not found.");
        }

        JsonSchema schema;
        try
        {
            schema = _schemas.GetOrAdd(schemaFileName, _ => JsonSchema.FromFile(schemaPath));
        }
        catch (Exception ex)
        {
            return Fail("MCP_SCHEMA_LOAD_FAILED", $"Schema file '{schemaFileName}' failed to load.", new Dictionary<string, string?>
            {
                ["exception"] = ex.GetType().Name
            });
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var result = schema.Evaluate(doc.RootElement);
            if (!result.IsValid)
            {
                return Fail("MCP_SCHEMA_INVALID", $"LLM output did not match schema '{schemaFileName}'.");
            }
        }
        catch (JsonException)
        {
            return Fail("MCP_OUTPUT_INVALID_JSON", "LLM output was not valid JSON.");
        }

        return new Result<bool>(true, true, null);
    }

    private static Result<bool> Fail(string code, string message, IReadOnlyDictionary<string, string?>? meta = null)
    {
        return new Result<bool>(false, false, new Error(code, message, meta));
    }
}
