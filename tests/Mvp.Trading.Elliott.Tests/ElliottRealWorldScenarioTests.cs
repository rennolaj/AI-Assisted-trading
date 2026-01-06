using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mvp.Trading.Contracts;
using Mvp.Trading.Elliott;
using Xunit;

namespace Mvp.Trading.Elliott.Tests;

/// <summary>
/// Real-world data scenario tests using captured Kraken spot OHLC data.
/// </summary>
public sealed class ElliottRealWorldScenarioTests
{
    [Fact]
    public async Task Engine_RealWorldCandles_IsDeterministic()
    {
        var fixture = LoadFixture("fixtures/kraken-spot/xxbtzusd_m15.json");
        var candles = fixture.Candles;
        var evaluationTime = candles[^1].OpenTimeUtc.AddMinutes(fixture.IntervalMinutes);

        var marketData = new FixtureMarketDataProvider(candles);
        var options = new ElliottOptions
        {
            MinBars = 200,
            MaxBars = 500,
            DepthMultiplier = 30
        };

        var engine = BuildEngine(marketData, options);
        var parameters = new ElliottParameters("ZigZag", Depth: 2, DeviationPct: 0.4m, MaxCandidates: 25);
        var pivotExtractor = new ZigZagPivotExtractor(options);
        var pivots = pivotExtractor.Extract(candles, Timeframe.M15, parameters, evaluationTime);

        Assert.True(
            pivots.Count >= options.MinPivotCount,
            $"Fixture produced {pivots.Count} pivots, expected at least {options.MinPivotCount}.");

        var first = await engine.GenerateCandidatesAsync(
            fixture.Pair,
            Timeframe.M15,
            parameters,
            evaluationTime,
            CancellationToken.None);

        var second = await engine.GenerateCandidatesAsync(
            fixture.Pair,
            Timeframe.M15,
            parameters,
            evaluationTime,
            CancellationToken.None);

        var firstJson = ElliottCandidatesJson.Serialize(first);
        var secondJson = ElliottCandidatesJson.Serialize(second);

        Assert.Equal(firstJson, secondJson);
        Assert.NotEmpty(first.Candidates);
        Assert.True(candles.Count >= options.MinBars, "Fixture should contain enough candles for lookback sizing.");
        Assert.DoesNotContain(first.Candidates, candidate =>
            candidate.RuleViolations.Any(v =>
                string.Equals(v.Rule, ElliottRuleCodes.TimeframeUnsupported, StringComparison.Ordinal) ||
                string.Equals(v.Rule, ElliottRuleCodes.PivotsInsufficient, StringComparison.Ordinal)));
    }

    private static ElliottEngine BuildEngine(IMarketDataProvider marketData, ElliottOptions options)
    {
        return new ElliottEngine(
            marketData,
            options,
            new ZigZagPivotExtractor(options),
            new ImpulseCandidateBuilder(options),
            new CandidateScorer(options),
            new InvalidationCalculator(options));
    }

    private static KrakenOhlcFixture LoadFixture(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Fixture not found: {path}");
        }

        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var pair = root.GetProperty("pair").GetString() ?? "XXBTZUSD";
        var interval = root.GetProperty("intervalMinutes").GetInt32();
        var candles = new List<Candle>();

        foreach (var item in root.GetProperty("candles").EnumerateArray())
        {
            var values = item.EnumerateArray().ToArray();
            if (values.Length < 6)
            {
                continue;
            }

            var tsSeconds = ReadLong(values[0]);
            var open = ReadDecimal(values[1]);
            var high = ReadDecimal(values[2]);
            var low = ReadDecimal(values[3]);
            var close = ReadDecimal(values[4]);
            var volume = values.Length > 6 ? ReadDecimal(values[6]) : 0m;

            var openTime = DateTimeOffset.FromUnixTimeSeconds(tsSeconds);
            candles.Add(new Candle(openTime, open, high, low, close, volume));
        }

        candles = candles.OrderBy(c => c.OpenTimeUtc).ToList();

        return new KrakenOhlcFixture(pair, interval, candles);
    }

    private static long ReadLong(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetInt64(),
            JsonValueKind.String when long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            _ => 0L
        };
    }

    private static decimal ReadDecimal(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) => value,
            _ => 0m
        };
    }

    private sealed record KrakenOhlcFixture(string Pair, int IntervalMinutes, IReadOnlyList<Candle> Candles);

    private sealed class FixtureMarketDataProvider : IMarketDataProvider
    {
        private readonly IReadOnlyList<Candle> _candles;

        public FixtureMarketDataProvider(IReadOnlyList<Candle> candles)
        {
            _candles = candles;
        }

        public string ExchangeId => "kraken-spot-fixture";

        public Task<Result<IReadOnlyList<Instrument>>> GetInstrumentsAsync(CancellationToken ct)
        {
            return Task.FromResult(new Result<IReadOnlyList<Instrument>>(true, Array.Empty<Instrument>(), null));
        }

        public Task<Result<IReadOnlyList<Ticker>>> GetTickersAsync(CancellationToken ct)
        {
            return Task.FromResult(new Result<IReadOnlyList<Ticker>>(true, Array.Empty<Ticker>(), null));
        }

        public Task<Result<IReadOnlyList<Candle>>> GetOhlcvAsync(
            string symbol,
            Timeframe timeframe,
            int lookbackBars,
            CancellationToken ct)
        {
            var slice = _candles.Count > lookbackBars
                ? _candles.Skip(_candles.Count - lookbackBars).ToList()
                : _candles.ToList();

            return Task.FromResult(new Result<IReadOnlyList<Candle>>(true, slice, null));
        }
    }
}
