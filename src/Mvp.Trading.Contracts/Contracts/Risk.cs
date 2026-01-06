using System;
using System.Collections.Generic;

namespace Mvp.Trading.Contracts;

/// <summary>
/// Hard risk limits and allowed trade sides for the MVP.
/// </summary>
public sealed record RiskPolicy(
    decimal MaxAccountRiskPctPerTrade,
    decimal MaxDailyLossPct,
    decimal MaxLeverage,
    decimal MaxNotional,
    string AllowedSides
);

/// <summary>
/// Deterministic trade plan produced by the risk engine.
/// </summary>
public sealed record TradePlan(
    Guid PlanId,
    string Symbol,
    string Side,
    Timeframe Timeframe,
    string EntryType,
    decimal EntryReferencePrice,
    decimal EntryLimitPrice,
    decimal Quantity,
    decimal StopLossPrice,
    decimal PlannedRiskAmount,
    IReadOnlyList<TakeProfitTarget> TakeProfitTargets,
    string RiskPolicyVersion,
    string PolicyHash,
    string CandidateId,
    string DecisionReceipt,
    DateTimeOffset CreatedAtUtc
);

/// <summary>
/// Partial take-profit target for a trade plan.
/// </summary>
public sealed record TakeProfitTarget(decimal Price, decimal Quantity, string Reason);
