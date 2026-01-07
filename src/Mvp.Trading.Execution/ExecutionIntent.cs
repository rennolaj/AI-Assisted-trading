namespace Mvp.Trading.Execution;

/// <summary>
/// Lightweight representation of an execution intent for reconciliation queries.
/// </summary>
public sealed record ExecutionIntent(
    Guid ExecutionId,
    Guid PlanId,
    string Mode,
    string Status,
    DateTimeOffset CreatedAtUtc
);
