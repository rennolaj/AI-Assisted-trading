namespace Mvp.Trading.Execution;

/// <summary>
/// Configuration options for reconciliation loop.
/// </summary>
public sealed class ReconciliationOptions
{
    /// <summary>
    /// Polling interval in seconds between reconciliation runs.
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 60;
    
    /// <summary>
    /// Maximum number of discrepancies to log per run (prevent log spam).
    /// </summary>
    public int MaxDiscrepanciesToLog { get; set; } = 10;
}
