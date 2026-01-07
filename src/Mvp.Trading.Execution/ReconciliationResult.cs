namespace Mvp.Trading.Execution;

/// <summary>
/// Result of a reconciliation run.
/// </summary>
public sealed record ReconciliationResult(
    int ExecutionsChecked,
    int DiscrepanciesFound,
    IReadOnlyList<ReconciliationDiscrepancy> Discrepancies
);
