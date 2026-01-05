using System.Text.Json.Serialization;

namespace Mvp.Trading.Api.Models;

/// <summary>
/// Incoming TradingView webhook payload fields required for normalization.
/// </summary>
public sealed record TradingViewWebhookPayload(
    [property: JsonPropertyName("idempotencyKey")] string IdempotencyKey,
    [property: JsonPropertyName("ticker")] string Ticker,
    [property: JsonPropertyName("exchange")] string Exchange,
    [property: JsonPropertyName("interval")] string Interval,
    [property: JsonPropertyName("close")] decimal? Close,
    [property: JsonPropertyName("volume")] decimal? Volume,
    [property: JsonPropertyName("directionHint")] string DirectionHint,
    [property: JsonPropertyName("symbolHint")] string SymbolHint,
    [property: JsonPropertyName("reason")] string Reason
);
