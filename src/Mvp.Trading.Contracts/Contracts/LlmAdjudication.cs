using System;

namespace Mvp.Trading.Contracts.Contracts;

/// <summary>
/// Complete LLM adjudication interaction record.
/// Stores full context of LLM decision for debugging and analysis.
/// </summary>
public sealed class LlmAdjudication
{
    public Guid AdjudicationId { get; init; }
    public Guid AlertId { get; init; }
    public Guid CorrelationId { get; init; }
    
    // Request
    public required string PromptText { get; init; }
    public int? PromptTokens { get; init; }
    
    // Response
    public required string RawResponse { get; init; }
    public int? CompletionTokens { get; init; }
    public int? TotalTokens { get; init; }
    
    // Parsed Decision
    public required string Decision { get; init; }
    public required string Reasoning { get; init; }
    public decimal? Confidence { get; init; }
    
    // Metadata
    public required string LlmProvider { get; init; }
    public string? LlmModel { get; init; }
    public int? ResponseTimeMs { get; init; }
    public DateTimeOffset AdjudicatedAtUtc { get; init; }
    
    // Errors
    public string? ParseError { get; init; }
    public string? ValidationErrors { get; init; } // JSON string
}
