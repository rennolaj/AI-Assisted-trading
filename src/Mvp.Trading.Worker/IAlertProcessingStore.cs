using Mvp.Trading.Contracts;

namespace Mvp.Trading.Worker;

/// <summary>
/// Persists processing status for dequeued alerts.
/// </summary>
public interface IAlertProcessingStore
{
    Task UpsertAsync(AlertEvent alert, string status, string? errorMessage, CancellationToken ct);
}
