namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// Configuration for OpenAI Responses API access.
/// </summary>
public sealed class OpenAiOptions
{
    /// <summary>
    /// API key for OpenAI requests.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Base URL for OpenAI API calls.
    /// </summary>
    public string BaseUrl { get; init; } = "https://api.openai.com/v1/";

    /// <summary>
    /// Optional organization header for OpenAI calls.
    /// </summary>
    public string? Organization { get; init; }

    /// <summary>
    /// Optional project header for OpenAI calls.
    /// </summary>
    public string? Project { get; init; }
}
