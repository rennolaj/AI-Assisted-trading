using System;
using Mvp.Trading.Elliott;
using Mvp.Trading.Contracts;
using Xunit;

namespace Mvp.Trading.Elliott.Tests;

/// <summary>
/// Tests for invalidation level calculations.
/// </summary>
public sealed class InvalidationCalculatorTests
{
    [Fact]
    public void W3Long_InvalidationUsesWave2LowAndBuffer()
    {
        var context = BuildW3Context(isUptrend: true, wave2Price: 105m, wave5Price: 0m);
        var calculator = new InvalidationCalculator(new ElliottOptions());

        var levels = calculator.Compute(context, 0.5m);

        Assert.Equal(104m, levels.LongInvalidationPrice);
        Assert.Null(levels.ShortInvalidationPrice);
    }

    [Fact]
    public void W5EndShort_InvalidationUsesWave5HighAndBuffer()
    {
        var context = BuildW5Context(isUptrend: true, wave5Price: 150m);
        var calculator = new InvalidationCalculator(new ElliottOptions());

        var levels = calculator.Compute(context, 0.5m);

        Assert.Equal(151m, levels.ShortInvalidationPrice);
        Assert.Null(levels.LongInvalidationPrice);
    }

    private static ImpulseCandidateContext BuildW3Context(bool isUptrend, decimal wave2Price, decimal wave5Price)
    {
        var start = DateTimeOffset.Parse("2026-01-06T00:00:00Z");
        var p0 = new PivotPoint(0, start, 100m, isUptrend ? PivotType.Low : PivotType.High);
        var p1 = new PivotPoint(1, start.AddMinutes(1), 110m, isUptrend ? PivotType.High : PivotType.Low);
        var p2 = new PivotPoint(2, start.AddMinutes(2), wave2Price, isUptrend ? PivotType.Low : PivotType.High);
        var p3 = new PivotPoint(3, start.AddMinutes(3), 120m, isUptrend ? PivotType.High : PivotType.Low);

        var wave = new ImpulseWave(p0, p1, p2, p3, null, null, isUptrend);
        return new ImpulseCandidateContext(wave, "W3", Array.Empty<RuleViolation>(), 12, 5, false, true);
    }

    private static ImpulseCandidateContext BuildW5Context(bool isUptrend, decimal wave5Price)
    {
        var start = DateTimeOffset.Parse("2026-01-06T00:00:00Z");
        var p0 = new PivotPoint(0, start, 100m, isUptrend ? PivotType.Low : PivotType.High);
        var p1 = new PivotPoint(1, start.AddMinutes(1), 120m, isUptrend ? PivotType.High : PivotType.Low);
        var p2 = new PivotPoint(2, start.AddMinutes(2), 110m, isUptrend ? PivotType.Low : PivotType.High);
        var p3 = new PivotPoint(3, start.AddMinutes(3), 135m, isUptrend ? PivotType.High : PivotType.Low);
        var p4 = new PivotPoint(4, start.AddMinutes(4), 125m, isUptrend ? PivotType.Low : PivotType.High);
        var p5 = new PivotPoint(5, start.AddMinutes(5), wave5Price, isUptrend ? PivotType.High : PivotType.Low);

        var wave = new ImpulseWave(p0, p1, p2, p3, p4, p5, isUptrend);
        return new ImpulseCandidateContext(wave, "W5END", Array.Empty<RuleViolation>(), 12, 5, true, true);
    }
}
