using Mvp.Trading.Contracts;

namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// Gateway abstraction for MCP tool invocation.
/// </summary>
public interface IMcpGateway
{
    /// <summary>
    /// Adjudicate Elliott wave candidates with full observability context.
    /// Returns complete LLM interaction details including prompt, response, timing, and tokens.
    /// </summary>
    Task<Result<McpAdjudicationResult>> AdjudicateElliottAsync(ElliottAdjudicationInput input, CancellationToken ct);

    Task<Result<StopLossSuggestion>> ExplainStopLossAsync(StopLossExplainInput input, CancellationToken ct);
}
