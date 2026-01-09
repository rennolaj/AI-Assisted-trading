using Mvp.Trading.Contracts;

namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// In-process MCP gateway placeholder for M5 scaffolding.
/// </summary>
public sealed class InProcessMcpGateway : IMcpGateway
{
    public Task<Result<McpAdjudicationResult>> AdjudicateElliottAsync(ElliottAdjudicationInput input, CancellationToken ct)
    {
        var error = new Error("MCP_NOT_READY", "MCP adjudication is not configured yet.", null);
        return Task.FromResult(new Result<McpAdjudicationResult>(false, null, error));
    }

    public Task<Result<StopLossSuggestion>> ExplainStopLossAsync(StopLossExplainInput input, CancellationToken ct)
    {
        var error = new Error("MCP_NOT_READY", "MCP stop-loss explanation is not configured yet.", null);
        return Task.FromResult(new Result<StopLossSuggestion>(false, null, error));
    }
}
