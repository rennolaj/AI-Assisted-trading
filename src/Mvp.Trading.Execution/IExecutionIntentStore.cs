namespace Mvp.Trading.Execution;

/// <summary>
/// Persists execution intents.
/// </summary>
public interface IExecutionIntentStore
{
    Task SaveAsync(Guid executionId, Guid planId, string mode, string status, DateTimeOffset createdAtUtc, CancellationToken ct);
    
    /// <summary>
    /// Get all active execution intents (not completed or failed).
    /// </summary>
    Task<IReadOnlyList<ExecutionIntent>> GetActiveAsync(CancellationToken ct = default);
}
