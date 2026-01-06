using System.Collections.Generic;

namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// File-backed MCP configuration for tool settings and schema versions.
/// </summary>
public sealed record McpConfiguration(
    McpToolRegistry Mcp,
    McpSchemaVersions SchemaVersions);

/// <summary>
/// MCP tool registry loaded from configuration.
/// </summary>
public sealed record McpToolRegistry(
    Dictionary<string, McpToolConfig> Tools);

/// <summary>
/// Settings for a single MCP tool invocation.
/// </summary>
public sealed record McpToolConfig(
    string Model,
    decimal Temperature,
    int MaxOutputTokens);

/// <summary>
/// Schema versions for LLM payload validation.
/// </summary>
public sealed record McpSchemaVersions(
    string LlmDecision,
    string StopLossSuggestion);
