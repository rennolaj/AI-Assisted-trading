namespace Mvp.Trading.Api.Services;

/// <summary>
/// Query interface for Elliott candidates.
/// </summary>
public interface IElliottCandidatesQuery
{
    Task<string?> GetJsonByAlertIdAsync(Guid alertId, CancellationToken ct);
}
