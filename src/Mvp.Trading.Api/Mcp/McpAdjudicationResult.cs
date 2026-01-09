using Mvp.Trading.Contracts;

namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// Complete result from LLM adjudication including timing, tokens, and error tracking.
/// Wraps the parsed LlmDecision with observability metadata.
/// </summary>
public sealed class McpAdjudicationResult
{
    /// <summary>
    /// Full prompt sent to LLM (includes system message + user message).
    /// </summary>
    public required string PromptSent { get; init; }
    
    /// <summary>
    /// Raw response text from LLM before JSON parsing.
    /// </summary>
    public required string RawResponse { get; init; }
    
    /// <summary>
    /// Parsed LLM decision (may have fallback values if parsing failed).
    /// </summary>
    public required LlmDecision Decision { get; init; }
    
    /// <summary>
    /// LLM provider name (e.g., "openai", "local").
    /// </summary>
    public required string Provider { get; init; }
    
    /// <summary>
    /// LLM model name (e.g., "gpt-4", "llama-3.1-8b").
    /// </summary>
    public string? Model { get; init; }
    
    /// <summary>
    /// Total duration of LLM call in milliseconds.
    /// </summary>
    public int? DurationMs { get; init; }
    
    /// <summary>
    /// Number of tokens in the prompt.
    /// </summary>
    public int? PromptTokens { get; init; }
    
    /// <summary>
    /// Number of tokens in the completion/response.
    /// </summary>
    public int? CompletionTokens { get; init; }
    
    /// <summary>
    /// Total tokens used (prompt + completion).
    /// </summary>
    public int? TotalTokens { get; init; }
    
    /// <summary>
    /// Error message if JSON parsing failed.
    /// </summary>
    public string? ParseError { get; init; }
    
    /// <summary>
    /// Schema validation errors as JSON string.
    /// </summary>
    public string? ValidationErrors { get; init; }
}
