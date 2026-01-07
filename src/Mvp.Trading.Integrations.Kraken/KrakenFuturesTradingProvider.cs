using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Integrations.Kraken;

/// <summary>
/// Kraken Futures trading provider (private endpoints).
/// </summary>
public sealed class KrakenFuturesTradingProvider : ITradingProvider
{
    public const string KrakenFuturesExchangeId = KrakenFuturesMarketDataProvider.KrakenFuturesExchangeId;

    private readonly HttpClient _httpClient;
    private readonly KrakenFuturesOptions _options;
    private readonly Uri _baseUri;
    private readonly Uri _authBaseUri;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly byte[] _secretBytes;

    public KrakenFuturesTradingProvider(HttpClient httpClient, KrakenFuturesOptions options)
    {
        _httpClient = httpClient;
        _options = options;

        _baseUri = BuildBaseUri(string.IsNullOrWhiteSpace(options.BaseUrl)
            ? KrakenFuturesOptions.DefaultBaseUrl
            : options.BaseUrl);
        _authBaseUri = BuildBaseUri(string.IsNullOrWhiteSpace(options.AuthBaseUrl)
            ? KrakenFuturesOptions.DefaultAuthBaseUrl
            : options.AuthBaseUrl);

        _httpClient.BaseAddress ??= _baseUri;
        _httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mvp.Trading/1.0");

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        _secretBytes = DecodeSecret(options.ApiSecret);
    }

    public string ExchangeId => KrakenFuturesExchangeId;

    public async Task<Result<ApiKeyInfo>> CheckApiKeyAsync(CancellationToken ct)
    {
        var response = await SendPrivateAsync(HttpMethod.Get, _authBaseUri, "api-key", null, ct);
        if (!response.Ok)
        {
            return Fail<ApiKeyInfo>(
                response.Error?.Code ?? "UPSTREAM_ERROR",
                response.Error?.Message ?? "Kraken Futures API key check failed.");
        }

        var payload = response.Value;
        return ParseApiKeyInfo(payload);
    }

    public async Task<Result<OrderAck>> SendOrderAsync(SendOrderRequest request, CancellationToken ct)
    {
        if (request is null)
        {
            return Fail<OrderAck>("VALIDATION", "SendOrderRequest is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Symbol))
        {
            return Fail<OrderAck>("VALIDATION", "Symbol is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Side))
        {
            return Fail<OrderAck>("VALIDATION", "Side is required.");
        }

        if (string.IsNullOrWhiteSpace(request.OrderType))
        {
            return Fail<OrderAck>("VALIDATION", "OrderType is required.");
        }

        if (request.Size <= 0m)
        {
            return Fail<OrderAck>("VALIDATION", "Size must be greater than zero.");
        }

        var symbol = KrakenFuturesSymbolFormatter.Normalize(request.Symbol);
        var side = request.Side.Trim().ToLowerInvariant();
        var orderType = request.OrderType.Trim().ToLowerInvariant();

        var requestPayload = new Dictionary<string, object?>
        {
            ["orderType"] = orderType,
            ["symbol"] = symbol,
            ["side"] = side,
            ["size"] = request.Size
        };

        if (request.LimitPrice.HasValue)
        {
            requestPayload["limitPrice"] = request.LimitPrice.Value;
        }

        if (request.StopPrice.HasValue)
        {
            requestPayload["stopPrice"] = request.StopPrice.Value;
        }

        if (request.ProcessBeforeUtc.HasValue)
        {
            requestPayload["processBefore"] = request.ProcessBeforeUtc.Value.ToUnixTimeMilliseconds();
        }

        if (!string.IsNullOrWhiteSpace(request.ClientOrderId))
        {
            requestPayload["cliOrdId"] = request.ClientOrderId;
        }

        var response = await SendPrivateAsync(HttpMethod.Post, _baseUri, "sendorder", requestPayload, ct);
        if (!response.Ok)
        {
            return Fail<OrderAck>(
                response.Error?.Code ?? "UPSTREAM_ERROR",
                response.Error?.Message ?? "Kraken Futures send order failed.");
        }

        var responsePayload = response.Value;
        return ParseOrderAck(responsePayload, request.ClientOrderId);
    }

    public async Task<Result<IReadOnlyList<OpenOrder>>> GetOpenOrdersAsync(CancellationToken ct)
    {
        var response = await SendPrivateAsync(HttpMethod.Get, _baseUri, "openorders", null, ct);
        if (!response.Ok)
        {
            return Fail<IReadOnlyList<OpenOrder>>(
                response.Error?.Code ?? "UPSTREAM_ERROR",
                response.Error?.Message ?? "Kraken Futures open orders request failed.");
        }

        var responsePayload = response.Value;
        return ParseOpenOrders(responsePayload);
    }

    public async Task<Result<CancelAck>> CancelOrderAsync(string orderId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return Fail<CancelAck>("VALIDATION", "OrderId is required.");
        }

        var requestPayload = new Dictionary<string, object?>
        {
            ["order_id"] = orderId
        };

        var response = await SendPrivateAsync(HttpMethod.Post, _baseUri, "cancelorder", requestPayload, ct);
        if (!response.Ok)
        {
            return Fail<CancelAck>(
                response.Error?.Code ?? "UPSTREAM_ERROR",
                response.Error?.Message ?? "Kraken Futures cancel order failed.");
        }

        var responsePayload = response.Value;
        return ParseCancelAck(responsePayload);
    }

    public async Task<Result<DeadMansSwitchAck>> CancelAllOrdersAfterAsync(int timeoutSeconds, CancellationToken ct)
    {
        if (timeoutSeconds < 0)
        {
            return Fail<DeadMansSwitchAck>("VALIDATION", "Timeout must be non-negative.");
        }

        var requestPayload = new Dictionary<string, object?>
        {
            ["timeout"] = timeoutSeconds
        };

        var response = await SendPrivateAsync(HttpMethod.Post, _baseUri, "cancelallordersafter", requestPayload, ct);
        if (!response.Ok)
        {
            return Fail<DeadMansSwitchAck>(
                response.Error?.Code ?? "UPSTREAM_ERROR",
                response.Error?.Message ?? "Kraken Futures dead-man's switch request failed.");
        }

        var responsePayload = response.Value;
        return ParseDeadMansSwitchAck(responsePayload, timeoutSeconds);
    }

    private async Task<Result<JsonElement>> SendPrivateAsync(
        HttpMethod method,
        Uri baseUri,
        string relativePath,
        object? payload,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || _secretBytes.Length == 0)
        {
            return Fail<JsonElement>("KRAKEN_CREDENTIALS_MISSING", "Kraken Futures API key/secret required.");
        }

        var requestUri = new Uri(baseUri, relativePath);
        var body = payload is null ? string.Empty : JsonSerializer.Serialize(payload, _jsonOptions);

        using var request = new HttpRequestMessage(method, requestUri);
        if (payload is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var signature = Sign(nonce, method.Method, requestUri.PathAndQuery, body);

        request.Headers.TryAddWithoutValidation("apiKey", _options.ApiKey);
        request.Headers.TryAddWithoutValidation("authent", signature);
        request.Headers.TryAddWithoutValidation("nonce", nonce);

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var bodyText = await response.Content.ReadAsStringAsync(ct);
            return ErrorResult<JsonElement>(response.StatusCode, bodyText);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var resultText = ReadString(doc.RootElement, "result");
        if (!string.IsNullOrWhiteSpace(resultText) &&
            !string.Equals(resultText, "success", StringComparison.OrdinalIgnoreCase))
        {
            var errorMessage = ReadString(doc.RootElement, "error")
                ?? $"Kraken Futures response returned result '{resultText}'.";
            return Fail<JsonElement>("KRAKEN_UPSTREAM_ERROR", errorMessage);
        }

        return new Result<JsonElement>(true, doc.RootElement.Clone(), null);
    }

    private static Result<ApiKeyInfo> ParseApiKeyInfo(JsonElement root)
    {
        var payload = SelectPayload(root);
        var key = ReadString(payload, "apiKey") ?? ReadString(payload, "key") ?? string.Empty;
        var permissions = new List<string>();

        if (TryGetArray(payload, "permissions", out var perms))
        {
            foreach (var item in perms.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                {
                    permissions.Add(item.GetString()!);
                }
            }
        }

        if (permissions.Count == 0 && TryGetArray(payload, "perm", out var permAlt))
        {
            foreach (var item in permAlt.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                {
                    permissions.Add(item.GetString()!);
                }
            }
        }

        return new Result<ApiKeyInfo>(true, new ApiKeyInfo(key, permissions), null);
    }

    private static Result<OrderAck> ParseOrderAck(JsonElement root, string? fallbackOrderId)
    {
        var payload = SelectPayload(root, "sendStatus");

        var orderId = ReadString(payload, "orderId")
            ?? ReadString(payload, "order_id")
            ?? ReadString(payload, "id")
            ?? fallbackOrderId;

        if (string.IsNullOrWhiteSpace(orderId))
        {
            return Fail<OrderAck>("PARSE_ERROR", "Kraken Futures response missing order id.");
        }

        var status = ReadString(payload, "status")
            ?? ReadString(payload, "orderStatus")
            ?? "UNKNOWN";

        var ts = ReadTimestamp(payload, "timestamp")
            ?? ReadTimestamp(payload, "time")
            ?? DateTimeOffset.UtcNow;

        return new Result<OrderAck>(true, new OrderAck(orderId, status, ts), null);
    }

    private static Result<IReadOnlyList<OpenOrder>> ParseOpenOrders(JsonElement root)
    {
        var payload = SelectPayload(root);
        if (!TryGetArray(payload, "openOrders", out var array) && !TryGetArray(payload, "orders", out array))
        {
            return Fail<IReadOnlyList<OpenOrder>>("PARSE_ERROR", "Kraken Futures response missing open orders list.");
        }

        var results = new List<OpenOrder>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var orderId = ReadString(item, "order_id") ?? ReadString(item, "orderId") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(orderId))
            {
                continue;
            }

            var symbol = ReadString(item, "symbol") ?? string.Empty;
            var side = ReadString(item, "side") ?? string.Empty;
            var size = ReadDecimal(item, "size") ?? 0m;
            var type = ReadString(item, "type") ?? ReadString(item, "orderType") ?? string.Empty;
            var price = ReadDecimal(item, "limitPrice") ?? ReadDecimal(item, "price");

            results.Add(new OpenOrder(orderId, symbol, side, size, type, price));
        }

        return new Result<IReadOnlyList<OpenOrder>>(true, results, null);
    }

    private static Result<CancelAck> ParseCancelAck(JsonElement root)
    {
        var payload = SelectPayload(root);
        var result = ReadString(payload, "result")
            ?? ReadString(payload, "status")
            ?? "UNKNOWN";

        return new Result<CancelAck>(true, new CancelAck(result), null);
    }

    private static Result<DeadMansSwitchAck> ParseDeadMansSwitchAck(JsonElement root, int fallbackTimeout)
    {
        var payload = SelectPayload(root);
        var timeout = ReadInt(payload, "timeout")
            ?? ReadInt(payload, "timeoutSeconds")
            ?? fallbackTimeout;

        return new Result<DeadMansSwitchAck>(true, new DeadMansSwitchAck(timeout), null);
    }

    private static JsonElement SelectPayload(JsonElement root, string? preferredProperty = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredProperty) &&
            TryGetProperty(root, preferredProperty, out var preferred) &&
            preferred.ValueKind == JsonValueKind.Object)
        {
            return preferred;
        }

        if (TryGetProperty(root, "result", out var result) && result.ValueKind == JsonValueKind.Object)
        {
            return result;
        }

        return root;
    }

    private string Sign(string nonce, string method, string pathAndQuery, string body)
    {
        // Kraken Futures signing uses nonce + METHOD + path + body hashed with the API secret.
        var message = $"{nonce}{method.ToUpperInvariant()}{pathAndQuery}{body}";
        using var hmac = new HMACSHA512(_secretBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return Convert.ToBase64String(hash);
    }

    private static Uri BuildBaseUri(string value)
    {
        var url = value;
        if (!url.EndsWith("/", StringComparison.Ordinal))
        {
            url += "/";
        }

        return new Uri(url, UriKind.Absolute);
    }

    private static byte[] DecodeSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return Array.Empty<byte>();
        }

        try
        {
            return Convert.FromBase64String(secret);
        }
        catch (FormatException)
        {
            return Encoding.UTF8.GetBytes(secret);
        }
    }

    private static Result<T> ErrorResult<T>(HttpStatusCode statusCode, string? body)
    {
        var meta = new Dictionary<string, string?>
        {
            ["status"] = ((int)statusCode).ToString(CultureInfo.InvariantCulture)
        };

        var code = statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden
            ? "KRAKEN_AUTH_FAILED"
            : statusCode == (HttpStatusCode)429
                ? "RATE_LIMIT"
                : "UPSTREAM_ERROR";

        var message = string.IsNullOrWhiteSpace(body)
            ? $"Kraken Futures request failed with status {(int)statusCode}."
            : $"Kraken Futures request failed with status {(int)statusCode}: {body}";

        return new Result<T>(false, default, new Error(code, message, meta));
    }

    private static Result<T> Fail<T>(string code, string message)
    {
        return new Result<T>(false, default, new Error(code, message, null));
    }

    private static bool TryGetArray(JsonElement element, string name, out JsonElement array)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Array)
            {
                array = prop.Value;
                return true;
            }
        }

        array = default;
        return false;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };
    }

    private static decimal? ReadDecimal(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static int? ReadInt(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var numeric))
        {
            if (numeric > 10_000_000_000)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(numeric);
            }

            return DateTimeOffset.FromUnixTimeSeconds(numeric);
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (long.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var numericString))
            {
                if (numericString > 10_000_000_000)
                {
                    return DateTimeOffset.FromUnixTimeMilliseconds(numericString);
                }

                return DateTimeOffset.FromUnixTimeSeconds(numericString);
            }

            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return parsed.ToUniversalTime();
            }
        }

        return null;
    }
}
