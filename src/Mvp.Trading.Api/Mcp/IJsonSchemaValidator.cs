using Mvp.Trading.Contracts;

namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// Validates JSON payloads against stored JSON schemas.
/// </summary>
public interface IJsonSchemaValidator
{
    Result<bool> Validate(string schemaFileName, string json);
}
