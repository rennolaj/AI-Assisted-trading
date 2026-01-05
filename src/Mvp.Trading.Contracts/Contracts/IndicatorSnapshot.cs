using System;
using System.Collections.Generic;

namespace Mvp.Trading.Contracts;

/// <summary>
/// Deterministic multi-timeframe indicator snapshot for a single alert.
/// </summary>
public sealed record IndicatorSnapshot(
    Guid AlertId,
    Guid CorrelationId,
    string Symbol,
    string Mode,
    string Direction,
    Timeframe AnchorTimeframe,
    Timeframe TrendTimeframe,
    DateTimeOffset EvaluationTimeUtc,
    DateTimeOffset ComputedAtUtc,
    IndicatorParameters Parameters,
    IReadOnlyList<TimeframeSnapshot> Timeframes,
    IndicatorGates Gates,
    IndicatorConfirmations Confirmations,
    IndicatorScore Score,
    IndicatorRisk Risk
);

/// <summary>
/// Gate results that must be satisfied before scoring is considered valid.
/// </summary>
public sealed record IndicatorGates(
    bool DataIntegrity,
    bool RsiExtremeAnchor,
    bool DirectionDeclared
);

/// <summary>
/// Confirmation rule outcomes for scoring.
/// </summary>
public sealed record IndicatorConfirmations(
    bool RsiMultiTf,
    bool StochRsiReversal,
    bool MacdMomentum,
    bool TrendAlignment,
    bool VolumeConfirm,
    bool CounterTrendVolumeOk
);

/// <summary>
/// Aggregated score details for the snapshot.
/// </summary>
public sealed record IndicatorScore(
    int Confirmations,
    int Score,
    int MaxScore,
    decimal ScorePercent
);

/// <summary>
/// Risk classification derived from confirmations and trend alignment.
/// </summary>
public sealed record IndicatorRisk(
    string Category,
    string Action,
    decimal RiskMultiplier,
    bool TrendRequired,
    bool CounterTrendAllowed,
    int MinConfirmations
);
