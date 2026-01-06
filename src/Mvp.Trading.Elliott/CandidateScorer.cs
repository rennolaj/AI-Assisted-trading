using System;
using System.Linq;

namespace Mvp.Trading.Elliott;

/// <summary>
/// Deterministic scoring and confidence calculator for Elliott candidates.
/// </summary>
public sealed class CandidateScorer
{
    private readonly ElliottOptions _options;

    public CandidateScorer(ElliottOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public CandidateScore Score(ImpulseCandidateContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var weights = _options.ScoreWeights;
        var penalties = _options.Penalties;
        var fib = _options.FibGuidelines;

        decimal score = 0m;
        if (context.IsStructuralValid)
        {
            score += weights.StructuralValidity;
        }

        if (!HasError(context))
        {
            score += weights.HardRulePassBonus;
        }

        var w1 = WaveLength(context.Wave.P0, context.Wave.P1);
        var w3 = WaveLength(context.Wave.P2, context.Wave.P3);
        var w5 = context.Wave.P4 is not null && context.Wave.P5 is not null
            ? WaveLength(context.Wave.P4, context.Wave.P5)
            : (decimal?)null;

        if (w1 > 0m && w3 >= w1 * fib.Wave3MinMultiple)
        {
            score += fib.Wave3Points;
        }

        if (w1 > 0m && w5 is not null && w5.Value >= w1 * fib.Wave5EqualityLower && w5.Value <= w1 * fib.Wave5EqualityUpper)
        {
            score += fib.Wave5Points;
        }

        score += 0m; // ChannelFit placeholder
        score += 0m; // Wave3Strength placeholder
        score += 0m; // AlternationProxy placeholder
        score += 0m; // PivotQuality placeholder

        var warnCount = context.RuleViolations.Count(v => IsSeverity(v.Severity, "WARN"));
        var errorCount = context.RuleViolations.Count(v => IsSeverity(v.Severity, "ERROR"));
        score += warnCount * penalties.WarnPenalty;
        score += errorCount * penalties.ErrorPenalty;

        score = Clamp(score, 0m, 100m);
        var scoreInt = (int)Math.Round(score, 0, MidpointRounding.ToEven);

        var confidence = scoreInt / 100m;
        if (context.PivotCount < _options.Confidence.PivotCountThreshold)
        {
            confidence *= _options.Confidence.PivotCountFactor;
        }

        if (context.Depth > _options.Confidence.DepthThreshold)
        {
            confidence *= _options.Confidence.DepthFactor;
        }

        confidence = Clamp(confidence, 0m, 1m);
        confidence = Math.Round(confidence, _options.SnapshotPrecisionDecimals, MidpointRounding.ToEven);

        return new CandidateScore(scoreInt, confidence);
    }

    private static decimal WaveLength(PivotPoint start, PivotPoint end)
    {
        return Math.Abs(end.Price - start.Price);
    }

    private static bool HasError(ImpulseCandidateContext context)
    {
        return context.RuleViolations.Any(v => IsSeverity(v.Severity, "ERROR"));
    }

    private static bool IsSeverity(string severity, string expected)
    {
        return string.Equals(severity, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static decimal Clamp(decimal value, decimal min, decimal max)
    {
        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }
}

/// <summary>
/// Score results for an Elliott candidate.
/// </summary>
public sealed record CandidateScore(int Score, decimal Confidence);
