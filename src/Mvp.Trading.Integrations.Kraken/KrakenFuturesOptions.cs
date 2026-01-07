namespace Mvp.Trading.Integrations.Kraken;

/// <summary>
/// Configuration for Kraken Futures integration.
/// </summary>
public sealed class KrakenFuturesOptions
{
    /// <summary>
    /// Default production base URL.
    /// </summary>
    public const string DefaultBaseUrl = "https://futures.kraken.com/derivatives/api/v3";

    /// <summary>
    /// Default production auth base URL.
    /// </summary>
    public const string DefaultAuthBaseUrl = "https://futures.kraken.com/api/auth/v1";

    /// <summary>
    /// Default production WebSocket URL.
    /// </summary>
    public const string DefaultWebSocketUrl = "wss://futures.kraken.com/ws/v1";

    /// <summary>
    /// Default production Charts API base URL.
    /// </summary>
    public const string DefaultChartsBaseUrl = "https://futures.kraken.com/api/charts/v1";

    /// <summary>
    /// Default environment label.
    /// </summary>
    public const string DefaultEnvironment = "prod";

    /// <summary>
    /// Default test symbol for integration validation.
    /// </summary>
    public const string DefaultTestSymbol = "BTCUSD.P";

    /// <summary>
    /// Kraken Futures environment (demo or prod).
    /// </summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>
    /// Base URL for Kraken Futures REST API.
    /// </summary>
    public string BaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>
    /// Base URL for Kraken Futures auth endpoints.
    /// </summary>
    public string AuthBaseUrl { get; set; } = DefaultAuthBaseUrl;

    /// <summary>
    /// WebSocket URL for Kraken Futures streaming endpoints.
    /// </summary>
    public string WebSocketUrl { get; set; } = DefaultWebSocketUrl;

    /// <summary>
    /// Base URL for Kraken Futures Charts API (OHLC candles).
    /// </summary>
    public string ChartsBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Charts tick type (trade, mark, spot).
    /// </summary>
    public string ChartsTickType { get; set; } = "trade";

    /// <summary>
    /// Maximum candles returned per charts request.
    /// </summary>
    public int ChartsMaxCandlesPerRequest { get; set; } = 500;

    /// <summary>
    /// Maximum charts batches to page when more candles are needed.
    /// </summary>
    public int ChartsMaxBatches { get; set; } = 10;

    /// <summary>
    /// Allow fallback to trade-history candles when charts is unavailable or insufficient.
    /// </summary>
    public bool ChartsFallbackToHistory { get; set; } = true;

    /// <summary>
    /// Test symbol used by integration validation.
    /// </summary>
    public string TestSymbol { get; set; } = DefaultTestSymbol;

    /// <summary>
    /// Request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// API key for private Kraken Futures endpoints.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// API secret for private Kraken Futures endpoints.
    /// </summary>
    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>
    /// Demo environment API key (used when Environment is demo).
    /// </summary>
    public string DemoApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Demo environment API secret (used when Environment is demo).
    /// </summary>
    public string DemoApiSecret { get; set; } = string.Empty;

    /// <summary>
    /// Production environment API key (used when Environment is prod).
    /// </summary>
    public string ProdApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Production environment API secret (used when Environment is prod).
    /// </summary>
    public string ProdApiSecret { get; set; } = string.Empty;
}
