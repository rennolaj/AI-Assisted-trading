namespace Mvp.Trading.Execution;

/// <summary>
/// Types of discrepancies detected during reconciliation.
/// </summary>
public enum ReconciliationDiscrepancyType
{
    /// <summary>Order placed internally but not found on exchange (possible network failure)</summary>
    MISSING_ON_EXCHANGE,
    
    /// <summary>Order exists on exchange but not in our internal state (manual intervention or crash)</summary>
    ORPHANED_ON_EXCHANGE,
    
    /// <summary>Order status differs between internal and exchange (e.g. canceled vs open)</summary>
    STATUS_MISMATCH,
    
    /// <summary>Fill quantity doesn't match expectations</summary>
    FILL_MISMATCH,
    
    /// <summary>Stop-loss triggered (informational, expected behavior)</summary>
    INVALIDATION_TRIGGERED,
    
    /// <summary>Reconciliation error (API timeout, network issue, etc.)</summary>
    ERROR
}
