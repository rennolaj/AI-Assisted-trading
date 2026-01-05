using System;

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
    string Side,
    EntrySpec Entry,
    StopSpec StopLoss,
    TakeProfitSpec? TakeProfit,
    MarginSpec Margin,
    DateTimeOffset CreatedAtUtc
);

/// <summary>
/// Entry order details for a trade plan.
/// </summary>
public sealed record EntrySpec(string Type, decimal? Price, int MaxSlippageBps);

/// <summary>
/// Stop-loss order details for a trade plan.
/// </summary>
public sealed record StopSpec(string Type, decimal Price, string Reason);

/// <summary>
/// Optional take-profit order details for a trade plan.
/// </summary>
public sealed record TakeProfitSpec(string Type, decimal? Price);

/// <summary>
/// Margin and leverage details for a trade plan.
/// </summary>
public sealed record MarginSpec(decimal Leverage, decimal? Notional);
