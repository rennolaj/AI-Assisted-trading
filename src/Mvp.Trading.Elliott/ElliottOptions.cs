using System;
using System.Collections.Generic;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Elliott;

/// <summary>
/// Configuration constants for Elliott candidate generation.
/// </summary>
public sealed class ElliottOptions
{
    /// <summary>
    /// Default decimal precision for deterministic rounding.
    /// </summary>
    public const int DefaultSnapshotPrecisionDecimals = 6;

    /// <summary>
    /// Minimum number of bars to request for pivot extraction.
    /// </summary>
    public int MinBars { get; init; } = 1;

    /// <summary>
    /// Number of lookback days to request per timeframe.
    /// </summary>
    public int LookbackDays { get; init; } = 1;

    /// <summary>
    /// Multiplier applied to Depth for lookback sizing.
    /// </summary>
    public int DepthMultiplier { get; init; } = 30;

    /// <summary>
    /// Maximum number of bars to request for pivot extraction.
    /// </summary>
    public int MaxBars { get; init; } = 5000;

    /// <summary>
    /// Minimum pivot count required before generating candidates.
    /// </summary>
    public int MinPivotCount { get; init; } = 12;

    /// <summary>
    /// Decimal rounding precision.
    /// </summary>
    public int SnapshotPrecisionDecimals { get; init; } = DefaultSnapshotPrecisionDecimals;

    /// <summary>
    /// Number of ticks applied as invalidation buffer.
    /// </summary>
    public int InvalidationBufferTicks { get; init; } = 2;

    /// <summary>
    /// Fallback tick size when instrument metadata is unavailable.
    /// </summary>
    public decimal TickSizeFallback { get; init; } = 0m;

    /// <summary>
    /// Optional per-symbol tick size overrides (case-insensitive).
    /// </summary>
    public IReadOnlyDictionary<string, decimal> TickSizeOverrides { get; init; }
        = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Supported base timeframes for Elliott generation.
    /// </summary>
    public IReadOnlyCollection<Timeframe> SupportedTimeframes { get; init; } = new HashSet<Timeframe>
    {
        Timeframe.M1,
        Timeframe.M5,
        Timeframe.M15,
        Timeframe.H1,
        Timeframe.H4,
        Timeframe.D1
    };

    /// <summary>
    /// Deterministic scoring weights.
    /// </summary>
    public ElliottScoreWeights ScoreWeights { get; init; } = ElliottScoreWeights.Default;

    /// <summary>
    /// Scoring penalties for rule violations.
    /// </summary>
    public ElliottScorePenalties Penalties { get; init; } = ElliottScorePenalties.Default;

    /// <summary>
    /// Fib guideline thresholds.
    /// </summary>
    public ElliottFibGuidelines FibGuidelines { get; init; } = ElliottFibGuidelines.Default;

    /// <summary>
    /// Confidence adjustment rules.
    /// </summary>
    public ElliottConfidenceRules Confidence { get; init; } = ElliottConfidenceRules.Default;
}

/// <summary>
/// Weights for Elliott scoring.
/// </summary>
public sealed record ElliottScoreWeights(
    int StructuralValidity,
    int HardRulePassBonus,
    int FibGuidelines,
    int ChannelFit,
    int Wave3Strength,
    int AlternationProxy,
    int PivotQuality)
{
    public static readonly ElliottScoreWeights Default = new(
        StructuralValidity: 35,
        HardRulePassBonus: 10,
        FibGuidelines: 20,
        ChannelFit: 10,
        Wave3Strength: 15,
        AlternationProxy: 5,
        PivotQuality: 5);
}

/// <summary>
/// Scoring penalties for rule violations.
/// </summary>
public sealed record ElliottScorePenalties(
    int WarnPenalty,
    int ErrorPenalty,
    int DiagonalPenalty)
{
    public static readonly ElliottScorePenalties Default = new(
        WarnPenalty: -5,
        ErrorPenalty: -25,
        DiagonalPenalty: -10);
}

/// <summary>
/// Fib guideline thresholds used in scoring.
/// </summary>
public sealed record ElliottFibGuidelines(
    decimal Wave3MinMultiple,
    decimal Wave5EqualityLower,
    decimal Wave5EqualityUpper,
    int Wave3Points,
    int Wave5Points)
{
    public static readonly ElliottFibGuidelines Default = new(
        Wave3MinMultiple: 1.0m,
        Wave5EqualityLower: 0.85m,
        Wave5EqualityUpper: 1.15m,
        Wave3Points: 12,
        Wave5Points: 8);
}

/// <summary>
/// Confidence penalties for low pivot count or high depth.
/// </summary>
public sealed record ElliottConfidenceRules(
    int PivotCountThreshold,
    decimal PivotCountFactor,
    int DepthThreshold,
    decimal DepthFactor)
{
    public static readonly ElliottConfidenceRules Default = new(
        PivotCountThreshold: 10,
        PivotCountFactor: 0.85m,
        DepthThreshold: 30,
        DepthFactor: 0.9m);
}
