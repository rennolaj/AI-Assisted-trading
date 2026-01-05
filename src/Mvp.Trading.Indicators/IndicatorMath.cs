using System;
using System.Collections.Generic;

namespace Mvp.Trading.Indicators;

/// <summary>
/// Deterministic indicator math utilities.
/// </summary>
public static class IndicatorMath
{
    public static List<decimal?> ComputeRsi(IReadOnlyList<decimal> closes, int period)
    {
        var results = new List<decimal?>(closes.Count);
        for (var i = 0; i < closes.Count; i++)
        {
            results.Add(null);
        }

        if (period <= 0 || closes.Count == 0)
        {
            return results;
        }

        if (closes.Count <= period)
        {
            return results;
        }

        decimal avgGain = 0m;
        decimal avgLoss = 0m;

        for (var i = 1; i <= period; i++)
        {
            var change = closes[i] - closes[i - 1];
            if (change > 0)
            {
                avgGain += change;
            }
            else
            {
                avgLoss += Math.Abs(change);
            }
        }

        avgGain /= period;
        avgLoss /= period;
        results[period] = ComputeRsiValue(avgGain, avgLoss);

        for (var i = period + 1; i < closes.Count; i++)
        {
            var change = closes[i] - closes[i - 1];
            var gain = change > 0 ? change : 0m;
            var loss = change < 0 ? Math.Abs(change) : 0m;

            avgGain = ((avgGain * (period - 1)) + gain) / period;
            avgLoss = ((avgLoss * (period - 1)) + loss) / period;
            results[i] = ComputeRsiValue(avgGain, avgLoss);
        }

        return results;
    }

    public static StochRsiSeries ComputeStochRsi(
        IReadOnlyList<decimal?> rsiSeries,
        int stochPeriod,
        int kPeriod,
        int dPeriod)
    {
        var stoch = new List<decimal?>(rsiSeries.Count);
        var kValues = new List<decimal?>(rsiSeries.Count);
        var dValues = new List<decimal?>(rsiSeries.Count);

        for (var i = 0; i < rsiSeries.Count; i++)
        {
            if (i < stochPeriod - 1)
            {
                stoch.Add(null);
                continue;
            }

            var window = new List<decimal>();
            for (var j = i - stochPeriod + 1; j <= i; j++)
            {
                var value = rsiSeries[j];
                if (value is null)
                {
                    window.Clear();
                    break;
                }

                window.Add(value.Value);
            }

            if (window.Count == 0)
            {
                stoch.Add(null);
                continue;
            }

            var min = Min(window);
            var max = Max(window);
            if (max == min)
            {
                stoch.Add(0m);
                continue;
            }

            var current = rsiSeries[i] ?? 0m;
            stoch.Add(((current - min) / (max - min)) * 100m);
        }

        kValues.AddRange(ComputeSmaSeries(stoch, kPeriod));
        dValues.AddRange(ComputeSmaSeries(kValues, dPeriod));

        return new StochRsiSeries(kValues, dValues);
    }

    public static MacdSeries ComputeMacd(IReadOnlyList<decimal> closes, int fastPeriod, int slowPeriod, int signalPeriod)
    {
        var fast = ComputeEma(closes, fastPeriod);
        var slow = ComputeEma(closes, slowPeriod);
        var macd = new List<decimal?>(closes.Count);

        for (var i = 0; i < closes.Count; i++)
        {
            if (fast[i] is null || slow[i] is null)
            {
                macd.Add(null);
                continue;
            }

            macd.Add(fast[i]!.Value - slow[i]!.Value);
        }

        var signal = ComputeEma(macd, signalPeriod);
        var hist = new List<decimal?>(closes.Count);

        for (var i = 0; i < closes.Count; i++)
        {
            if (macd[i] is null || signal[i] is null)
            {
                hist.Add(null);
                continue;
            }

            hist.Add(macd[i]!.Value - signal[i]!.Value);
        }

        return new MacdSeries(macd, signal, hist);
    }

    public static decimal? ComputeSma(IReadOnlyList<decimal> values, int period)
    {
        if (period <= 0 || values.Count < period)
        {
            return null;
        }

        decimal sum = 0m;
        for (var i = values.Count - period; i < values.Count; i++)
        {
            sum += values[i];
        }

        return sum / period;
    }

    private static List<decimal?> ComputeSmaSeries(IReadOnlyList<decimal?> values, int period)
    {
        var results = new List<decimal?>(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            if (i < period - 1)
            {
                results.Add(null);
                continue;
            }

            decimal sum = 0m;
            var count = 0;
            for (var j = i - period + 1; j <= i; j++)
            {
                if (values[j] is null)
                {
                    count = 0;
                    break;
                }

                sum += values[j]!.Value;
                count++;
            }

            results.Add(count == 0 ? null : sum / period);
        }

        return results;
    }

    private static List<decimal?> ComputeEma(IReadOnlyList<decimal> values, int period)
    {
        var results = new List<decimal?>(values.Count);
        if (period <= 0 || values.Count == 0)
        {
            for (var i = 0; i < values.Count; i++)
            {
                results.Add(null);
            }

            return results;
        }

        decimal? ema = null;
        var multiplier = 2m / (period + 1m);

        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
            if (i < period - 1)
            {
                results.Add(null);
                continue;
            }

            if (i == period - 1)
            {
                decimal sum = 0m;
                for (var j = 0; j < period; j++)
                {
                    sum += values[j];
                }

                ema = sum / period;
                results.Add(ema);
                continue;
            }

            ema = ((value - ema!.Value) * multiplier) + ema.Value;
            results.Add(ema);
        }

        return results;
    }

    private static List<decimal?> ComputeEma(IReadOnlyList<decimal?> values, int period)
    {
        var results = new List<decimal?>(values.Count);
        if (period <= 0 || values.Count == 0)
        {
            for (var i = 0; i < values.Count; i++)
            {
                results.Add(null);
            }

            return results;
        }

        decimal? ema = null;
        var multiplier = 2m / (period + 1m);
        var buffer = new List<decimal>(period);

        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
            if (value is null)
            {
                results.Add(null);
                continue;
            }

            if (ema is null)
            {
                buffer.Add(value.Value);
                if (buffer.Count < period)
                {
                    results.Add(null);
                    continue;
                }

                var sum = 0m;
                foreach (var item in buffer)
                {
                    sum += item;
                }

                ema = sum / period;
                results.Add(ema);
                continue;
            }

            ema = ((value.Value - ema.Value) * multiplier) + ema.Value;
            results.Add(ema);
        }

        return results;
    }

    private static decimal ComputeRsiValue(decimal avgGain, decimal avgLoss)
    {
        if (avgGain == 0m && avgLoss == 0m)
        {
            return 50m;
        }

        if (avgLoss == 0m)
        {
            return 100m;
        }

        if (avgGain == 0m)
        {
            return 0m;
        }

        var rs = avgGain / avgLoss;
        return 100m - (100m / (1m + rs));
    }

    private static decimal Min(IReadOnlyList<decimal> values)
    {
        var min = values[0];
        for (var i = 1; i < values.Count; i++)
        {
            if (values[i] < min)
            {
                min = values[i];
            }
        }

        return min;
    }

    private static decimal Max(IReadOnlyList<decimal> values)
    {
        var max = values[0];
        for (var i = 1; i < values.Count; i++)
        {
            if (values[i] > max)
            {
                max = values[i];
            }
        }

        return max;
    }
}

/// <summary>
/// Stochastic RSI result series.
/// </summary>
public sealed record StochRsiSeries(IReadOnlyList<decimal?> K, IReadOnlyList<decimal?> D);

/// <summary>
/// MACD result series.
/// </summary>
public sealed record MacdSeries(
    IReadOnlyList<decimal?> Macd,
    IReadOnlyList<decimal?> Signal,
    IReadOnlyList<decimal?> Hist);
