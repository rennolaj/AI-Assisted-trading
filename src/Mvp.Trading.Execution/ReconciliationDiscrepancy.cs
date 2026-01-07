namespace Mvp.Trading.Execution;

/// <summary>
/// Represents a detected discrepancy between internal and exchange state.
/// </summary>
public sealed record ReconciliationDiscrepancy(
    Guid? ExecutionId,
    ReconciliationDiscrepancyType Type,
    string InternalState,
    string ExchangeState,
    string Details
);
