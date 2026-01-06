using System;
using System.Collections.Generic;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Elliott;

/// <summary>
/// Extracts pivots from closed candles using the configured algorithm.
/// </summary>
public interface IPivotExtractor
{
    IReadOnlyList<PivotPoint> Extract(
        IReadOnlyList<Candle> candles,
        Timeframe timeframe,
        ElliottParameters parameters,
        DateTimeOffset evaluationTimeUtc);
}
