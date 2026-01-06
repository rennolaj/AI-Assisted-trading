namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// Request data for OpenAI Responses API.
/// </summary>
public sealed record OpenAiResponsesRequest(
    string Model,
    string Prompt,
    decimal Temperature,
    int MaxOutputTokens,
    string SchemaName,
    string SchemaFileName);
