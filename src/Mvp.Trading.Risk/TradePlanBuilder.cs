using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Risk;

/// <summary>
/// Deterministic trade plan builder enforcing risk policy and instrument constraints.
/// </summary>
public sealed class TradePlanBuilder : ITradePlanBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IAccountStateProvider _accountProvider;
    private readonly IInstrumentSpecProvider _instrumentProvider;
    private readonly IExecutionSettingsProvider _executionSettingsProvider;

    public TradePlanBuilder(
        IAccountStateProvider accountProvider,
        IInstrumentSpecProvider instrumentProvider,
        IExecutionSettingsProvider executionSettingsProvider)
    {
        _accountProvider = accountProvider;
        _instrumentProvider = instrumentProvider;
        _executionSettingsProvider = executionSettingsProvider;
    }

    public Result<TradePlan> BuildPlan(TradePlanContext context)
    {
        if (!IsAllowDecision(context.Decision.Decision))
        {
            return Fail("RISK_DECISION_REJECTED", $"Decision was {context.Decision.Decision}.");
        }

        var side = ResolveSide(context.Decision.Decision);
        if (side is null)
        {
            return Fail("RISK_DECISION_SIDE_UNKNOWN", "Unable to derive side from decision.");
        }

        if (!IsSideAllowed(context.Policy.AllowedSides, side))
        {
            return Fail("RISK_SIDE_NOT_ALLOWED", $"Side {side} is not allowed.");
        }

        if (string.IsNullOrWhiteSpace(context.Decision.ChosenCandidateId))
        {
            return Fail("RISK_CANDIDATE_MISSING", "Decision did not include a candidate id.");
        }

        var candidate = ResolveCandidate(context.Candidates, context.Decision.ChosenCandidateId);
        if (candidate is null)
        {
            return Fail("RISK_CANDIDATE_NOT_FOUND", "Chosen candidate was not found.");
        }

        if (!string.Equals(context.Decision.StopLossAnchor, "WAVEINVALIDATION", StringComparison.OrdinalIgnoreCase))
        {
            return Fail("RISK_STOP_ANCHOR_UNSUPPORTED", $"Stop-loss anchor {context.Decision.StopLossAnchor} is not supported.");
        }

        var stopLoss = side == "LONG"
            ? candidate.Invalidation.LongInvalidationPrice
            : candidate.Invalidation.ShortInvalidationPrice;
        if (!stopLoss.HasValue || stopLoss.Value <= 0m)
        {
            return Fail("RISK_STOP_LOSS_MISSING", "Candidate invalidation price was missing.");
        }

        var entryReference = context.Alert.Tv.Close;
        if (!entryReference.HasValue || entryReference.Value <= 0m)
        {
            return Fail("RISK_ENTRY_PRICE_MISSING", "Alert close price was missing.");
        }

        var instrument = _instrumentProvider.GetSpec(context.Snapshot.Symbol);
        if (instrument is null)
        {
            return Fail("RISK_INSTRUMENT_MISSING", $"Instrument spec for {context.Snapshot.Symbol} was not found.");
        }

        if (instrument.PriceTick <= 0m || instrument.QtyStep <= 0m)
        {
            return Fail("RISK_INSTRUMENT_INVALID", "Instrument tick or step is invalid.");
        }

        var execution = _executionSettingsProvider.GetSettings();
        var entryLimit = ComputeEntryLimit(entryReference.Value, execution.SlippageCapPct, side);
        entryLimit = RoundToTick(entryLimit, instrument.PriceTick, roundUp: side == "LONG");

        var stopLossPrice = RoundToTick(stopLoss.Value, instrument.PriceTick, roundUp: side == "SHORT");
        var stopDistance = Math.Abs(entryLimit - stopLossPrice);
        if (stopDistance <= 0m)
        {
            return Fail("RISK_STOP_DISTANCE_ZERO", "Stop distance is zero after rounding.");
        }

        var account = _accountProvider.GetAccountState();
        var riskAmount = account.Equity * (context.Policy.MaxAccountRiskPctPerTrade / 100m);
        if (riskAmount <= 0m)
        {
            return Fail("RISK_AMOUNT_INVALID", "Computed risk amount was not positive.");
        }

        var multiplier = instrument.ContractMultiplier <= 0m ? 1m : instrument.ContractMultiplier;
        var pointRisk = stopDistance * multiplier;
        if (pointRisk <= 0m)
        {
            return Fail("RISK_POINT_VALUE_INVALID", "Point risk is zero after applying multiplier.");
        }

        var qty = RoundDownToStep(riskAmount / pointRisk, instrument.QtyStep);
        if (qty <= 0m)
        {
            return Fail("RISK_QUANTITY_TOO_LOW", "Quantity rounded to zero.");
        }

        qty = ApplyMaxNotionalConstraints(qty, entryLimit, multiplier, account.Equity, instrument, context.Policy);
        if (qty <= 0m)
        {
            return Fail("RISK_QUANTITY_TOO_LOW", "Quantity reduced to zero after notional constraints.");
        }

        var notional = qty * entryLimit * multiplier;
        if (qty < instrument.MinQty || notional < instrument.MinNotional)
        {
            return Fail("RISK_MIN_CONSTRAINTS", "Quantity or notional below minimum constraints.");
        }

        var plannedRisk = qty * pointRisk;
        var dailyRiskCap = account.Equity * (context.Policy.MaxDailyLossPct / 100m);
        if (account.DailyRiskUsed + plannedRisk > dailyRiskCap)
        {
            return Fail("RISK_DAILY_BUDGET", "Daily risk budget exceeded.");
        }

        var targets = BuildTakeProfitTargets(qty, stopDistance, entryLimit, side, instrument);
        if (targets is null)
        {
            return Fail("RISK_TAKE_PROFIT_INVALID", "Unable to allocate take-profit targets.");
        }

        var policyHash = HashString(JsonSerializer.Serialize(context.Policy, JsonOptions));
        var decisionReceipt = BuildDecisionReceipt(context.Decision, context.DecisionSchemaVersion);
        var planId = ComputePlanId(context, side, entryLimit, stopLossPrice, qty, policyHash);

        var plan = new TradePlan(
            planId,
            context.Snapshot.Symbol,
            side,
            context.Candidates.BaseTimeframe,
            "LIMIT_IOC",
            entryReference.Value,
            entryLimit,
            qty,
            stopLossPrice,
            plannedRisk,
            targets,
            context.RiskPolicyVersion,
            policyHash,
            candidate.CandidateId,
            decisionReceipt,
            context.EvaluationTimeUtc);

        return new Result<TradePlan>(true, plan, null);
    }

    private static ElliottCandidate? ResolveCandidate(ElliottCandidates candidates, string candidateId)
    {
        foreach (var candidate in candidates.Candidates)
        {
            if (string.Equals(candidate.CandidateId, candidateId, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsAllowDecision(string decision)
    {
        return decision.StartsWith("ALLOW", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveSide(string decision)
    {
        if (decision.Contains("LONG", StringComparison.OrdinalIgnoreCase))
        {
            return "LONG";
        }

        if (decision.Contains("SHORT", StringComparison.OrdinalIgnoreCase))
        {
            return "SHORT";
        }

        return null;
    }

    private static bool IsSideAllowed(string allowedSides, string side)
    {
        if (string.IsNullOrWhiteSpace(allowedSides))
        {
            return false;
        }

        foreach (var token in allowedSides.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(token, side, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static decimal ComputeEntryLimit(decimal referencePrice, decimal slippageCapPct, string side)
    {
        var cap = Math.Max(slippageCapPct, 0m);
        return side == "LONG"
            ? referencePrice * (1m + cap)
            : referencePrice * (1m - cap);
    }

    private static decimal ApplyMaxNotionalConstraints(
        decimal qty,
        decimal entryLimit,
        decimal multiplier,
        decimal equity,
        InstrumentSpec instrument,
        RiskPolicy policy)
    {
        var maxLeverage = policy.MaxLeverage > 0m && instrument.MaxLeverage > 0m
            ? Math.Min(policy.MaxLeverage, instrument.MaxLeverage)
            : Math.Max(policy.MaxLeverage, instrument.MaxLeverage);

        var maxNotionalByLeverage = maxLeverage > 0m ? equity * maxLeverage : decimal.MaxValue;
        var maxNotionalByPolicy = policy.MaxNotional > 0m ? policy.MaxNotional : decimal.MaxValue;
        var maxNotional = Math.Min(maxNotionalByLeverage, maxNotionalByPolicy);

        var notional = qty * entryLimit * multiplier;
        if (notional <= maxNotional)
        {
            return qty;
        }

        var cappedQty = maxNotional / (entryLimit * multiplier);
        return RoundDownToStep(cappedQty, instrument.QtyStep);
    }

    private static decimal RoundToTick(decimal value, decimal tick, bool roundUp)
    {
        if (tick <= 0m)
        {
            return value;
        }

        var factor = Math.Floor(value / tick);
        var rounded = factor * tick;
        if (roundUp && rounded < value)
        {
            rounded += tick;
        }

        return rounded;
    }

    private static decimal RoundDownToStep(decimal value, decimal step)
    {
        if (step <= 0m)
        {
            return value;
        }

        var factor = Math.Floor(value / step);
        return factor * step;
    }

    private static IReadOnlyList<TakeProfitTarget>? BuildTakeProfitTargets(
        decimal totalQty,
        decimal stopDistance,
        decimal entryLimit,
        string side,
        InstrumentSpec instrument)
    {
        var step = instrument.QtyStep;
        if (step <= 0m)
        {
            return null;
        }

        var totalStepsDecimal = decimal.Floor(totalQty / step);
        if (totalStepsDecimal <= 0m)
        {
            return null;
        }

        var totalSteps = (int)totalStepsDecimal;
        var steps1 = (int)decimal.Floor(totalSteps * 0.5m);
        var steps2 = (int)decimal.Floor(totalSteps * 0.3m);
        var steps3 = totalSteps - steps1 - steps2;

        if (steps1 <= 0 || steps2 <= 0 || steps3 <= 0)
        {
            return null;
        }

        var qty1 = steps1 * step;
        var qty2 = steps2 * step;
        var qty3 = steps3 * step;

        if (qty1 < instrument.MinQty || qty2 < instrument.MinQty || qty3 < instrument.MinQty)
        {
            return null;
        }

        var roundUp = side == "SHORT";
        var price1 = ComputeTakeProfitPrice(entryLimit, stopDistance, side, 1m, instrument.PriceTick, roundUp);
        var price2 = ComputeTakeProfitPrice(entryLimit, stopDistance, side, 2m, instrument.PriceTick, roundUp);
        var price3 = ComputeTakeProfitPrice(entryLimit, stopDistance, side, 3m, instrument.PriceTick, roundUp);

        if (price1 <= 0m || price2 <= 0m || price3 <= 0m)
        {
            return null;
        }

        return new[]
        {
            new TakeProfitTarget(price1, qty1, "TP1"),
            new TakeProfitTarget(price2, qty2, "TP2"),
            new TakeProfitTarget(price3, qty3, "TP3")
        };
    }

    private static decimal ComputeTakeProfitPrice(
        decimal entryLimit,
        decimal stopDistance,
        string side,
        decimal multiple,
        decimal tick,
        bool roundUp)
    {
        var raw = side == "LONG"
            ? entryLimit + (stopDistance * multiple)
            : entryLimit - (stopDistance * multiple);

        return RoundToTick(raw, tick, roundUp);
    }

    private static Guid ComputePlanId(
        TradePlanContext context,
        string side,
        decimal entryLimitPrice,
        decimal stopLossPrice,
        decimal qty,
        string policyHash)
    {
        var seed = string.Join(
            "|",
            context.Snapshot.Symbol,
            side,
            context.Candidates.BaseTimeframe.ToString(),
            context.Decision.ChosenCandidateId ?? string.Empty,
            FormatDecimal(entryLimitPrice),
            FormatDecimal(stopLossPrice),
            FormatDecimal(qty),
            policyHash,
            context.EvaluationTimeUtc.ToString("O", CultureInfo.InvariantCulture));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static string BuildDecisionReceipt(LlmDecision decision, string schemaVersion)
    {
        var decisionJson = JsonSerializer.Serialize(decision, JsonOptions);
        var decisionHash = HashString(decisionJson);
        var receipt = new DecisionReceipt(schemaVersion, decision, decisionHash);
        return JsonSerializer.Serialize(receipt, JsonOptions);
    }

    private static string HashString(string payload)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string FormatDecimal(decimal value)
    {
        return value.ToString("G29", CultureInfo.InvariantCulture);
    }

    private static Result<TradePlan> Fail(string code, string message)
    {
        return new Result<TradePlan>(false, null, new Error(code, message, null));
    }

    private sealed record DecisionReceipt(string SchemaVersion, LlmDecision Decision, string DecisionHash);
}
