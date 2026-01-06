using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mvp.Trading.Contracts;
using Mvp.Trading.Elliott;
using Xunit;

namespace Mvp.Trading.Elliott.Tests;

/// <summary>
/// Tests for Elliott engine orchestration and run-level failures.
/// </summary>
public sealed class ElliottEngineTests
{
    [Fact]
    public async Task GenerateCandidatesAsync_UnsupportedTimeframe_ReturnsSyntheticCandidate()
    {
        var marketData = new StubMarketDataProvider((_, _, _) => OkCandles(Array.Empty<Candle>()));
        var options = new ElliottOptions();
        var engine = BuildEngine(marketData, options, new FakePivotExtractor(Array.Empty<PivotPoint>()));
        var parameters = new ElliottParameters("ZigZag", 5, 5m, 5);

        var result = await engine.GenerateCandidatesAsync(
            "BTCUSD.P",
            Timeframe.M30,
            parameters,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(0, marketData.OhlcvCalls);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("OTHER", candidate.WaveLabel);
        Assert.Equal(0m, candidate.Score);
        Assert.Equal(0m, candidate.Confidence);
        Assert.Null(candidate.Invalidation.LongInvalidationPrice);
        Assert.Null(candidate.Invalidation.ShortInvalidationPrice);

        var violation = Assert.Single(candidate.RuleViolations);
        Assert.Equal(ElliottRuleCodes.TimeframeUnsupported, violation.Rule);
        Assert.Equal("ERROR", violation.Severity);
    }

    [Fact]
    public async Task GenerateCandidatesAsync_InsufficientPivots_ReturnsSyntheticCandidate()
    {
        var marketData = new StubMarketDataProvider((_, _, _) => OkCandles(Array.Empty<Candle>()));
        var options = new ElliottOptions { MinPivotCount = 5 };
        var engine = BuildEngine(marketData, options, new FakePivotExtractor(Array.Empty<PivotPoint>()));
        var parameters = new ElliottParameters("ZigZag", 5, 5m, 5);

        var result = await engine.GenerateCandidatesAsync(
            "BTCUSD.P",
            Timeframe.M5,
            parameters,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        var candidate = Assert.Single(result.Candidates);
        var violation = Assert.Single(candidate.RuleViolations);
        Assert.Equal(ElliottRuleCodes.PivotsInsufficient, violation.Rule);
        Assert.Equal("ERROR", violation.Severity);
    }

    [Fact]
    public async Task GenerateCandidatesAsync_UsesTickSizeOverrideForInvalidation()
    {
        var pivots = BuildUptrendPivots(100m, 120m, 110m, 140m, 130m, 160m);
        var marketData = new StubMarketDataProvider((_, _, _) => OkCandles(Array.Empty<Candle>()));
        var options = new ElliottOptions
        {
            MinPivotCount = 6,
            TickSizeOverrides = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["BTCUSD.P"] = 0.5m
            }
        };

        var engine = BuildEngine(marketData, options, new FakePivotExtractor(pivots));
        var parameters = new ElliottParameters("ZigZag", 5, 5m, 10);

        var result = await engine.GenerateCandidatesAsync(
            "BTCUSD.P",
            Timeframe.M5,
            parameters,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        var candidate = result.Candidates.Single(c => c.WaveLabel == "W5END");
        Assert.Null(candidate.Invalidation.LongInvalidationPrice);
        Assert.Equal(161.0m, candidate.Invalidation.ShortInvalidationPrice);
    }

    [Fact]
    public async Task GenerateCandidatesAsync_IsDeterministicAcrossRuns()
    {
        var pivots = BuildUptrendPivots(100m, 120m, 110m, 140m, 130m, 160m);
        var marketData = new StubMarketDataProvider((_, _, _) => OkCandles(Array.Empty<Candle>()));
        var options = new ElliottOptions { MinPivotCount = 6 };
        var engine = BuildEngine(marketData, options, new FakePivotExtractor(pivots));
        var parameters = new ElliottParameters("ZigZag", 5, 5m, 10);

        var first = await engine.GenerateCandidatesAsync(
            "BTCUSD.P",
            Timeframe.M5,
            parameters,
            new DateTimeOffset(2026, 1, 6, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        var second = await engine.GenerateCandidatesAsync(
            "BTCUSD.P",
            Timeframe.M5,
            parameters,
            new DateTimeOffset(2026, 1, 6, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        var firstIds = string.Join("|", first.Candidates.Select(c => c.CandidateId));
        var secondIds = string.Join("|", second.Candidates.Select(c => c.CandidateId));
        Assert.Equal(firstIds, secondIds);
    }

    private static ElliottEngine BuildEngine(
        IMarketDataProvider marketData,
        ElliottOptions options,
        IPivotExtractor pivotExtractor)
    {
        return new ElliottEngine(
            marketData,
            options,
            pivotExtractor,
            new ImpulseCandidateBuilder(options),
            new CandidateScorer(options),
            new InvalidationCalculator(options));
    }

    private static Result<IReadOnlyList<Candle>> OkCandles(IReadOnlyList<Candle> candles)
    {
        return new Result<IReadOnlyList<Candle>>(true, candles, null);
    }

    private static IReadOnlyList<PivotPoint> BuildUptrendPivots(
        decimal p0,
        decimal p1,
        decimal p2,
        decimal p3,
        decimal p4,
        decimal p5)
    {
        var start = DateTimeOffset.Parse("2026-01-06T00:00:00Z");
        return new List<PivotPoint>
        {
            new(0, start, p0, PivotType.Low),
            new(1, start.AddMinutes(1), p1, PivotType.High),
            new(2, start.AddMinutes(2), p2, PivotType.Low),
            new(3, start.AddMinutes(3), p3, PivotType.High),
            new(4, start.AddMinutes(4), p4, PivotType.Low),
            new(5, start.AddMinutes(5), p5, PivotType.High)
        };
    }

    private sealed class FakePivotExtractor : IPivotExtractor
    {
        private readonly IReadOnlyList<PivotPoint> _pivots;

        public FakePivotExtractor(IReadOnlyList<PivotPoint> pivots)
        {
            _pivots = pivots;
        }

        public IReadOnlyList<PivotPoint> Extract(
            IReadOnlyList<Candle> candles,
            Timeframe timeframe,
            ElliottParameters parameters,
            DateTimeOffset evaluationTimeUtc)
        {
            return _pivots;
        }
    }

    private sealed class StubMarketDataProvider : IMarketDataProvider
    {
        private readonly Func<string, Timeframe, int, Result<IReadOnlyList<Candle>>> _ohlcvHandler;

        public StubMarketDataProvider(Func<string, Timeframe, int, Result<IReadOnlyList<Candle>>> ohlcvHandler)
        {
            _ohlcvHandler = ohlcvHandler;
        }

        public int OhlcvCalls { get; private set; }

        public string ExchangeId => "stub";

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
            OhlcvCalls++;
            return Task.FromResult(_ohlcvHandler(symbol, timeframe, lookbackBars));
        }
    }
}
