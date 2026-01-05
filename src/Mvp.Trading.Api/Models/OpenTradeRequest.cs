namespace Mvp.Trading.Api.Models;

/// <summary>
/// Request payload for seeding an open trade.
/// </summary>
public sealed record OpenTradeRequest(
    string ExchangeId,
    string Symbol,
    string Side,
    decimal? EntryPrice,
    decimal? InvalidationPrice
);
