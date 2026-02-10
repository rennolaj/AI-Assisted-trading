using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mvp.Trading.Contracts;
using Mvp.Trading.Elliott;
using Mvp.Trading.Elliott.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Mvp.Trading.Elliott.Tests;

/// <summary>
/// Visual integration tests that run the full Elliott engine on fixture data,
/// assert basic expectations, and write HTML+Plotly reports to tests/visual-reports/.
/// </summary>
public sealed class ElliottVisualTests
{
    private readonly ITestOutputHelper _output;

    public ElliottVisualTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A 36-candle bullish impulse fixture should produce at least one candidate with no fatal violations,
    /// and the HTML report should be written successfully.
    /// </summary>
    [Fact]
    public async Task AcceptFixture_BullImpulse_ProducesCandidatesAndWritesReport()
    {
        // Arrange
        var candles = LoadCandles("fixtures/elliott/accept_bull.json");
        var evaluationTime = candles[^1].OpenTimeUtc.AddMinutes(1);

        var options = new ElliottOptions
        {
            MinBars = 10,
            MaxBars = 200,
            MinPivotCount = 4
        };

        var parameters = new ElliottParameters("ZigZag", Depth: 1, DeviationPct: 0.05m, MaxCandidates: 25);

        // Extract pivots using the real ZigZag extractor
        var pivotExtractor = new ZigZagPivotExtractor(options);
        var pivots = pivotExtractor.Extract(candles, Timeframe.M1, parameters, evaluationTime);

        _output.WriteLine($"Pivots extracted: {pivots.Count}");
        foreach (var p in pivots)
        {
            _output.WriteLine($"  P{p.Index}: {p.Type} @ {p.Price:F2} ({p.TimeUtc:HH:mm})");
        }

        Assert.True(
            pivots.Count >= options.MinPivotCount,
            $"Accept fixture produced only {pivots.Count} pivots, expected >= {options.MinPivotCount}.");

        // Run the full engine
        var engine = BuildEngine(new FixtureMarketDataProvider(candles), options);
        var result = await engine.GenerateCandidatesAsync(
            "VISUAL.ACCEPT",
            Timeframe.M1,
            parameters,
            evaluationTime,
            CancellationToken.None);

        // Assert: at least one candidate exists
        Assert.NotNull(result);
        Assert.NotEmpty(result.Candidates);
        _output.WriteLine($"Candidates: {result.Candidates.Count}");

        // Assert: no run-level error violations (PivotsInsufficient, TimeframeUnsupported)
        Assert.DoesNotContain(result.Candidates, c =>
            c.RuleViolations.Any(v =>
                string.Equals(v.Rule, ElliottRuleCodes.PivotsInsufficient, StringComparison.Ordinal) ||
                string.Equals(v.Rule, ElliottRuleCodes.TimeframeUnsupported, StringComparison.Ordinal)));

        // Write visual report
        var outDir = Path.Combine(AppContext.BaseDirectory, "visual-reports");
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(outDir, "accept_bull.html");
        ElliottVisualizer.WriteReport(outPath, "VISUAL.ACCEPT", candles, pivots, result);

        Assert.True(File.Exists(outPath), $"Expected HTML report at {outPath}");
        _output.WriteLine($"Report written: {outPath}");
    }

    /// <summary>
    /// A tiny 4-candle choppy fixture should produce a synthetic candidate with a PivotsInsufficient
    /// violation (because insufficient pivots can be extracted). HTML report is still written.
    /// </summary>
    [Fact]
    public async Task RejectFixture_InsufficientPivots_ProducesSyntheticCandidate()
    {
        // Arrange
        var candles = LoadCandles("fixtures/elliott/reject_insufficient_pivots.json");
        var evaluationTime = candles[^1].OpenTimeUtc.AddMinutes(1);

        var options = new ElliottOptions
        {
            MinBars = 10,
            MaxBars = 200,
            MinPivotCount = 8  // intentionally high so the few candles fail to produce enough pivots
        };

        var parameters = new ElliottParameters("ZigZag", Depth: 1, DeviationPct: 0.05m, MaxCandidates: 10);

        // Extract pivots (should be very few)
        var pivotExtractor = new ZigZagPivotExtractor(options);
        var pivots = pivotExtractor.Extract(candles, Timeframe.M1, parameters, evaluationTime);

        _output.WriteLine($"Pivots extracted: {pivots.Count}");
        Assert.True(pivots.Count < options.MinPivotCount,
            $"Reject fixture unexpectedly produced {pivots.Count} pivots (expected < {options.MinPivotCount}).");

        // Run the full engine
        var engine = BuildEngine(new FixtureMarketDataProvider(candles), options);
        var result = await engine.GenerateCandidatesAsync(
            "VISUAL.REJECT",
            Timeframe.M1,
            parameters,
            evaluationTime,
            CancellationToken.None);

        // Assert: should produce a single synthetic candidate with PivotsInsufficient violation
        Assert.NotNull(result);
        var candidate = Assert.Single(result.Candidates);
        var violation = Assert.Single(candidate.RuleViolations);
        Assert.Equal(ElliottRuleCodes.PivotsInsufficient, violation.Rule);
        Assert.Equal("ERROR", violation.Severity);
        Assert.Equal(0m, candidate.Score);
        Assert.Equal(0m, candidate.Confidence);

        _output.WriteLine($"Candidate: {candidate.WaveLabel}, violation: {violation.Rule}");

        // Write visual report (even for failures — useful for debugging)
        var outDir = Path.Combine(AppContext.BaseDirectory, "visual-reports");
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(outDir, "reject_insufficient_pivots.html");
        ElliottVisualizer.WriteReport(outPath, "VISUAL.REJECT", candles, pivots, result);

        Assert.True(File.Exists(outPath), $"Expected HTML report at {outPath}");
        _output.WriteLine($"Report written: {outPath}");
    }

    // ──────────── Helpers ────────────

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

    private static List<Candle> LoadCandles(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Fixture not found: {path}");
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<Candle>>(json) ?? new List<Candle>();
    }

    private sealed class FixtureMarketDataProvider : IMarketDataProvider
    {
        private readonly IReadOnlyList<Candle> _candles;

        public FixtureMarketDataProvider(IReadOnlyList<Candle> candles) => _candles = candles;

        public string ExchangeId => "visual-fixture";

        public Task<Result<IReadOnlyList<Instrument>>> GetInstrumentsAsync(CancellationToken ct)
            => Task.FromResult(new Result<IReadOnlyList<Instrument>>(true, Array.Empty<Instrument>(), null));

        public Task<Result<IReadOnlyList<Ticker>>> GetTickersAsync(CancellationToken ct)
            => Task.FromResult(new Result<IReadOnlyList<Ticker>>(true, Array.Empty<Ticker>(), null));

        public Task<Result<IReadOnlyList<Candle>>> GetOhlcvAsync(string symbol, Timeframe timeframe, int lookbackBars, CancellationToken ct)
        {
            var slice = _candles.Count > lookbackBars
                ? _candles.Skip(_candles.Count - lookbackBars).ToList()
                : _candles.ToList();
            return Task.FromResult(new Result<IReadOnlyList<Candle>>(true, slice, null));
        }
    }
}
