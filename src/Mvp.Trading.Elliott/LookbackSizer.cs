using System;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Elliott;

/// <summary>
/// Computes lookback sizing for Elliott pivot extraction.
/// </summary>
public static class LookbackSizer
{
    /// <summary>
    /// Computes the number of bars to request for the given depth.
    /// </summary>
    public static int ComputeLookbackBars(Timeframe timeframe, int depth, ElliottOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (depth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "Depth must be greater than zero.");
        }

        long computed;
        if (options.LookbackDays > 0)
        {
            var barsPerDay = BarsPerDay(timeframe);
            computed = (long)barsPerDay * options.LookbackDays;
        }
        else
        {
            computed = (long)depth * options.DepthMultiplier + 200L;
        }

        computed = Math.Max(1, computed);
        var clamped = Math.Clamp(computed, options.MinBars, options.MaxBars);
        return (int)clamped;
    }

    private static int BarsPerDay(Timeframe timeframe)
    {
        var minutes = timeframe switch
        {
            Timeframe.M1 => 1,
            Timeframe.M5 => 5,
            Timeframe.M15 => 15,
            Timeframe.M30 => 30,
            Timeframe.H1 => 60,
            Timeframe.H2 => 120,
            Timeframe.H4 => 240,
            Timeframe.H12 => 720,
            Timeframe.D1 => 1440,
            _ => 1
        };

        return Math.Max(1, 1440 / minutes);
    }
}
