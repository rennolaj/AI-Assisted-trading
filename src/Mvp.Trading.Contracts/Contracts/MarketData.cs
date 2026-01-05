using System;

namespace Mvp.Trading.Contracts;

/// <summary>
/// Describes a tradable instrument in the exchange catalog.
/// </summary>
public sealed record Instrument(string Symbol, string Base, string Quote, bool Tradable);

/// <summary>
/// Represents the latest ticker snapshot for an instrument.
/// </summary>
public sealed record Ticker(string Symbol, decimal Last, decimal Bid, decimal Ask, DateTimeOffset TsUtc);

/// <summary>
/// Represents an OHLCV candle for a specific timeframe.
/// </summary>
public sealed record Candle(
    DateTimeOffset OpenTimeUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume
);

/// <summary>
/// Supported timeframes for indicator and market data queries.
/// </summary>
public enum Timeframe
{
    M1,
    M5,
    M15,
    H1,
    H4,
    D1
}
