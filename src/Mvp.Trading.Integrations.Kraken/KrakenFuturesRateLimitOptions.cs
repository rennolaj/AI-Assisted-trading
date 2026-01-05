namespace Mvp.Trading.Integrations.Kraken;

/// <summary>
/// Rate limit budget configuration for Kraken Futures endpoints.
/// </summary>
public sealed class KrakenFuturesRateLimitOptions
{
    /// <summary>
    /// Max cost budget per window.
    /// </summary>
    public int MaxCostPerWindow { get; init; } = 500;

    /// <summary>
    /// Window length in seconds.
    /// </summary>
    public int WindowSeconds { get; init; } = 10;

    /// <summary>
    /// Cost for instruments endpoint.
    /// </summary>
    public int InstrumentsCost { get; init; } = 0;

    /// <summary>
    /// Cost for tickers endpoint.
    /// </summary>
    public int TickersCost { get; init; } = 0;

    /// <summary>
    /// Cost for candles endpoint.
    /// </summary>
    public int CandlesCost { get; init; } = 0;
}
