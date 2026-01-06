namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// Configuration for selecting the MCP LLM provider.
/// </summary>
public sealed class McpProviderOptions
{
    /// <summary>
    /// Provider name (openai, local, auto).
    /// </summary>
    public string Provider { get; init; } = "openai";

    /// <summary>
    /// When enabled, fall back to local LLM if OpenAI returns a 429.
    /// </summary>
    public bool FallbackOnOpenAi429 { get; init; } = true;

    /// <summary>
    /// When enabled, force an ALLOW decision for demo E2E runs.
    /// </summary>
    public bool ForceAllow { get; init; }
}
