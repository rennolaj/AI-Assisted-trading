namespace Mvp.Trading.Api.Services;

/// <summary>
/// Configuration for TradingView webhook ingestion.
/// </summary>
public sealed class TradingViewOptions
{
    /// <summary>
    /// Shared secret used in the webhook route path.
    /// </summary>
    public string WebhookSecret { get; init; } = string.Empty;
}
