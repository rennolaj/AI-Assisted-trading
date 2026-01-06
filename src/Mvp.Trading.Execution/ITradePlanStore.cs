using Mvp.Trading.Contracts;

namespace Mvp.Trading.Execution;

/// <summary>
/// Persists trade plans for audit and reconciliation.
/// </summary>
public interface ITradePlanStore
{
    Task SaveAsync(Guid alertId, TradePlan plan, CancellationToken ct);
}
