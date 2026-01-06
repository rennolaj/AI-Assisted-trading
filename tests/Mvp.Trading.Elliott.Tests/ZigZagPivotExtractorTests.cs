using System;
using System.Collections.Generic;
using Mvp.Trading.Contracts;
using Mvp.Trading.Elliott;
using Xunit;

namespace Mvp.Trading.Elliott.Tests;

/// <summary>
/// ZigZag pivot extraction fixtures and edge cases.
/// </summary>
public sealed class ZigZagPivotExtractorTests
{
    [Fact]
    public void EqualHighs_UsesEarliestHighPivot()
    {
        var candles = BuildCandles(
            new[] { 101m, 104m, 104m, 100m },
            new[] { 100m, 103m, 103m, 99m });

        var extractor = new ZigZagPivotExtractor(new ElliottOptions());
        var parameters = new ElliottParameters("ZigZag", 1, 2m, 5);
        var evaluationTime = candles[^1].OpenTimeUtc.AddMinutes(1);

        var pivots = extractor.Extract(candles, Timeframe.M1, parameters, evaluationTime);

        Assert.Equal(2, pivots.Count);
        Assert.Equal(PivotType.Low, pivots[0].Type);
        Assert.Equal(0, pivots[0].Index);
        Assert.Equal(PivotType.High, pivots[1].Type);
        Assert.Equal(1, pivots[1].Index);
    }

    [Fact]
    public void FlatMarket_ProducesNoPivots()
    {
        var candles = BuildCandles(
            new[] { 100m, 100m, 100m, 100m },
            new[] { 100m, 100m, 100m, 100m });

        var extractor = new ZigZagPivotExtractor(new ElliottOptions());
        var parameters = new ElliottParameters("ZigZag", 1, 1m, 5);
        var evaluationTime = candles[^1].OpenTimeUtc.AddMinutes(1);

        var pivots = extractor.Extract(candles, Timeframe.M1, parameters, evaluationTime);

        Assert.Empty(pivots);
    }

    [Fact]
    public void DepthRequirement_PreventsEarlyPivot()
    {
        var candles = BuildCandles(
            new[] { 101m, 105m, 106m, 107m },
            new[] { 100m, 104m, 105m, 106m });

        var extractor = new ZigZagPivotExtractor(new ElliottOptions());
        var parameters = new ElliottParameters("ZigZag", 5, 1m, 5);
        var evaluationTime = candles[^1].OpenTimeUtc.AddMinutes(1);

        var pivots = extractor.Extract(candles, Timeframe.M1, parameters, evaluationTime);

        Assert.Empty(pivots);
    }

    private static IReadOnlyList<Candle> BuildCandles(decimal[] highs, decimal[] lows)
    {
        if (highs.Length != lows.Length)
        {
            throw new ArgumentException("High/low arrays must match.");
        }

        var candles = new List<Candle>(highs.Length);
        var start = DateTimeOffset.Parse("2026-01-06T00:00:00Z");

        for (var i = 0; i < highs.Length; i++)
        {
            var low = lows[i];
            var high = highs[i];
            candles.Add(new Candle(
                start.AddMinutes(i),
                low,
                high,
                low,
                high,
                1m));
        }

        return candles;
    }
}
