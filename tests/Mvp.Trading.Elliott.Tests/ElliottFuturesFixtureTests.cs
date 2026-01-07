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
/// Real-world futures data scenario tests using aggregated trade history.
/// </summary>
public sealed class ElliottFuturesFixtureTests
{
    [Fact]
    public async Task Engine_FuturesTrades_InsufficientPivots_YieldsSyntheticCandidate()
    {
        var fixture = LoadFixture("fixtures/kraken-futures/btcusd_p_m15.json");
        var candles = fixture.Candles;
        var evaluationTime = candles[^1].OpenTimeUtc.AddMinutes(fixture.IntervalMinutes);

        var options = new ElliottOptions
        {
            MinBars = 10,
            MaxBars = 200,
            MinPivotCount = 20
        };

        var engine = BuildEngine(new FixtureMarketDataProvider(candles), options);
        var parameters = new ElliottParameters("ZigZag", Depth: 2, DeviationPct: 0.4m, MaxCandidates: 10);

        var first = await engine.GenerateCandidatesAsync(
            fixture.Symbol,
            Timeframe.M15,
            parameters,
            evaluationTime,
            CancellationToken.None);

        var second = await engine.GenerateCandidatesAsync(
            fixture.Symbol,
            Timeframe.M15,
            parameters,
            evaluationTime,
            CancellationToken.None);

        Assert.Equal(ElliottCandidatesJson.Serialize(first), ElliottCandidatesJson.Serialize(second));

        var candidate = Assert.Single(first.Candidates);
        var violation = Assert.Single(candidate.RuleViolations);
        Assert.Equal(ElliottRuleCodes.PivotsInsufficient, violation.Rule);
        Assert.Equal("ERROR", violation.Severity);
        Assert.Equal(0m, candidate.Score);
        Assert.Equal(0m, candidate.Confidence);
        Assert.Null(candidate.Invalidation.LongInvalidationPrice);
        Assert.Null(candidate.Invalidation.ShortInvalidationPrice);
    }

    [Fact]
    public async Task Engine_FuturesTrades_M1_YieldsCandidates()
    {
        var fixture = LoadFixture("fixtures/kraken-futures/btcusd_p_m1_varied.json");
        var candles = fixture.Candles;
        var evaluationTime = candles[^1].OpenTimeUtc.AddMinutes(fixture.IntervalMinutes);

        var options = new ElliottOptions
        {
            MinBars = 20,
            MaxBars = 200,
            MinPivotCount = 4
        };

        var parameters = new ElliottParameters("ZigZag", Depth: 1, DeviationPct: 0.05m, MaxCandidates: 25);
        var pivotExtractor = new ZigZagPivotExtractor(options);
        var pivots = pivotExtractor.Extract(candles, Timeframe.M1, parameters, evaluationTime);

        Assert.True(
            pivots.Count >= options.MinPivotCount,
            $"Fixture produced {pivots.Count} pivots, expected at least {options.MinPivotCount}.");

        var engine = BuildEngine(new FixtureMarketDataProvider(candles), options);
        var first = await engine.GenerateCandidatesAsync(
            fixture.Symbol,
            Timeframe.M1,
            parameters,
            evaluationTime,
            CancellationToken.None);

        var second = await engine.GenerateCandidatesAsync(
            fixture.Symbol,
            Timeframe.M1,
            parameters,
            evaluationTime,
            CancellationToken.None);

        Assert.Equal(ElliottCandidatesJson.Serialize(first), ElliottCandidatesJson.Serialize(second));
        Assert.NotEmpty(first.Candidates);
        Assert.DoesNotContain(first.Candidates, candidate =>
            candidate.RuleViolations.Any(v =>
                string.Equals(v.Rule, ElliottRuleCodes.PivotsInsufficient, StringComparison.Ordinal) ||
                string.Equals(v.Rule, ElliottRuleCodes.TimeframeUnsupported, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Engine_FuturesTrades_M1_LongFixture_YieldsCandidates()
    {
        var fixture = LoadFixture("fixtures/kraken-futures/pf_ethusd_m1_long.json");
        var candles = fixture.Candles;
        var evaluationTime = candles[^1].OpenTimeUtc.AddMinutes(fixture.IntervalMinutes);

        var options = new ElliottOptions
        {
            MinBars = 10,
            MaxBars = 200,
            MinPivotCount = 8
        };

        var parameters = new ElliottParameters("ZigZag", Depth: 1, DeviationPct: 0.1m, MaxCandidates: 25);
        var pivotExtractor = new ZigZagPivotExtractor(options);
        var pivots = pivotExtractor.Extract(candles, Timeframe.M1, parameters, evaluationTime);

        Assert.True(
            pivots.Count >= options.MinPivotCount,
            $"Fixture produced {pivots.Count} pivots, expected at least {options.MinPivotCount}.");

        var engine = BuildEngine(new FixtureMarketDataProvider(candles), options);
        var first = await engine.GenerateCandidatesAsync(
            fixture.Symbol,
            Timeframe.M1,
            parameters,
            evaluationTime,
            CancellationToken.None);

        Assert.NotEmpty(first.Candidates);
        Assert.DoesNotContain(first.Candidates, candidate =>
            candidate.RuleViolations.Any(v =>
                string.Equals(v.Rule, ElliottRuleCodes.PivotsInsufficient, StringComparison.Ordinal) ||
                string.Equals(v.Rule, ElliottRuleCodes.TimeframeUnsupported, StringComparison.Ordinal)));
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

    private static FuturesOhlcFixture LoadFixture(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Fixture not found: {path}");
        }

        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var symbol = root.GetProperty("symbol").GetString() ?? "BTCUSD.P";
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

        return new FuturesOhlcFixture(symbol, interval, candles);
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

    private sealed record FuturesOhlcFixture(string Symbol, int IntervalMinutes, IReadOnlyList<Candle> Candles);

    private sealed class FixtureMarketDataProvider : IMarketDataProvider
    {
        private readonly IReadOnlyList<Candle> _candles;

        public FixtureMarketDataProvider(IReadOnlyList<Candle> candles)
        {
            _candles = candles;
        }

        public string ExchangeId => "kraken-futures-fixture";

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
