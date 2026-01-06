using System;
using System.Collections.Generic;
using System.Linq;
using Mvp.Trading.Contracts;
using Mvp.Trading.Elliott;
using Xunit;

namespace Mvp.Trading.Elliott.Tests;

/// <summary>
/// Determinism tests for Elliott candidate generation JSON.
/// </summary>
public sealed class ElliottDeterminismTests
{
    [Fact]
    public void CandidateJson_IsDeterministic()
    {
        var pivots = BuildUptrendPivots(100m, 120m, 110m, 135m, 125m, 150m);
        var options = new ElliottOptions();
        var parameters = new ElliottParameters("ZigZag", 5, 5m, 5);
        var builder = new ImpulseCandidateBuilder(options);
        var scorer = new CandidateScorer(options);
        var invalidation = new InvalidationCalculator(options);

        string? first = null;
        for (var i = 0; i < 20; i++)
        {
            var candidate = BuildCandidate(pivots, builder, scorer, invalidation);
            var json = ElliottCandidatesJson.Serialize(candidate);
            if (first is null)
            {
                first = json;
            }
            else
            {
                Assert.Equal(first, json);
            }
        }
    }

    private static ElliottCandidates BuildCandidate(
        IReadOnlyList<PivotPoint> pivots,
        ImpulseCandidateBuilder builder,
        CandidateScorer scorer,
        InvalidationCalculator invalidation)
    {
        var contexts = builder.BuildCandidates(pivots, new ElliottParameters("ZigZag", 5, 5m, 5));
        var context = contexts.First(c => c.IsFullPattern);
        var score = scorer.Score(context);
        var invalidationLevels = invalidation.Compute(context, 0.5m);

        var candidate = new ElliottCandidate(
            "det-001",
            "IMPULSE",
            context.WaveLabel,
            score.Score,
            score.Confidence,
            context.RuleViolations,
            invalidationLevels);

        return new ElliottCandidates(Timeframe.M15, new[] { candidate });
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
}
