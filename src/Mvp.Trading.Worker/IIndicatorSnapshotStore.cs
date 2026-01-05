using Mvp.Trading.Contracts;

namespace Mvp.Trading.Worker;

/// <summary>
/// Stores indicator snapshots for later retrieval.
/// </summary>
public interface IIndicatorSnapshotStore
{
    Task UpsertAsync(IndicatorSnapshot snapshot, CancellationToken ct);
}
