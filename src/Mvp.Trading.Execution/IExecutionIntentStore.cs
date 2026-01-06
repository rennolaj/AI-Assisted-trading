namespace Mvp.Trading.Execution;

/// <summary>
/// Persists execution intents.
/// </summary>
public interface IExecutionIntentStore
{
    Task SaveAsync(Guid executionId, Guid planId, string mode, string status, DateTimeOffset createdAtUtc, CancellationToken ct);
}
