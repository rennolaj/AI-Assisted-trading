using Mvp.Trading.Contracts;

namespace Mvp.Trading.Risk;

/// <summary>
/// Builds deterministic trade plans from LLM decisions and risk constraints.
/// </summary>
public interface ITradePlanBuilder
{
    Result<TradePlan> BuildPlan(TradePlanContext context);
}
