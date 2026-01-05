using System;
using System.Collections.Generic;
using System.Linq;
using Mvp.Trading.Indicators;
using Xunit;

namespace Mvp.Trading.Indicators.Tests;

/// <summary>
/// Deterministic tests for indicator math helpers.
/// </summary>
public sealed class IndicatorMathTests
{
    [Fact]
    public void ComputeRsi_MonotonicIncrease_IsOverbought()
    {
        var closes = Enumerable.Range(1, 20).Select(i => (decimal)i).ToList();
        var rsiSeries = IndicatorMath.ComputeRsi(closes, 14);
        var last = GetLast(rsiSeries);

        Assert.NotNull(last);
        Assert.Equal(100m, Math.Round(last!.Value, 6, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void ComputeStochRsi_Crossover_IsDeterministic()
    {
        var rsiValues = new List<decimal?>
        {
            36m, 17m, 12m, 79m, 64m, 89m, 22m, 43m, 18m, 38m, 19m, 48m
        };

        var series = IndicatorMath.ComputeStochRsi(rsiValues, stochPeriod: 5, kPeriod: 3, dPeriod: 3);
        var k = GetLast(series.K);
        var d = GetLast(series.D);

        Assert.NotNull(k);
        Assert.NotNull(d);
        Assert.Equal(44.056338m, Math.Round(k!.Value, 6, MidpointRounding.AwayFromZero));
        Assert.Equal(24.872258m, Math.Round(d!.Value, 6, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void ComputeMacd_IsDeterministic()
    {
        var closes = Enumerable.Range(1, 50).Select(i => (decimal)i).ToList();
        var macdSeries = IndicatorMath.ComputeMacd(closes, 12, 26, 9);
        var macd = GetLast(macdSeries.Macd);
        var signal = GetLast(macdSeries.Signal);
        var hist = GetLast(macdSeries.Hist);

        Assert.NotNull(macd);
        Assert.NotNull(signal);
        Assert.NotNull(hist);
        Assert.Equal(7.000000m, Math.Round(macd!.Value, 6, MidpointRounding.AwayFromZero));
        Assert.Equal(7.000000m, Math.Round(signal!.Value, 6, MidpointRounding.AwayFromZero));
        Assert.Equal(0.000000m, Math.Round(hist!.Value, 6, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void ComputeVolumeRatio_IsDeterministic()
    {
        var volumes = Enumerable.Range(1, 30).Select(i => (decimal)i).ToList();
        var sma = IndicatorMath.ComputeSma(volumes, 20);

        Assert.NotNull(sma);

        var ratio = volumes[^1] / sma!.Value;
        Assert.Equal(1.463415m, Math.Round(ratio, 6, MidpointRounding.AwayFromZero));
    }

    private static decimal? GetLast(IReadOnlyList<decimal?> series)
    {
        for (var i = series.Count - 1; i >= 0; i--)
        {
            if (series[i] is not null)
            {
                return series[i];
            }
        }

        return null;
    }
}
