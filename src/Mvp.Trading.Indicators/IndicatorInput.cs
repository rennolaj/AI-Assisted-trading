using System;

namespace Mvp.Trading.Indicators;

/// <summary>
/// Input used to compute an indicator snapshot.
/// </summary>
public sealed record IndicatorInput(
    Guid AlertId,
    Guid CorrelationId,
    string Symbol,
    string DirectionHint,
    DateTimeOffset EvaluationTimeUtc
);
