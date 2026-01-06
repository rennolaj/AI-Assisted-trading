using System;
using System.Collections.Generic;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Elliott;

/// <summary>
/// Builds impulse candidate windows from pivot sequences.
/// </summary>
public sealed class ImpulseCandidateBuilder
{
    private readonly ElliottOptions _options;

    public ImpulseCandidateBuilder(ElliottOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IReadOnlyList<ImpulseCandidateContext> BuildCandidates(
        IReadOnlyList<PivotPoint> pivots,
        ElliottParameters parameters)
    {
        if (pivots is null)
        {
            throw new ArgumentNullException(nameof(pivots));
        }

        if (parameters is null)
        {
            throw new ArgumentNullException(nameof(parameters));
        }

        var results = new List<ImpulseCandidateContext>();
        if (pivots.Count < 4)
        {
            return results;
        }

        results.AddRange(BuildW3Candidates(pivots, parameters));
        results.AddRange(BuildW5Candidates(pivots, parameters));

        return results;
    }

    private IEnumerable<ImpulseCandidateContext> BuildW3Candidates(
        IReadOnlyList<PivotPoint> pivots,
        ElliottParameters parameters)
    {
        for (var i = 0; i + 3 < pivots.Count; i++)
        {
            var p0 = pivots[i];
            var p1 = pivots[i + 1];
            var p2 = pivots[i + 2];
            var p3 = pivots[i + 3];

            if (!IsImpulseWindow(p0, p1, p2, p3, out var isUptrend))
            {
                continue;
            }

            var violations = new List<RuleViolation>();
            EvaluateWave2NotBeyondWave1(p0, p2, isUptrend, violations);

            var waveLabel = BreaksWave1End(p1.Price, p3.Price, isUptrend)
                ? "W3"
                : "OTHER";

            var wave = new ImpulseWave(p0, p1, p2, p3, null, null, isUptrend);
            yield return new ImpulseCandidateContext(
                wave,
                waveLabel,
                violations,
                pivots.Count,
                parameters.Depth,
                IsFullPattern: false,
                IsStructuralValid: true);
        }
    }

    private IEnumerable<ImpulseCandidateContext> BuildW5Candidates(
        IReadOnlyList<PivotPoint> pivots,
        ElliottParameters parameters)
    {
        if (pivots.Count < 6)
        {
            yield break;
        }

        for (var i = 0; i + 5 < pivots.Count; i++)
        {
            var p0 = pivots[i];
            var p1 = pivots[i + 1];
            var p2 = pivots[i + 2];
            var p3 = pivots[i + 3];
            var p4 = pivots[i + 4];
            var p5 = pivots[i + 5];

            if (!IsImpulseWindow(p0, p1, p2, p3, p4, p5, out var isUptrend))
            {
                continue;
            }

            var violations = new List<RuleViolation>();
            EvaluateWave2NotBeyondWave1(p0, p2, isUptrend, violations);
            EvaluateWave3NotShortest(p0, p1, p2, p3, p4, p5, violations);
            EvaluateWave4Overlap(p1, p4, isUptrend, violations);

            var waveLabel = violations.Count == 0 ? "W5END" : "OTHER";
            var wave = new ImpulseWave(p0, p1, p2, p3, p4, p5, isUptrend);

            yield return new ImpulseCandidateContext(
                wave,
                waveLabel,
                violations,
                pivots.Count,
                parameters.Depth,
                IsFullPattern: true,
                IsStructuralValid: true);
        }
    }

    private static bool IsImpulseWindow(PivotPoint p0, PivotPoint p1, PivotPoint p2, PivotPoint p3, out bool isUptrend)
    {
        if (p0.Type == PivotType.Low &&
            p1.Type == PivotType.High &&
            p2.Type == PivotType.Low &&
            p3.Type == PivotType.High)
        {
            isUptrend = true;
            return true;
        }

        if (p0.Type == PivotType.High &&
            p1.Type == PivotType.Low &&
            p2.Type == PivotType.High &&
            p3.Type == PivotType.Low)
        {
            isUptrend = false;
            return true;
        }

        isUptrend = false;
        return false;
    }

    private static bool IsImpulseWindow(
        PivotPoint p0,
        PivotPoint p1,
        PivotPoint p2,
        PivotPoint p3,
        PivotPoint p4,
        PivotPoint p5,
        out bool isUptrend)
    {
        if (p0.Type == PivotType.Low &&
            p1.Type == PivotType.High &&
            p2.Type == PivotType.Low &&
            p3.Type == PivotType.High &&
            p4.Type == PivotType.Low &&
            p5.Type == PivotType.High)
        {
            isUptrend = true;
            return true;
        }

        if (p0.Type == PivotType.High &&
            p1.Type == PivotType.Low &&
            p2.Type == PivotType.High &&
            p3.Type == PivotType.Low &&
            p4.Type == PivotType.High &&
            p5.Type == PivotType.Low)
        {
            isUptrend = false;
            return true;
        }

        isUptrend = false;
        return false;
    }

    private static bool BreaksWave1End(decimal wave1End, decimal wave3End, bool isUptrend)
    {
        return isUptrend ? wave3End > wave1End : wave3End < wave1End;
    }

    private static void EvaluateWave2NotBeyondWave1(
        PivotPoint wave1Start,
        PivotPoint wave2End,
        bool isUptrend,
        List<RuleViolation> violations)
    {
        if (isUptrend && wave2End.Price < wave1Start.Price)
        {
            violations.Add(new RuleViolation(
                ElliottRuleCodes.ImpulseWave2BeyondWave1Start,
                "ERROR",
                "Wave 2 retraced below wave 1 start."));
        }

        if (!isUptrend && wave2End.Price > wave1Start.Price)
        {
            violations.Add(new RuleViolation(
                ElliottRuleCodes.ImpulseWave2BeyondWave1Start,
                "ERROR",
                "Wave 2 retraced above wave 1 start."));
        }
    }

    private static void EvaluateWave3NotShortest(
        PivotPoint p0,
        PivotPoint p1,
        PivotPoint p2,
        PivotPoint p3,
        PivotPoint p4,
        PivotPoint p5,
        List<RuleViolation> violations)
    {
        var w1 = Math.Abs(p1.Price - p0.Price);
        var w3 = Math.Abs(p3.Price - p2.Price);
        var w5 = Math.Abs(p5.Price - p4.Price);

        var shortest = Math.Min(w1, w5);
        if (w3 <= shortest)
        {
            violations.Add(new RuleViolation(
                ElliottRuleCodes.ImpulseWave3NotShortest,
                "ERROR",
                "Wave 3 is the shortest among waves 1, 3, and 5."));
        }
    }

    private static void EvaluateWave4Overlap(PivotPoint wave1End, PivotPoint wave4End, bool isUptrend, List<RuleViolation> violations)
    {
        if (isUptrend && wave4End.Price <= wave1End.Price)
        {
            violations.Add(new RuleViolation(
                ElliottRuleCodes.ImpulseWave4OverlapWave1,
                "ERROR",
                "Wave 4 overlapped wave 1 territory."));
        }

        if (!isUptrend && wave4End.Price >= wave1End.Price)
        {
            violations.Add(new RuleViolation(
                ElliottRuleCodes.ImpulseWave4OverlapWave1,
                "ERROR",
                "Wave 4 overlapped wave 1 territory."));
        }
    }
}
