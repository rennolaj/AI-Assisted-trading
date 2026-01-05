namespace Mvp.Trading.Integrations.Kraken;

/// <summary>
/// Environment-specific Kraken Futures endpoints and defaults.
/// </summary>
public sealed class KrakenFuturesEnvironmentOptions
{
    /// <summary>
    /// Base URL for Kraken Futures REST API.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Base URL for Kraken Futures auth endpoints.
    /// </summary>
    public string AuthBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// WebSocket URL for Kraken Futures streaming endpoints.
    /// </summary>
    public string WebSocketUrl { get; set; } = string.Empty;

    /// <summary>
    /// Default test symbol for the environment.
    /// </summary>
    public string TestSymbol { get; set; } = string.Empty;
}
