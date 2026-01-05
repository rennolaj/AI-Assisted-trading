namespace Mvp.Trading.Integrations.Kraken;

/// <summary>
/// Cache settings for Kraken Futures public market data.
/// </summary>
public sealed class KrakenFuturesCacheOptions
{
    /// <summary>
    /// Cache TTL for instruments in seconds.
    /// </summary>
    public int InstrumentsTtlSeconds { get; init; } = 300;

    /// <summary>
    /// Cache TTL for tickers in seconds.
    /// </summary>
    public int TickersTtlSeconds { get; init; } = 2;

    /// <summary>
    /// Cache TTL for candles in seconds.
    /// </summary>
    public int CandlesTtlSeconds { get; init; } = 5;
}
