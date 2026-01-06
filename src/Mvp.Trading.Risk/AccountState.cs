using System;

namespace Mvp.Trading.Risk;

/// <summary>
/// Current account state used for deterministic risk sizing.
/// </summary>
public sealed record AccountState(
    string AccountId,
    string Currency,
    decimal Equity,
    decimal DailyRiskUsed,
    DateTimeOffset? UpdatedAtUtc
);
