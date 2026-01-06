using System;
using System.Collections.Generic;
using System.Linq;
using Mvp.Trading.Contracts;
using Mvp.Trading.Elliott;
using Xunit;

namespace Mvp.Trading.Elliott.Tests;

/// <summary>
/// Tests for impulse hard rule violations.
/// </summary>
public sealed class ImpulseRuleViolationTests
{
    [Fact]
    public void Wave2BeyondWave1Start_IsViolation()
    {
        var pivots = BuildUptrendPivots(100m, 120m, 90m, 140m, 130m, 150m);
        var builder = new ImpulseCandidateBuilder(new ElliottOptions());
        var parameters = new ElliottParameters("ZigZag", 5, 5m, 5);

        var candidates = builder.BuildCandidates(pivots, parameters);
        var full = Assert.Single(candidates, c => c.IsFullPattern);

        Assert.Contains(full.RuleViolations, v => v.Rule == ElliottRuleCodes.ImpulseWave2BeyondWave1Start);
    }

    [Fact]
    public void Wave4OverlapWave1_IsViolation()
    {
        var pivots = BuildUptrendPivots(100m, 120m, 110m, 135m, 115m, 145m);
        var builder = new ImpulseCandidateBuilder(new ElliottOptions());
        var parameters = new ElliottParameters("ZigZag", 5, 5m, 5);

        var candidates = builder.BuildCandidates(pivots, parameters);
        var full = Assert.Single(candidates, c => c.IsFullPattern);

        Assert.Contains(full.RuleViolations, v => v.Rule == ElliottRuleCodes.ImpulseWave4OverlapWave1);
    }

    [Fact]
    public void Wave3Shortest_IsViolation()
    {
        var pivots = BuildUptrendPivots(100m, 110m, 105m, 112m, 111m, 123m);
        var builder = new ImpulseCandidateBuilder(new ElliottOptions());
        var parameters = new ElliottParameters("ZigZag", 5, 5m, 5);

        var candidates = builder.BuildCandidates(pivots, parameters);
        var full = Assert.Single(candidates, c => c.IsFullPattern);

        Assert.Contains(full.RuleViolations, v => v.Rule == ElliottRuleCodes.ImpulseWave3NotShortest);
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
