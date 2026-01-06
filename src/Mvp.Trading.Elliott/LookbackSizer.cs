using System;

namespace Mvp.Trading.Elliott;

/// <summary>
/// Computes lookback sizing for Elliott pivot extraction.
/// </summary>
public static class LookbackSizer
{
    /// <summary>
    /// Computes the number of bars to request for the given depth.
    /// </summary>
    public static int ComputeLookbackBars(int depth, ElliottOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (depth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "Depth must be greater than zero.");
        }

        var computed = (long)depth * options.DepthMultiplier + 200L;
        var clamped = Math.Clamp(computed, options.MinBars, options.MaxBars);
        return (int)clamped;
    }
}
