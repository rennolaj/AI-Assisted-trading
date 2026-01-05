using System.Collections.Generic;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Indicators;

/// <summary>
/// Configuration for indicator snapshot computation.
/// </summary>
public sealed record IndicatorConfig(
    string Mode,
    IReadOnlyList<Timeframe> Timeframes,
    Timeframe AnchorTimeframe,
    Timeframe TrendTimeframe,
    int LookbackBars,
    int EvaluationWindowMinutes,
    int EvaluationIntervalMinutes,
    int SnapshotPrecision,
    IndicatorParameters Parameters,
    IndicatorThresholds Thresholds,
    IndicatorWeights Weights,
    IReadOnlyList<IndicatorRiskProfile> RiskProfiles,
    int StochRsiKPeriod,
    int StochRsiDPeriod
);

/// <summary>
/// Thresholds used for indicator state classification.
/// </summary>
public sealed record IndicatorThresholds(
    decimal RsiOversold,
    decimal RsiOverbought,
    decimal StochRsiOversold,
    decimal StochRsiOverbought
);

/// <summary>
/// Scoring weights for confirmation rules.
/// </summary>
public sealed record IndicatorWeights(
    int RsiMultiTf,
    int StochRsiReversal,
    int MacdMomentum,
    int TrendAlignment,
    int VolumeConfirm
);

/// <summary>
/// Risk profile thresholds and actions.
/// </summary>
public sealed record IndicatorRiskProfile(
    string Category,
    int MinScore,
    int MaxScore,
    int MinConfirmations,
    bool TrendRequired,
    bool CounterTrendAllowed,
    decimal RiskMultiplier,
    string Action
);
