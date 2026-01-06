using System;
using System.Collections.Generic;
using System.Linq;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Elliott;

/// <summary>
/// Deterministic ZigZag pivot extractor using Depth and DeviationPct.
/// </summary>
public sealed class ZigZagPivotExtractor : IPivotExtractor
{
    private readonly ElliottOptions _options;

    public ZigZagPivotExtractor(ElliottOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IReadOnlyList<PivotPoint> Extract(
        IReadOnlyList<Candle> candles,
        Timeframe timeframe,
        ElliottParameters parameters,
        DateTimeOffset evaluationTimeUtc)
    {
        if (candles is null)
        {
            throw new ArgumentNullException(nameof(candles));
        }

        if (parameters is null)
        {
            throw new ArgumentNullException(nameof(parameters));
        }

        if (parameters.Depth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters.Depth), "Depth must be greater than zero.");
        }

        if (parameters.DeviationPct <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters.DeviationPct), "DeviationPct must be greater than zero.");
        }

        if (!_options.SupportedTimeframes.Contains(timeframe))
        {
            return Array.Empty<PivotPoint>();
        }

        var closed = FilterClosed(candles, timeframe, evaluationTimeUtc);
        if (closed.Count < 2)
        {
            return Array.Empty<PivotPoint>();
        }

        return ExtractPivots(closed, parameters.Depth, parameters.DeviationPct);
    }

    private IReadOnlyList<PivotPoint> ExtractPivots(
        IReadOnlyList<Candle> candles,
        int depth,
        decimal deviationPct)
    {
        var pivots = new List<PivotPoint>();

        var first = candles[0];
        var candidateHigh = new Extreme(Normalize(first.High), 0, first.OpenTimeUtc);
        var candidateLow = new Extreme(Normalize(first.Low), 0, first.OpenTimeUtc);

        var direction = SwingDirection.Unknown;
        Extreme? lastPivot = null;

        var hasSwingHigh = false;
        var hasSwingLow = false;
        Extreme swingHigh = default;
        Extreme swingLow = default;
        Extreme retraceLow = default;
        Extreme retraceHigh = default;

        for (var i = 1; i < candles.Count; i++)
        {
            var candle = candles[i];
            var high = Normalize(candle.High);
            var low = Normalize(candle.Low);

            if (high > candidateHigh.Price)
            {
                candidateHigh = new Extreme(high, i, candle.OpenTimeUtc);
            }

            if (low < candidateLow.Price)
            {
                candidateLow = new Extreme(low, i, candle.OpenTimeUtc);
            }

            if (direction == SwingDirection.Unknown)
            {
                if (candidateLow.Index < candidateHigh.Index &&
                    candidateHigh.Index - candidateLow.Index >= depth &&
                    HasDeviationUp(candidateLow.Price, candidateHigh.Price, deviationPct))
                {
                    pivots.Add(ToPivot(candidateLow, PivotType.Low));
                    direction = SwingDirection.Up;
                    lastPivot = candidateLow;
                    swingHigh = candidateHigh;
                    retraceLow = new Extreme(low, i, candle.OpenTimeUtc);
                    hasSwingHigh = true;
                    hasSwingLow = false;
                }
                else if (candidateHigh.Index < candidateLow.Index &&
                         candidateLow.Index - candidateHigh.Index >= depth &&
                         HasDeviationDown(candidateHigh.Price, candidateLow.Price, deviationPct))
                {
                    pivots.Add(ToPivot(candidateHigh, PivotType.High));
                    direction = SwingDirection.Down;
                    lastPivot = candidateHigh;
                    swingLow = candidateLow;
                    retraceHigh = new Extreme(high, i, candle.OpenTimeUtc);
                    hasSwingLow = true;
                    hasSwingHigh = false;
                }

                continue;
            }

            if (direction == SwingDirection.Up)
            {
                if (!hasSwingHigh)
                {
                    swingHigh = new Extreme(high, i, candle.OpenTimeUtc);
                    retraceLow = new Extreme(low, i, candle.OpenTimeUtc);
                    hasSwingHigh = true;
                }
                else if (high > swingHigh.Price)
                {
                    swingHigh = new Extreme(high, i, candle.OpenTimeUtc);
                    retraceLow = new Extreme(low, i, candle.OpenTimeUtc);
                }
                else if (low < retraceLow.Price)
                {
                    retraceLow = new Extreme(low, i, candle.OpenTimeUtc);
                }

                if (lastPivot is not null &&
                    retraceLow.Index - lastPivot.Value.Index >= depth &&
                    HasDeviationDown(swingHigh.Price, retraceLow.Price, deviationPct))
                {
                    pivots.Add(ToPivot(swingHigh, PivotType.High));
                    direction = SwingDirection.Down;
                    lastPivot = swingHigh;
                    swingLow = retraceLow;
                    retraceHigh = new Extreme(high, i, candle.OpenTimeUtc);
                    hasSwingLow = true;
                    hasSwingHigh = false;
                }
            }
            else if (direction == SwingDirection.Down)
            {
                if (!hasSwingLow)
                {
                    swingLow = new Extreme(low, i, candle.OpenTimeUtc);
                    retraceHigh = new Extreme(high, i, candle.OpenTimeUtc);
                    hasSwingLow = true;
                }
                else if (low < swingLow.Price)
                {
                    swingLow = new Extreme(low, i, candle.OpenTimeUtc);
                    retraceHigh = new Extreme(high, i, candle.OpenTimeUtc);
                }
                else if (high > retraceHigh.Price)
                {
                    retraceHigh = new Extreme(high, i, candle.OpenTimeUtc);
                }

                if (lastPivot is not null &&
                    retraceHigh.Index - lastPivot.Value.Index >= depth &&
                    HasDeviationUp(swingLow.Price, retraceHigh.Price, deviationPct))
                {
                    pivots.Add(ToPivot(swingLow, PivotType.Low));
                    direction = SwingDirection.Up;
                    lastPivot = swingLow;
                    swingHigh = retraceHigh;
                    retraceLow = new Extreme(low, i, candle.OpenTimeUtc);
                    hasSwingHigh = true;
                    hasSwingLow = false;
                }
            }
        }

        return pivots;
    }

    private List<Candle> FilterClosed(IReadOnlyList<Candle> candles, Timeframe timeframe, DateTimeOffset evaluationTimeUtc)
    {
        var span = TimeSpan.FromMinutes(TimeframeToMinutes(timeframe));
        return candles
            .Where(c => c.OpenTimeUtc.Add(span) <= evaluationTimeUtc)
            .OrderBy(c => c.OpenTimeUtc)
            .ToList();
    }

    private decimal Normalize(decimal value)
    {
        return Math.Round(value, _options.SnapshotPrecisionDecimals, MidpointRounding.ToEven);
    }

    private static bool HasDeviationDown(decimal lastHigh, decimal candidateLow, decimal deviationPct)
    {
        if (lastHigh <= 0m)
        {
            return false;
        }

        var movePct = ((lastHigh - candidateLow) / lastHigh) * 100m;
        return movePct >= deviationPct;
    }

    private static bool HasDeviationUp(decimal lastLow, decimal candidateHigh, decimal deviationPct)
    {
        if (lastLow <= 0m)
        {
            return false;
        }

        var movePct = ((candidateHigh - lastLow) / lastLow) * 100m;
        return movePct >= deviationPct;
    }

    private static PivotPoint ToPivot(Extreme extreme, PivotType type)
    {
        return new PivotPoint(extreme.Index, extreme.TimeUtc, extreme.Price, type);
    }

    private static int TimeframeToMinutes(Timeframe timeframe) => timeframe switch
    {
        Timeframe.M1 => 1,
        Timeframe.M5 => 5,
        Timeframe.M15 => 15,
        Timeframe.H1 => 60,
        Timeframe.H4 => 240,
        Timeframe.D1 => 1440,
        _ => 1
    };

    private enum SwingDirection
    {
        Unknown,
        Up,
        Down
    }

    private readonly record struct Extreme(decimal Price, int Index, DateTimeOffset TimeUtc);
}
