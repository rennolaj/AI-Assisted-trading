using System;
using System.Collections.Generic;

namespace Mvp.Trading.Contracts;

/// <summary>
/// Configures indicator periods and volume rules.
/// </summary>
public sealed record IndicatorParameters(
    int RsiPeriod,
    int StochRsiPeriod,
    int MacdFast,
    int MacdSlow,
    int MacdSignal,
    VolumeRule VolumeRule
);

/// <summary>
/// Defines the volume rule applied during indicator evaluation.
/// </summary>
public sealed record VolumeRule(string Mode, int Period, decimal Threshold);

/// <summary>
/// Aggregated indicator snapshot for a symbol across timeframes.
/// </summary>
public sealed record SignalSnapshot(
    string Symbol,
    DateTimeOffset ComputedAtUtc,
    IReadOnlyList<TimeframeSnapshot> Timeframes
);

/// <summary>
/// Indicator states for a single timeframe.
/// </summary>
public sealed record TimeframeSnapshot(
    Timeframe Tf,
    RsiState Rsi,
    StochRsiState StochRsi,
    MacdState Macd,
    VolumeState Volume
);

/// <summary>
/// RSI indicator value and state label.
/// </summary>
public sealed record RsiState(decimal Value, string State);

/// <summary>
/// Stochastic RSI indicator values and state label.
/// </summary>
public sealed record StochRsiState(decimal K, decimal D, string State);

/// <summary>
/// MACD indicator values and state label.
/// </summary>
public sealed record MacdState(decimal Macd, decimal Signal, decimal Hist, string State);

/// <summary>
/// Volume indicator value and rule evaluation state.
/// </summary>
public sealed record VolumeState(decimal Value, string State, string Rule);
