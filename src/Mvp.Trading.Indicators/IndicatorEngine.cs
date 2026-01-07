using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Indicators;

/// <summary>
/// Computes deterministic indicator snapshots using configured rules.
/// </summary>
public sealed class IndicatorEngine
{
    private readonly IMarketDataProvider _marketData;
    private readonly IndicatorConfig _config;
    private readonly ILogger<IndicatorEngine> _logger;

    public IndicatorEngine(
        IMarketDataProvider marketData,
        IndicatorConfig config,
        ILogger<IndicatorEngine>? logger = null)
    {
        _marketData = marketData;
        _config = config;
        _logger = logger ?? NullLogger<IndicatorEngine>.Instance;
    }

    public async Task<Result<IndicatorSnapshot>> ComputeAsync(IndicatorInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Symbol))
        {
            return new Result<IndicatorSnapshot>(
                false,
                null,
                new Error("VALIDATION", "Symbol is required to compute indicators.", null));
        }

        var direction = NormalizeDirection(input.DirectionHint);
        var evaluationTime = input.EvaluationTimeUtc;
        var timeframeTasks = _config.Timeframes
            .Select(tf => FetchCandlesAsync(input.Symbol, tf, evaluationTime, ct))
            .ToArray();

        var timeframeResults = await Task.WhenAll(timeframeTasks);
        if (timeframeResults.Any(r => !r.Result.Ok || r.Analysis is null))
        {
            var error = timeframeResults.FirstOrDefault(r => !r.Result.Ok)?.Result.Error
                ?? new Error("UPSTREAM_ERROR", "Failed to fetch candles.", null);
            return new Result<IndicatorSnapshot>(
                false,
                null,
                error);
        }

        var analyses = timeframeResults
            .Where(r => r.Analysis is not null)
            .Select(r => r.Analysis!)
            .ToList();

        var dataIntegrity = analyses.All(a => a.HasEnoughData);
        var anchor = analyses.FirstOrDefault(a => a.Timeframe == _config.AnchorTimeframe);
        var trend = analyses.FirstOrDefault(a => a.Timeframe == _config.TrendTimeframe);

        if (anchor is null || trend is null)
        {
            return new Result<IndicatorSnapshot>(
                false,
                null,
                new Error("CONFIG", "Anchor or trend timeframe data is missing.", null));
        }

        var gates = new IndicatorGates(
            DataIntegrity: dataIntegrity,
            RsiExtremeAnchor: IsRsiExtreme(anchor.Rsi, direction),
            DirectionDeclared: direction is "LONG" or "SHORT");

        var confirmations = EvaluateConfirmations(direction, anchor, trend, analyses);
        var score = Score(confirmations);
        var risk = EvaluateRisk(gates, confirmations, score.ScorePercent);

        var snapshot = new IndicatorSnapshot(
            input.AlertId,
            input.CorrelationId,
            input.Symbol,
            _config.Mode,
            direction,
            _config.AnchorTimeframe,
            _config.TrendTimeframe,
            evaluationTime,
            evaluationTime,
            _config.Parameters,
            analyses.Select(a => a.Snapshot).ToList(),
            gates,
            confirmations,
            score,
            risk);

        return new Result<IndicatorSnapshot>(true, snapshot, null);
    }

    private async Task<TimeframeAnalysisResult> FetchCandlesAsync(string symbol, Timeframe timeframe, DateTimeOffset evaluationTime, CancellationToken ct)
    {
        var targetBars = GetTargetBars(timeframe);
        var requiredBars = GetRequiredBars(targetBars);
        var result = await _marketData.GetOhlcvAsync(symbol, timeframe, requiredBars, ct);
        if (!result.Ok || result.Value is null)
        {
            return new TimeframeAnalysisResult(result, null);
        }

        var candles = result.Value
            .OrderBy(c => c.OpenTimeUtc)
            .ToList();

        var closed = FilterClosed(candles, timeframe, evaluationTime);
        if (closed.Count < requiredBars)
        {
            _logger.LogWarning(
                "Indicator candles insufficient for {Symbol} {Timeframe}: targetBars={TargetBars} requiredBars={RequiredBars} returnedBars={ReturnedBars} closedBars={ClosedBars} evaluationTime={EvaluationTime}",
                symbol,
                timeframe,
                targetBars,
                requiredBars,
                candles.Count,
                closed.Count,
                evaluationTime);
        }
        var analysis = AnalyzeTimeframe(timeframe, closed, requiredBars);
        return new TimeframeAnalysisResult(result, analysis);
    }

    private TimeframeAnalysis AnalyzeTimeframe(Timeframe timeframe, IReadOnlyList<Candle> candles, int requiredBars)
    {
        if (candles.Count < requiredBars)
        {
            return TimeframeAnalysis.CreateInsufficient(timeframe);
        }

        var closes = candles.Select(c => c.Close).ToList();
        var volumes = candles.Select(c => c.Volume).ToList();

        var rsiSeries = IndicatorMath.ComputeRsi(closes, _config.Parameters.RsiPeriod);
        var rsiValue = GetLast(rsiSeries, out var rsiIndex);

        var stochSeries = IndicatorMath.ComputeStochRsi(
            rsiSeries,
            _config.Parameters.StochRsiPeriod,
            _config.StochRsiKPeriod,
            _config.StochRsiDPeriod);
        var stochK = GetLast(stochSeries.K, out var stochIndex);
        var stochD = GetLast(stochSeries.D, out _);
        var prevStochK = GetPrevious(stochSeries.K, stochIndex);
        var prevStochD = GetPrevious(stochSeries.D, stochIndex);

        var macdSeries = IndicatorMath.ComputeMacd(
            closes,
            _config.Parameters.MacdFast,
            _config.Parameters.MacdSlow,
            _config.Parameters.MacdSignal);
        var macdValue = GetLast(macdSeries.Macd, out var macdIndex);
        var macdSignal = GetLast(macdSeries.Signal, out _);
        var macdHist = GetLast(macdSeries.Hist, out _);
        var prevHist = GetPrevious(macdSeries.Hist, macdIndex);

        var volumeRatio = ComputeVolumeRatio(volumes);

        var snapshot = BuildSnapshot(timeframe, rsiValue, stochK, stochD, macdValue, macdSignal, macdHist, volumeRatio);

        return new TimeframeAnalysis(
            timeframe,
            true,
            rsiValue,
            stochK,
            stochD,
            prevStochK,
            prevStochD,
            macdValue,
            macdSignal,
            macdHist,
            prevHist,
            volumeRatio,
            snapshot);
    }

    private TimeframeSnapshot BuildSnapshot(
        Timeframe timeframe,
        decimal? rsiValue,
        decimal? stochK,
        decimal? stochD,
        decimal? macd,
        decimal? macdSignal,
        decimal? macdHist,
        decimal? volumeRatio)
    {
        if (rsiValue is null || stochK is null || stochD is null || macd is null || macdSignal is null || macdHist is null || volumeRatio is null)
        {
            return new TimeframeSnapshot(
                timeframe,
                new RsiState(0m, "INSUFFICIENT_DATA"),
                new StochRsiState(0m, 0m, "INSUFFICIENT_DATA"),
                new MacdState(0m, 0m, 0m, "INSUFFICIENT_DATA"),
                new VolumeState(0m, "INSUFFICIENT_DATA", _config.Parameters.VolumeRule.Mode));
        }

        var roundedRsi = Round(rsiValue.Value);
        var roundedK = Round(stochK.Value);
        var roundedD = Round(stochD.Value);
        var roundedMacd = Round(macd.Value);
        var roundedSignal = Round(macdSignal.Value);
        var roundedHist = Round(macdHist.Value);
        var roundedVolume = Round(volumeRatio.Value);

        return new TimeframeSnapshot(
            timeframe,
            new RsiState(roundedRsi, GetRsiState(roundedRsi)),
            new StochRsiState(roundedK, roundedD, GetStochState(roundedK)),
            new MacdState(roundedMacd, roundedSignal, roundedHist, GetMacdState(roundedMacd, roundedSignal, roundedHist)),
            new VolumeState(roundedVolume, GetVolumeState(roundedVolume), $"{_config.Parameters.VolumeRule.Mode}:{_config.Parameters.VolumeRule.Period}:{_config.Parameters.VolumeRule.Threshold}"));
    }

    private IndicatorConfirmations EvaluateConfirmations(
        string direction,
        TimeframeAnalysis anchor,
        TimeframeAnalysis trend,
        IReadOnlyList<TimeframeAnalysis> analyses)
    {
        var directionDeclared = direction is "LONG" or "SHORT";
        var rsiMultiTf = directionDeclared &&
                         analyses.Any(a => a.Timeframe != _config.AnchorTimeframe && IsRsiExtreme(a.Rsi, direction));

        var stochRsiReversal = directionDeclared && analyses.Any(a =>
            IsLowerTimeframe(a.Timeframe, _config.AnchorTimeframe) &&
            IsStochRsiReversal(direction, a.StochK, a.StochD, a.PrevStochK, a.PrevStochD));

        var macdMomentum = directionDeclared && IsMacdMomentum(direction, anchor.Macd, anchor.MacdSignal, anchor.MacdHist, anchor.PrevMacdHist);

        var trendAlignment = directionDeclared && IsTrendAligned(direction, trend.Macd);

        var volumeConfirm = directionDeclared && anchor.VolumeRatio is not null &&
                            anchor.VolumeRatio.Value >= _config.Parameters.VolumeRule.Threshold;

        var counterTrend = directionDeclared && !trendAlignment;
        var counterTrendVolumeOk = !counterTrend || volumeConfirm;

        return new IndicatorConfirmations(
            rsiMultiTf,
            stochRsiReversal,
            macdMomentum,
            trendAlignment,
            volumeConfirm,
            counterTrendVolumeOk);
    }

    private IndicatorScore Score(IndicatorConfirmations confirmations)
    {
        var score = 0;
        var confirmationsCount = 0;

        if (confirmations.RsiMultiTf)
        {
            score += _config.Weights.RsiMultiTf;
            confirmationsCount++;
        }

        if (confirmations.StochRsiReversal)
        {
            score += _config.Weights.StochRsiReversal;
            confirmationsCount++;
        }

        if (confirmations.MacdMomentum)
        {
            score += _config.Weights.MacdMomentum;
            confirmationsCount++;
        }

        if (confirmations.TrendAlignment)
        {
            score += _config.Weights.TrendAlignment;
            confirmationsCount++;
        }

        if (confirmations.VolumeConfirm)
        {
            score += _config.Weights.VolumeConfirm;
            confirmationsCount++;
        }

        var maxScore = _config.Weights.RsiMultiTf +
                       _config.Weights.StochRsiReversal +
                       _config.Weights.MacdMomentum +
                       _config.Weights.TrendAlignment +
                       _config.Weights.VolumeConfirm;

        var scorePercent = maxScore == 0
            ? 0m
            : Round((score / (decimal)maxScore) * 100m);

        return new IndicatorScore(confirmationsCount, score, maxScore, scorePercent);
    }

    private IndicatorRisk EvaluateRisk(IndicatorGates gates, IndicatorConfirmations confirmations, decimal scorePercent)
    {
        if (!gates.DataIntegrity || !gates.DirectionDeclared || !gates.RsiExtremeAnchor)
        {
            return new IndicatorRisk(
                "INVALID",
                "REJECT_DEFAULT",
                0m,
                TrendRequired: false,
                CounterTrendAllowed: false,
                MinConfirmations: 0);
        }

        var confirmationCount = CountConfirmations(confirmations);
        foreach (var profile in _config.RiskProfiles.OrderByDescending(p => p.MinScore))
        {
            if (scorePercent < profile.MinScore || scorePercent > profile.MaxScore)
            {
                continue;
            }

            if (confirmationCount < profile.MinConfirmations)
            {
                continue;
            }

            if (profile.TrendRequired && !confirmations.TrendAlignment)
            {
                continue;
            }

            if (!profile.CounterTrendAllowed && !confirmations.TrendAlignment)
            {
                continue;
            }

            return new IndicatorRisk(
                profile.Category,
                profile.Action,
                profile.RiskMultiplier,
                profile.TrendRequired,
                profile.CounterTrendAllowed,
                profile.MinConfirmations);
        }

        return new IndicatorRisk(
            "HIGH",
            "REJECT_DEFAULT",
            0.25m,
            TrendRequired: false,
            CounterTrendAllowed: false,
            MinConfirmations: 1);
    }

    private int CountConfirmations(IndicatorConfirmations confirmations)
    {
        var count = 0;
        if (confirmations.RsiMultiTf)
        {
            count++;
        }

        if (confirmations.StochRsiReversal)
        {
            count++;
        }

        if (confirmations.MacdMomentum)
        {
            count++;
        }

        if (confirmations.TrendAlignment)
        {
            count++;
        }

        if (confirmations.VolumeConfirm)
        {
            count++;
        }

        return count;
    }

    private static string NormalizeDirection(string? direction)
    {
        if (string.IsNullOrWhiteSpace(direction))
        {
            return "UNKNOWN";
        }

        var normalized = direction.Trim().ToUpperInvariant();
        return normalized switch
        {
            "LONG" => "LONG",
            "SHORT" => "SHORT",
            "BUY" => "LONG",
            "SELL" => "SHORT",
            _ => "UNKNOWN"
        };
    }

    private bool IsRsiExtreme(decimal? rsi, string direction)
    {
        if (rsi is null)
        {
            return false;
        }

        return direction switch
        {
            "LONG" => rsi.Value <= _config.Thresholds.RsiOversold,
            "SHORT" => rsi.Value >= _config.Thresholds.RsiOverbought,
            _ => false
        };
    }

    private bool IsStochRsiReversal(string direction, decimal? k, decimal? d, decimal? prevK, decimal? prevD)
    {
        if (k is null || d is null || prevK is null || prevD is null)
        {
            return false;
        }

        if (direction == "LONG")
        {
            var wasOversold = prevK.Value <= _config.Thresholds.StochRsiOversold &&
                              prevD.Value <= _config.Thresholds.StochRsiOversold;
            return wasOversold && prevK.Value <= prevD.Value && k.Value > d.Value;
        }

        if (direction == "SHORT")
        {
            var wasOverbought = prevK.Value >= _config.Thresholds.StochRsiOverbought &&
                                prevD.Value >= _config.Thresholds.StochRsiOverbought;
            return wasOverbought && prevK.Value >= prevD.Value && k.Value < d.Value;
        }

        return false;
    }

    private static bool IsMacdMomentum(string direction, decimal? macd, decimal? signal, decimal? hist, decimal? prevHist)
    {
        if (macd is null || signal is null || hist is null || prevHist is null)
        {
            return false;
        }

        return direction switch
        {
            "LONG" => macd.Value > signal.Value && hist.Value > prevHist.Value,
            "SHORT" => macd.Value < signal.Value && hist.Value < prevHist.Value,
            _ => false
        };
    }

    private static bool IsTrendAligned(string direction, decimal? macd)
    {
        if (macd is null)
        {
            return false;
        }

        return direction switch
        {
            "LONG" => macd.Value >= 0m,
            "SHORT" => macd.Value <= 0m,
            _ => false
        };
    }

    private bool IsLowerTimeframe(Timeframe candidate, Timeframe anchor)
    {
        return TimeframeToMinutes(candidate) < TimeframeToMinutes(anchor);
    }

    private static IReadOnlyList<Candle> FilterClosed(IReadOnlyList<Candle> candles, Timeframe timeframe, DateTimeOffset evaluationTime)
    {
        var span = TimeframeToSpan(timeframe);
        return candles
            .Where(c => c.OpenTimeUtc.Add(span) <= evaluationTime)
            .ToList();
    }

    private static TimeSpan TimeframeToSpan(Timeframe timeframe)
    {
        return TimeSpan.FromMinutes(TimeframeToMinutes(timeframe));
    }

    private static int TimeframeToMinutes(Timeframe timeframe) => timeframe switch
    {
        Timeframe.M1 => 1,
        Timeframe.M5 => 5,
        Timeframe.M15 => 15,
        Timeframe.M30 => 30,
        Timeframe.H1 => 60,
        Timeframe.H2 => 120,
        Timeframe.H4 => 240,
        Timeframe.H12 => 720,
        Timeframe.D1 => 1440,
        _ => 1
    };

    private int GetTargetBars(Timeframe timeframe)
    {
        if (_config.LookbackDaysByTimeframe.TryGetValue(timeframe, out var daysOverride) && daysOverride > 0)
        {
            var bars = BarsPerDay(timeframe) * daysOverride;
            return Math.Max(1, bars);
        }

        if (_config.LookbackBarsByTimeframe.TryGetValue(timeframe, out var barsOverride) && barsOverride > 0)
        {
            return Math.Max(1, barsOverride);
        }

        if (_config.LookbackDays > 0)
        {
            var bars = BarsPerDay(timeframe) * _config.LookbackDays;
            return Math.Max(1, bars);
        }

        return Math.Max(1, _config.LookbackBars);
    }

    private static int BarsPerDay(Timeframe timeframe)
    {
        var minutes = TimeframeToMinutes(timeframe);
        if (minutes <= 0)
        {
            return 1;
        }

        return Math.Max(1, 1440 / minutes);
    }

    private int GetRequiredBars(int targetBars)
    {
        var rsiRequired = _config.Parameters.RsiPeriod + 1;
        var stochRequired = _config.Parameters.RsiPeriod + _config.Parameters.StochRsiPeriod + _config.StochRsiKPeriod + _config.StochRsiDPeriod;
        var macdRequired = _config.Parameters.MacdSlow + _config.Parameters.MacdSignal + 1;
        var volumeRequired = _config.Parameters.VolumeRule.Period + 1;

        var minimum = Math.Max(Math.Max(rsiRequired, stochRequired), Math.Max(macdRequired, volumeRequired));
        return Math.Max(targetBars, minimum);
    }

    private decimal? ComputeVolumeRatio(IReadOnlyList<decimal> volumes)
    {
        var sma = IndicatorMath.ComputeSma(volumes, _config.Parameters.VolumeRule.Period);
        if (sma is null || sma.Value == 0m)
        {
            return null;
        }

        var last = volumes[^1];
        return last / sma.Value;
    }

    private decimal? GetLast(IReadOnlyList<decimal?> series, out int index)
    {
        for (var i = series.Count - 1; i >= 0; i--)
        {
            if (series[i] is not null)
            {
                index = i;
                return series[i];
            }
        }

        index = -1;
        return null;
    }

    private static decimal? GetPrevious(IReadOnlyList<decimal?> series, int index)
    {
        if (index <= 0)
        {
            return null;
        }

        for (var i = index - 1; i >= 0; i--)
        {
            if (series[i] is not null)
            {
                return series[i];
            }
        }

        return null;
    }

    private string GetRsiState(decimal value)
    {
        if (value <= _config.Thresholds.RsiOversold)
        {
            return "OVERSOLD";
        }

        if (value >= _config.Thresholds.RsiOverbought)
        {
            return "OVERBOUGHT";
        }

        return "NEUTRAL";
    }

    private string GetStochState(decimal value)
    {
        if (value <= _config.Thresholds.StochRsiOversold)
        {
            return "OVERSOLD";
        }

        if (value >= _config.Thresholds.StochRsiOverbought)
        {
            return "OVERBOUGHT";
        }

        return "NEUTRAL";
    }

    private static string GetMacdState(decimal macd, decimal signal, decimal hist)
    {
        if (macd >= signal && hist >= 0m)
        {
            return "BULLISH";
        }

        if (macd <= signal && hist <= 0m)
        {
            return "BEARISH";
        }

        return "NEUTRAL";
    }

    private string GetVolumeState(decimal ratio)
    {
        return ratio >= _config.Parameters.VolumeRule.Threshold ? "CONFIRMED" : "WEAK";
    }

    private decimal Round(decimal value)
    {
        return Math.Round(value, _config.SnapshotPrecision, MidpointRounding.AwayFromZero);
    }

    private sealed record TimeframeAnalysisResult(Result<IReadOnlyList<Candle>> Result, TimeframeAnalysis? Analysis);

    private sealed record TimeframeAnalysis(
        Timeframe Timeframe,
        bool HasEnoughData,
        decimal? Rsi,
        decimal? StochK,
        decimal? StochD,
        decimal? PrevStochK,
        decimal? PrevStochD,
        decimal? Macd,
        decimal? MacdSignal,
        decimal? MacdHist,
        decimal? PrevMacdHist,
        decimal? VolumeRatio,
        TimeframeSnapshot Snapshot)
    {
        public static TimeframeAnalysis CreateInsufficient(Timeframe timeframe)
        {
            return new TimeframeAnalysis(
                timeframe,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                new TimeframeSnapshot(
                    timeframe,
                    new RsiState(0m, "INSUFFICIENT_DATA"),
                    new StochRsiState(0m, 0m, "INSUFFICIENT_DATA"),
                    new MacdState(0m, 0m, 0m, "INSUFFICIENT_DATA"),
                    new VolumeState(0m, "INSUFFICIENT_DATA", "INSUFFICIENT_DATA")));
        }
    }
}
