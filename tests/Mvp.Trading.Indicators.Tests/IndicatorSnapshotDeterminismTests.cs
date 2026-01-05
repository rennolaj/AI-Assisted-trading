using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mvp.Trading.Contracts;
using Mvp.Trading.Indicators;
using Xunit;

namespace Mvp.Trading.Indicators.Tests;

/// <summary>
/// Determinism tests for indicator snapshots.
/// </summary>
public sealed class IndicatorSnapshotDeterminismTests
{
    [Fact]
    public async Task IndicatorSnapshot_IsDeterministic()
    {
        var config = IndicatorDefaults.ScalpingDefault();
        var evaluationTime = DateTimeOffset.Parse("2026-01-05T12:00:00Z");
        var provider = new StubMarketDataProvider(config.Timeframes, evaluationTime);
        var engine = new IndicatorEngine(provider, config);
        var input = new IndicatorInput(
            Guid.Parse("6B1E3F9A-8E32-4AA7-AB40-83206E6A7C6A"),
            Guid.Parse("9D48C3A0-746A-4D4A-9A1A-08E64B4B4C72"),
            "PI_XBTUSD",
            "LONG",
            evaluationTime);

        var first = await engine.ComputeAsync(input, CancellationToken.None);
        var second = await engine.ComputeAsync(input, CancellationToken.None);

        Assert.True(first.Ok);
        Assert.True(second.Ok);
        Assert.NotNull(first.Value);
        Assert.NotNull(second.Value);

        var jsonFirst = IndicatorSnapshotJson.Serialize(first.Value!);
        var jsonSecond = IndicatorSnapshotJson.Serialize(second.Value!);

        Assert.Equal(jsonFirst, jsonSecond);

        var hashFirst = ComputeHash(jsonFirst);
        var hashSecond = ComputeHash(jsonSecond);

        Assert.Equal(hashFirst, hashSecond);
    }

    private static string ComputeHash(string payload)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(payload);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    private sealed class StubMarketDataProvider : IMarketDataProvider
    {
        private readonly Dictionary<Timeframe, IReadOnlyList<Candle>> _candles;

        public StubMarketDataProvider(IReadOnlyList<Timeframe> timeframes, DateTimeOffset evaluationTime)
        {
            _candles = timeframes.ToDictionary(tf => tf, tf => GenerateCandles(tf, evaluationTime));
        }

        public string ExchangeId => "stub";

        public Task<Result<IReadOnlyList<Instrument>>> GetInstrumentsAsync(CancellationToken ct)
        {
            return Task.FromResult(new Result<IReadOnlyList<Instrument>>(true, Array.Empty<Instrument>(), null));
        }

        public Task<Result<IReadOnlyList<Ticker>>> GetTickersAsync(CancellationToken ct)
        {
            return Task.FromResult(new Result<IReadOnlyList<Ticker>>(true, Array.Empty<Ticker>(), null));
        }

        public Task<Result<IReadOnlyList<Candle>>> GetOhlcvAsync(string symbol, Timeframe timeframe, int lookbackBars, CancellationToken ct)
        {
            if (!_candles.TryGetValue(timeframe, out var candles))
            {
                return Task.FromResult(new Result<IReadOnlyList<Candle>>(false, null, new Error("MISSING_TF", "Missing candles.", null)));
            }

            var slice = candles.Count > lookbackBars
                ? candles.Skip(candles.Count - lookbackBars).ToList()
                : candles.ToList();

            return Task.FromResult(new Result<IReadOnlyList<Candle>>(true, slice, null));
        }

        private static IReadOnlyList<Candle> GenerateCandles(Timeframe timeframe, DateTimeOffset evaluationTime)
        {
            const int count = 220;
            var minutes = TimeframeToMinutes(timeframe);
            var start = evaluationTime.AddMinutes(-minutes * (count + 1));
            var candles = new List<Candle>(count);

            for (var i = 0; i < count; i++)
            {
                var openTime = start.AddMinutes(minutes * i);
                var close = 100m + (i * 0.25m);
                var open = close - 0.05m;
                var high = close + 0.1m;
                var low = close - 0.1m;
                var volume = 100m + i;

                candles.Add(new Candle(openTime, open, high, low, close, volume));
            }

            return candles;
        }

        private static int TimeframeToMinutes(Timeframe timeframe) => timeframe switch
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
    }
}
