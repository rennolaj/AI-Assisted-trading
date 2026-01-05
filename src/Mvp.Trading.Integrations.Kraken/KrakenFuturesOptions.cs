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
    /// Default environment label.
    /// </summary>
    public const string DefaultEnvironment = "prod";

    /// <summary>
    /// Default test symbol for integration validation.
    /// </summary>
    public const string DefaultTestSymbol = "PI_XBTUSD";

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
}
