using Mvp.Trading.Contracts;

namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// Gateway abstraction for MCP tool invocation.
/// </summary>
public interface IMcpGateway
{
    Task<Result<LlmDecision>> AdjudicateElliottAsync(ElliottAdjudicationInput input, CancellationToken ct);

    Task<Result<StopLossSuggestion>> ExplainStopLossAsync(StopLossExplainInput input, CancellationToken ct);
}
