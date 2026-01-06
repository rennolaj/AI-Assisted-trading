using Mvp.Trading.Contracts;

namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// Provides the active risk policy for LLM adjudication.
/// </summary>
public interface IPolicyStore
{
    RiskPolicy GetRiskPolicy();
}
