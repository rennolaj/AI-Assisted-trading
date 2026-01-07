namespace Mvp.Trading.Execution;

/// <summary>
/// Persists reconciliation results and discrepancies.
/// </summary>
public interface IReconciliationStore
{
    /// <summary>
    /// Save reconciliation run results and any detected discrepancies.
    /// </summary>
    Task SaveReconciliationAsync(
        int executionsChecked, 
        IReadOnlyList<ReconciliationDiscrepancy> discrepancies, 
        CancellationToken ct = default);
    
    /// <summary>
    /// Get all unresolved discrepancies for manual investigation.
    /// </summary>
    Task<IReadOnlyList<ReconciliationDiscrepancy>> GetUnresolvedDiscrepanciesAsync(
        CancellationToken ct = default);
    
    /// <summary>
    /// Mark a discrepancy as resolved with notes.
    /// </summary>
    Task MarkDiscrepancyResolvedAsync(
        Guid discrepancyId, 
        string resolutionNotes, 
        CancellationToken ct = default);
}
