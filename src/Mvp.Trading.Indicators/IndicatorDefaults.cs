using System;
using System.Collections.Generic;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Indicators;

/// <summary>
/// Default indicator configurations.
/// </summary>
public static class IndicatorDefaults
{
    public const string ScalpingMode = "scalping_default";
    public const string SwingMode = "swing_default";

    public static IndicatorConfig ForMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return ScalpingDefault();
        }

        return mode.Trim().ToLowerInvariant() switch
        {
            "scalping" => ScalpingDefault(),
            "scalping_default" => ScalpingDefault(),
            "swing" => SwingDefault(),
            "swing_default" => SwingDefault(),
            _ => throw new ArgumentException($"Unsupported indicator mode '{mode}'.", nameof(mode))
        };
    }

    public static IndicatorConfig ScalpingDefault()
    {
        var parameters = new IndicatorParameters(
            RsiPeriod: 14,
            StochRsiPeriod: 14,
            MacdFast: 12,
            MacdSlow: 26,
            MacdSignal: 9,
            VolumeRule: new VolumeRule("SMA_RATIO", 20, 1.5m));

        var thresholds = new IndicatorThresholds(
            RsiOversold: 30m,
            RsiOverbought: 70m,
            StochRsiOversold: 20m,
            StochRsiOverbought: 80m);

        var weights = new IndicatorWeights(
            RsiMultiTf: 15,
            StochRsiReversal: 20,
            MacdMomentum: 20,
            TrendAlignment: 25,
            VolumeConfirm: 20);

        var riskProfiles = new List<IndicatorRiskProfile>
        {
            new(
                Category: "SAFE",
                MinScore: 85,
                MaxScore: 100,
                MinConfirmations: 4,
                TrendRequired: true,
                CounterTrendAllowed: true,
                RiskMultiplier: 1.25m,
                Action: "ALLOW"),
            new(
                Category: "NORMAL",
                MinScore: 70,
                MaxScore: 84,
                MinConfirmations: 3,
                TrendRequired: true,
                CounterTrendAllowed: true,
                RiskMultiplier: 1.0m,
                Action: "ALLOW"),
            new(
                Category: "MODERATE",
                MinScore: 55,
                MaxScore: 69,
                MinConfirmations: 2,
                TrendRequired: false,
                CounterTrendAllowed: true,
                RiskMultiplier: 0.5m,
                Action: "ALLOW_IF_POLICY"),
            new(
                Category: "HIGH",
                MinScore: 40,
                MaxScore: 54,
                MinConfirmations: 1,
                TrendRequired: false,
                CounterTrendAllowed: false,
                RiskMultiplier: 0.25m,
                Action: "REJECT_DEFAULT")
        };

        return new IndicatorConfig(
            Mode: ScalpingMode,
            Timeframes: new[] { Timeframe.M5, Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.H2 },
            AnchorTimeframe: Timeframe.M30,
            TrendTimeframe: Timeframe.H2,
            LookbackBars: 200,
            LookbackDays: 1,
            EvaluationWindowMinutes: 60,
            EvaluationIntervalMinutes: 5,
            SnapshotPrecision: 6,
            Parameters: parameters,
            Thresholds: thresholds,
            Weights: weights,
            RiskProfiles: riskProfiles,
            StochRsiKPeriod: 3,
            StochRsiDPeriod: 3);
    }

    public static IndicatorConfig SwingDefault()
    {
        var parameters = new IndicatorParameters(
            RsiPeriod: 21,
            StochRsiPeriod: 14,
            MacdFast: 12,
            MacdSlow: 26,
            MacdSignal: 9,
            VolumeRule: new VolumeRule("SMA_RATIO", 50, 1.2m));

        var thresholds = new IndicatorThresholds(
            RsiOversold: 30m,
            RsiOverbought: 70m,
            StochRsiOversold: 20m,
            StochRsiOverbought: 80m);

        var weights = new IndicatorWeights(
            RsiMultiTf: 10,
            StochRsiReversal: 15,
            MacdMomentum: 20,
            TrendAlignment: 35,
            VolumeConfirm: 20);

        var riskProfiles = new List<IndicatorRiskProfile>
        {
            new(
                Category: "SAFE",
                MinScore: 85,
                MaxScore: 100,
                MinConfirmations: 4,
                TrendRequired: true,
                CounterTrendAllowed: false,
                RiskMultiplier: 1.25m,
                Action: "ALLOW"),
            new(
                Category: "NORMAL",
                MinScore: 70,
                MaxScore: 84,
                MinConfirmations: 3,
                TrendRequired: true,
                CounterTrendAllowed: false,
                RiskMultiplier: 1.0m,
                Action: "ALLOW"),
            new(
                Category: "MODERATE",
                MinScore: 55,
                MaxScore: 69,
                MinConfirmations: 2,
                TrendRequired: true,
                CounterTrendAllowed: false,
                RiskMultiplier: 0.5m,
                Action: "ALLOW_IF_POLICY"),
            new(
                Category: "HIGH",
                MinScore: 40,
                MaxScore: 54,
                MinConfirmations: 1,
                TrendRequired: false,
                CounterTrendAllowed: false,
                RiskMultiplier: 0.25m,
                Action: "REJECT_DEFAULT")
        };

        return new IndicatorConfig(
            Mode: SwingMode,
            Timeframes: new[] { Timeframe.M30, Timeframe.H1, Timeframe.H4, Timeframe.H12, Timeframe.D1 },
            AnchorTimeframe: Timeframe.H4,
            TrendTimeframe: Timeframe.D1,
            LookbackBars: 200,
            LookbackDays: 1,
            EvaluationWindowMinutes: 720,
            EvaluationIntervalMinutes: 60,
            SnapshotPrecision: 6,
            Parameters: parameters,
            Thresholds: thresholds,
            Weights: weights,
            RiskProfiles: riskProfiles,
            StochRsiKPeriod: 3,
            StochRsiDPeriod: 3);
    }
}
