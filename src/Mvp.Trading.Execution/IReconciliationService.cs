using Mvp.Trading.Contracts;

namespace Mvp.Trading.Execution;

/// <summary>
/// Reconciles internal execution state against exchange state.
/// </summary>
public interface IReconciliationService
{
    /// <summary>
    /// Reconcile all active execution intents against exchange state.
    /// Returns list of detected discrepancies (detection only, no auto-remediation).
    /// </summary>
    Task<Result<ReconciliationResult>> ReconcileAsync(CancellationToken ct = default);
}
