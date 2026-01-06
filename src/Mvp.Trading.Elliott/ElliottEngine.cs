using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Elliott;

/// <summary>
/// Generates deterministic Elliott candidates using market data and configured rules.
/// </summary>
public sealed class ElliottEngine : IElliottEngine
{
    private const string PatternTypeImpulse = "IMPULSE";

    private readonly IMarketDataProvider _marketData;
    private readonly ElliottOptions _options;
    private readonly IPivotExtractor _pivotExtractor;
    private readonly ImpulseCandidateBuilder _candidateBuilder;
    private readonly CandidateScorer _scorer;
    private readonly InvalidationCalculator _invalidation;

    public ElliottEngine(
        IMarketDataProvider marketData,
        ElliottOptions options,
        IPivotExtractor pivotExtractor,
        ImpulseCandidateBuilder candidateBuilder,
        CandidateScorer scorer,
        InvalidationCalculator invalidation)
    {
        _marketData = marketData ?? throw new ArgumentNullException(nameof(marketData));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _pivotExtractor = pivotExtractor ?? throw new ArgumentNullException(nameof(pivotExtractor));
        _candidateBuilder = candidateBuilder ?? throw new ArgumentNullException(nameof(candidateBuilder));
        _scorer = scorer ?? throw new ArgumentNullException(nameof(scorer));
        _invalidation = invalidation ?? throw new ArgumentNullException(nameof(invalidation));
    }

    public async Task<ElliottCandidates> GenerateCandidatesAsync(
        string symbol,
        Timeframe baseTimeframe,
        ElliottParameters parameters,
        DateTimeOffset evaluationTimeUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Symbol is required.", nameof(symbol));
        }

        if (parameters is null)
        {
            throw new ArgumentNullException(nameof(parameters));
        }

        if (parameters.Depth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters.Depth), "Depth must be greater than zero.");
        }

        if (parameters.DeviationPct <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters.DeviationPct), "DeviationPct must be greater than zero.");
        }

        if (!_options.SupportedTimeframes.Contains(baseTimeframe))
        {
            return CreateRunFailureCandidate(
                symbol,
                baseTimeframe,
                parameters,
                ElliottRuleCodes.TimeframeUnsupported,
                $"Base timeframe {baseTimeframe} is not supported.");
        }

        if (parameters.MaxCandidates <= 0)
        {
            return new ElliottCandidates(baseTimeframe, Array.Empty<ElliottCandidate>());
        }

        var lookbackBars = LookbackSizer.ComputeLookbackBars(parameters.Depth, _options);
        var candleResult = await _marketData.GetOhlcvAsync(symbol, baseTimeframe, lookbackBars, ct);
        if (!candleResult.Ok || candleResult.Value is null)
        {
            var details = candleResult.Error is null
                ? "Market data unavailable."
                : $"Market data error: {candleResult.Error.Code}.";
            return CreateRunFailureCandidate(
                symbol,
                baseTimeframe,
                parameters,
                ElliottRuleCodes.PivotsInsufficient,
                details);
        }

        var pivots = _pivotExtractor.Extract(candleResult.Value, baseTimeframe, parameters, evaluationTimeUtc);
        if (pivots.Count < _options.MinPivotCount)
        {
            return CreateRunFailureCandidate(
                symbol,
                baseTimeframe,
                parameters,
                ElliottRuleCodes.PivotsInsufficient,
                $"Only {pivots.Count} pivots available.");
        }

        var contexts = _candidateBuilder.BuildCandidates(pivots, parameters);
        if (contexts.Count == 0)
        {
            return new ElliottCandidates(baseTimeframe, Array.Empty<ElliottCandidate>());
        }

        var tickSize = ResolveTickSize(symbol);
        var candidates = new List<CandidateEnvelope>(contexts.Count);

        foreach (var context in contexts)
        {
            var score = _scorer.Score(context);
            var invalidation = _invalidation.Compute(context, tickSize);
            var seed = BuildCandidateSeed(symbol, baseTimeframe, parameters, context);
            var candidateId = HashSeed(seed);

            var candidate = new ElliottCandidate(
                candidateId,
                PatternTypeImpulse,
                context.WaveLabel,
                score.Score,
                score.Confidence,
                context.RuleViolations,
                invalidation);

            candidates.Add(new CandidateEnvelope(seed, candidate));
        }

        var ordered = candidates
            .OrderBy(c => c.Seed, StringComparer.Ordinal)
            .Select(c => c.Candidate)
            .Take(parameters.MaxCandidates)
            .ToList();

        return new ElliottCandidates(baseTimeframe, ordered);
    }

    private ElliottCandidates CreateRunFailureCandidate(
        string symbol,
        Timeframe baseTimeframe,
        ElliottParameters parameters,
        string rule,
        string details)
    {
        var seed = BuildRunFailureSeed(symbol, baseTimeframe, parameters, rule);
        var candidate = new ElliottCandidate(
            HashSeed(seed),
            PatternTypeImpulse,
            "OTHER",
            0m,
            0m,
            new[] { new RuleViolation(rule, "ERROR", details) },
            new InvalidationLevels(null, null));

        return new ElliottCandidates(baseTimeframe, new[] { candidate });
    }

    private decimal ResolveTickSize(string symbol)
    {
        if (_options.TickSizeOverrides.Count == 0)
        {
            return _options.TickSizeFallback;
        }

        if (_options.TickSizeOverrides.TryGetValue(symbol, out var tickSize))
        {
            return tickSize;
        }

        foreach (var pair in _options.TickSizeOverrides)
        {
            if (string.Equals(pair.Key, symbol, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return _options.TickSizeFallback;
    }

    private string BuildCandidateSeed(
        string symbol,
        Timeframe baseTimeframe,
        ElliottParameters parameters,
        ImpulseCandidateContext context)
    {
        var pivots = new List<PivotPoint>
        {
            context.Wave.P0,
            context.Wave.P1,
            context.Wave.P2,
            context.Wave.P3
        };

        if (context.Wave.P4 is not null && context.Wave.P5 is not null)
        {
            pivots.Add(context.Wave.P4);
            pivots.Add(context.Wave.P5);
        }

        var builder = new StringBuilder();
        builder.Append(symbol).Append('|')
            .Append(baseTimeframe).Append('|')
            .Append(parameters.PivotMethod).Append('|')
            .Append(parameters.Depth).Append('|')
            .Append(FormatDecimal(parameters.DeviationPct)).Append('|')
            .Append(parameters.MaxCandidates).Append('|');

        foreach (var pivot in pivots)
        {
            builder.Append(pivot.Index)
                .Append(',')
                .Append(pivot.TimeUtc.ToUnixTimeSeconds())
                .Append(',')
                .Append(FormatDecimal(pivot.Price))
                .Append(',')
                .Append(pivot.Type)
                .Append('|');
        }

        return builder.ToString();
    }

    private string BuildRunFailureSeed(
        string symbol,
        Timeframe baseTimeframe,
        ElliottParameters parameters,
        string rule)
    {
        return string.Join(
            "|",
            "run-failure",
            symbol,
            baseTimeframe,
            parameters.PivotMethod,
            parameters.Depth.ToString(CultureInfo.InvariantCulture),
            FormatDecimal(parameters.DeviationPct),
            parameters.MaxCandidates.ToString(CultureInfo.InvariantCulture),
            rule);
    }

    private string FormatDecimal(decimal value)
    {
        var rounded = Math.Round(value, _options.SnapshotPrecisionDecimals, MidpointRounding.ToEven);
        return rounded.ToString($"F{_options.SnapshotPrecisionDecimals}", CultureInfo.InvariantCulture);
    }

    private static string HashSeed(string seed)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return hex[..16];
    }

    private sealed record CandidateEnvelope(string Seed, ElliottCandidate Candidate);
}
