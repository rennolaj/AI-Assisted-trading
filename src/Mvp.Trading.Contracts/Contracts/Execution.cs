using System;
using System.Collections.Generic;

namespace Mvp.Trading.Contracts;

/// <summary>
/// Describes a Kraken API key and its permissions.
/// </summary>
public sealed record ApiKeyInfo(string Key, IReadOnlyList<string> Permissions);

/// <summary>
/// Request payload for sending an order to the exchange.
/// </summary>
public sealed record SendOrderRequest(
    string Symbol,
    string Side,
    decimal Size,
    string OrderType,
    decimal? LimitPrice,
    decimal? StopPrice,
    DateTimeOffset? ProcessBeforeUtc,
    string? ClientOrderId
);

/// <summary>
/// Acknowledgement returned by the exchange for an order request.
/// </summary>
public sealed record OrderAck(string OrderId, string Status, DateTimeOffset TsUtc);

/// <summary>
/// An open order currently tracked on the exchange.
/// </summary>
public sealed record OpenOrder(
    string OrderId,
    string Symbol,
    string Side,
    decimal Size,
    string Type,
    decimal? Price
);

/// <summary>
/// Acknowledgement for cancel operations.
/// </summary>
public sealed record CancelAck(string Result);

/// <summary>
/// Response to a dead-man's switch request.
/// </summary>
public sealed record DeadMansSwitchAck(int TimeoutSeconds);
