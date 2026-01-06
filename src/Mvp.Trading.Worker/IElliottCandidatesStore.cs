using Mvp.Trading.Contracts;

namespace Mvp.Trading.Worker;

/// <summary>
/// Stores Elliott candidates for later retrieval.
/// </summary>
public interface IElliottCandidatesStore
{
    Task UpsertAsync(
        Guid alertId,
        DateTimeOffset computedAtUtc,
        DateTimeOffset evaluationTimeUtc,
        string symbol,
        Timeframe baseTimeframe,
        ElliottParameters parameters,
        ElliottCandidates candidates,
        CancellationToken ct);
}
