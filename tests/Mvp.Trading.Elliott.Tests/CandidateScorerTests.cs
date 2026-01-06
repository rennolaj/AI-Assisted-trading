using System;
using System.Collections.Generic;
using Mvp.Trading.Elliott;
using Mvp.Trading.Contracts;
using Xunit;

namespace Mvp.Trading.Elliott.Tests;

/// <summary>
/// Tests for scoring and confidence calculations.
/// </summary>
public sealed class CandidateScorerTests
{
    [Fact]
    public void Score_AddsStructuralAndFibGuidelines()
    {
        var context = BuildContext(
            w1: 10m,
            w3: 12m,
            w5: 9m,
            violations: Array.Empty<RuleViolation>());

        var scorer = new CandidateScorer(new ElliottOptions());
        var score = scorer.Score(context);

        Assert.Equal(65, score.Score);
        Assert.Equal(0.65m, score.Confidence);
    }

    [Fact]
    public void Score_AppliesPenalties()
    {
        var violations = new[]
        {
            new RuleViolation("WARN_TEST", "WARN", "warn"),
            new RuleViolation("ERROR_TEST", "ERROR", "error")
        };
        var context = BuildContext(10m, 12m, 9m, violations);

        var scorer = new CandidateScorer(new ElliottOptions());
        var score = scorer.Score(context);

        Assert.Equal(25, score.Score);
    }

    [Fact]
    public void Confidence_AppliesPivotAndDepthPenalties()
    {
        var context = BuildContext(10m, 12m, 9m, Array.Empty<RuleViolation>(), pivotCount: 8, depth: 40);

        var scorer = new CandidateScorer(new ElliottOptions());
        var score = scorer.Score(context);

        Assert.Equal(0.49725m, score.Confidence);
    }

    private static ImpulseCandidateContext BuildContext(
        decimal w1,
        decimal w3,
        decimal w5,
        IReadOnlyList<RuleViolation> violations,
        int pivotCount = 12,
        int depth = 5)
    {
        var start = DateTimeOffset.Parse("2026-01-06T00:00:00Z");
        var p0 = new PivotPoint(0, start, 100m, PivotType.Low);
        var p1 = new PivotPoint(1, start.AddMinutes(1), 100m + w1, PivotType.High);
        var p2 = new PivotPoint(2, start.AddMinutes(2), 100m + w1 - 2m, PivotType.Low);
        var p3 = new PivotPoint(3, start.AddMinutes(3), 100m + w1 - 2m + w3, PivotType.High);
        var p4 = new PivotPoint(4, start.AddMinutes(4), 100m + w1 - 1m, PivotType.Low);
        var p5 = new PivotPoint(5, start.AddMinutes(5), 100m + w1 - 1m + w5, PivotType.High);

        var wave = new ImpulseWave(p0, p1, p2, p3, p4, p5, true);
        return new ImpulseCandidateContext(wave, "W5END", violations, pivotCount, depth, IsFullPattern: true, IsStructuralValid: true);
    }
}
