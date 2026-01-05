namespace Mvp.Trading.Api.Services;

/// <summary>
/// Query interface for indicator snapshots.
/// </summary>
public interface IIndicatorSnapshotQuery
{
    Task<string?> GetJsonByAlertIdAsync(Guid alertId, CancellationToken ct);
}
