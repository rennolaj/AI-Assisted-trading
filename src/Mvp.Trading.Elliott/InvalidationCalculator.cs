using System;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Elliott;

/// <summary>
/// Computes invalidation levels for Elliott candidates.
/// </summary>
public sealed class InvalidationCalculator
{
    private readonly ElliottOptions _options;

    public InvalidationCalculator(ElliottOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public InvalidationLevels Compute(ImpulseCandidateContext context, decimal? tickSize)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var resolvedTickSize = tickSize.GetValueOrDefault(_options.TickSizeFallback);
        var buffer = resolvedTickSize * _options.InvalidationBufferTicks;

        decimal? longInvalidation = null;
        decimal? shortInvalidation = null;

        if (context.WaveLabel == "W3")
        {
            if (context.Wave.IsUptrend)
            {
                longInvalidation = ApplyRounding(context.Wave.P2.Price - buffer, resolvedTickSize, roundDown: true);
            }
            else
            {
                shortInvalidation = ApplyRounding(context.Wave.P2.Price + buffer, resolvedTickSize, roundDown: false);
            }
        }
        else if (context.WaveLabel == "W5END")
        {
            if (context.Wave.IsUptrend && context.Wave.P5 is not null)
            {
                shortInvalidation = ApplyRounding(context.Wave.P5.Price + buffer, resolvedTickSize, roundDown: false);
            }
            else if (!context.Wave.IsUptrend && context.Wave.P5 is not null)
            {
                longInvalidation = ApplyRounding(context.Wave.P5.Price - buffer, resolvedTickSize, roundDown: true);
            }
        }

        return new InvalidationLevels(longInvalidation, shortInvalidation);
    }

    private decimal ApplyRounding(decimal value, decimal tickSize, bool roundDown)
    {
        if (tickSize > 0m)
        {
            var ticks = value / tickSize;
            var roundedTicks = roundDown ? Math.Floor(ticks) : Math.Ceiling(ticks);
            return roundedTicks * tickSize;
        }

        return Math.Round(value, _options.SnapshotPrecisionDecimals, MidpointRounding.ToEven);
    }
}
