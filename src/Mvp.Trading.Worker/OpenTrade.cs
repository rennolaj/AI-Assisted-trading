using System;

namespace Mvp.Trading.Worker;

/// <summary>
/// Represents an open trade that must be continuously monitored.
/// </summary>
public sealed record OpenTrade(
    Guid TradeId,
    string ExchangeId,
    string Symbol,
    string Side,
    decimal? InvalidationPrice
);
