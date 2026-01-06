using System;
using System.Threading;
using System.Threading.Tasks;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Elliott;

/// <summary>
/// Generates deterministic Elliott candidates from market data.
/// </summary>
public interface IElliottEngine
{
    /// <summary>
    /// Builds Elliott candidates for the requested base timeframe.
    /// </summary>
    Task<ElliottCandidates> GenerateCandidatesAsync(
        string symbol,
        Timeframe baseTimeframe,
        ElliottParameters parameters,
        DateTimeOffset evaluationTimeUtc,
        CancellationToken ct);
}
