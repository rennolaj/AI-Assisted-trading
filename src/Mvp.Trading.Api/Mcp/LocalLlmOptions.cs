namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// Configuration for a local LLM with an OpenAI-compatible Responses API.
/// </summary>
public sealed class LocalLlmOptions
{
    /// <summary>
    /// Optional API key for the local LLM endpoint.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Base URL for the local LLM API calls.
    /// </summary>
    public string BaseUrl { get; init; } = "http://localhost:11434/v1/";

    /// <summary>
    /// Endpoint path for Responses API requests.
    /// </summary>
    public string ResponsesPath { get; init; } = "responses";

    /// <summary>
    /// Endpoint path for Chat Completions API requests.
    /// </summary>
    public string ChatCompletionsPath { get; init; } = "chat/completions";

    /// <summary>
    /// Provider mode for local LLM calls (responses or chat).
    /// </summary>
    public string Mode { get; init; } = "chat";

    /// <summary>
    /// When true, include response_format for chat completions (if supported).
    /// </summary>
    public bool UseResponseFormat { get; init; } = false;

    /// <summary>
    /// Optional model override for local LLM calls.
    /// </summary>
    public string? ModelOverride { get; init; }
}
