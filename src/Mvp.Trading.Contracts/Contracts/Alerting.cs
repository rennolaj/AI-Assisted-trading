using System;

namespace Mvp.Trading.Contracts;

/// <summary>
/// Canonical alert event normalized from a TradingView webhook payload.
/// </summary>
public sealed record AlertEvent(
    Guid AlertId,
    DateTimeOffset ReceivedAtUtc,
    string Source,
    string IdempotencyKey,
    TradingViewFields Tv,
    IntentFields Intent,
    string RawPayload
);

/// <summary>
/// TradingView-specific fields parsed from the incoming alert payload.
/// </summary>
public sealed record TradingViewFields(
    string Ticker,
    string Exchange,
    string Interval,
    decimal? Close,
    decimal? Volume
);

/// <summary>
/// Normalized intent hints extracted from the alert payload.
/// </summary>
public sealed record IntentFields(
    string DirectionHint,
    string SymbolHint,
    string Reason
);
