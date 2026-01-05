using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mvp.Trading.Contracts;

/// <summary>
/// Describes a supported exchange integration.
/// </summary>
public sealed record ExchangeInfo(string ExchangeId, string DisplayName, bool SupportsTrading);

/// <summary>
/// Common market data operations across exchange integrations.
/// </summary>
public interface IMarketDataProvider
{
    /// <summary>
    /// Exchange identifier (e.g., "kraken-futures", "binance").
    /// </summary>
    string ExchangeId { get; }

    /// <summary>
    /// Returns the available instruments for the exchange.
    /// </summary>
    Task<Result<IReadOnlyList<Instrument>>> GetInstrumentsAsync(CancellationToken ct);

    /// <summary>
    /// Returns the latest tickers for the exchange.
    /// </summary>
    Task<Result<IReadOnlyList<Ticker>>> GetTickersAsync(CancellationToken ct);

    /// <summary>
    /// Returns OHLCV candles for a symbol and timeframe.
    /// </summary>
    Task<Result<IReadOnlyList<Candle>>> GetOhlcvAsync(
        string symbol,
        Timeframe timeframe,
        int lookbackBars,
        CancellationToken ct);
}

/// <summary>
/// Common trading operations across exchange integrations.
/// </summary>
public interface ITradingProvider
{
    /// <summary>
    /// Exchange identifier (e.g., "kraken-futures", "binance").
    /// </summary>
    string ExchangeId { get; }

    /// <summary>
    /// Checks trading permissions for the configured API key.
    /// </summary>
    Task<Result<ApiKeyInfo>> CheckApiKeyAsync(CancellationToken ct);

    /// <summary>
    /// Sends a trading order to the exchange.
    /// </summary>
    Task<Result<OrderAck>> SendOrderAsync(SendOrderRequest request, CancellationToken ct);

    /// <summary>
    /// Returns open orders for the exchange account.
    /// </summary>
    Task<Result<IReadOnlyList<OpenOrder>>> GetOpenOrdersAsync(CancellationToken ct);

    /// <summary>
    /// Cancels a specific order.
    /// </summary>
    Task<Result<CancelAck>> CancelOrderAsync(string orderId, CancellationToken ct);

    /// <summary>
    /// Configures the exchange dead-man's switch timeout.
    /// </summary>
    Task<Result<DeadMansSwitchAck>> CancelAllOrdersAfterAsync(int timeoutSeconds, CancellationToken ct);
}
