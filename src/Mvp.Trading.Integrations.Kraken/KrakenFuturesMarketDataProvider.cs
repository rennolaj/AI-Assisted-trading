using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Integrations.Kraken;

/// <summary>
/// Kraken Futures market data provider.
/// </summary>
public sealed class KrakenFuturesMarketDataProvider : IMarketDataProvider
{
    public const string KrakenFuturesExchangeId = "kraken-futures";

    private readonly HttpClient _httpClient;
    private readonly KrakenFuturesCacheOptions _cacheOptions;
    private readonly KrakenFuturesRateLimitOptions _rateLimitOptions;
    private readonly KrakenFuturesRateLimitBudget _budget;
    private readonly IMemoryCache _cache;
    private readonly JsonSerializerOptions _jsonOptions;

    public KrakenFuturesMarketDataProvider(
        HttpClient httpClient,
        KrakenFuturesOptions options,
        KrakenFuturesCacheOptions cacheOptions,
        KrakenFuturesRateLimitOptions rateLimitOptions,
        KrakenFuturesRateLimitBudget budget,
        IMemoryCache cache)
    {
        _httpClient = httpClient;
        _cacheOptions = cacheOptions;
        _rateLimitOptions = rateLimitOptions;
        _budget = budget;
        _cache = cache;

        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
            ? KrakenFuturesOptions.DefaultBaseUrl
            : options.BaseUrl;
        if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
        {
            baseUrl += "/";
        }

        _httpClient.BaseAddress ??= new Uri(baseUrl, UriKind.Absolute);
        _httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mvp.Trading/1.0");

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public string ExchangeId => KrakenFuturesExchangeId;

    public async Task<Result<IReadOnlyList<Instrument>>> GetInstrumentsAsync(CancellationToken ct)
    {
        const string cacheKey = "kraken:instruments";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<Instrument>? cached) && cached is not null)
        {
            return new Result<IReadOnlyList<Instrument>>(true, cached, null);
        }

        if (!_budget.TryConsume(_rateLimitOptions.InstrumentsCost))
        {
            return RateLimitError<IReadOnlyList<Instrument>>();
        }

        var response = await _httpClient.GetAsync("instruments", ct);
        if (!response.IsSuccessStatusCode)
        {
            return ErrorResult<IReadOnlyList<Instrument>>("GetInstrumentsAsync", response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!TryGetArray(doc.RootElement, "instruments", out var instruments))
        {
            return ParseError<IReadOnlyList<Instrument>>("instruments");
        }

        var results = new List<Instrument>();
        foreach (var item in instruments.EnumerateArray())
        {
            var symbol = ReadString(item, "symbol") ?? string.Empty;
            var underlying = ReadString(item, "underlying") ?? ReadString(item, "base") ?? string.Empty;
            var quote = ReadString(item, "quote") ?? string.Empty;
            var tradable = ReadBool(item, "tradeable") ?? ReadBool(item, "tradable") ?? false;

            if (!string.IsNullOrWhiteSpace(symbol))
            {
                results.Add(new Instrument(symbol, underlying, quote, tradable));
            }
        }

        CacheSet(cacheKey, results, _cacheOptions.InstrumentsTtlSeconds);
        return new Result<IReadOnlyList<Instrument>>(true, results, null);
    }

    public async Task<Result<IReadOnlyList<Ticker>>> GetTickersAsync(CancellationToken ct)
    {
        const string cacheKey = "kraken:tickers";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<Ticker>? cached) && cached is not null)
        {
            return new Result<IReadOnlyList<Ticker>>(true, cached, null);
        }

        if (!_budget.TryConsume(_rateLimitOptions.TickersCost))
        {
            return RateLimitError<IReadOnlyList<Ticker>>();
        }

        var response = await _httpClient.GetAsync("tickers", ct);
        if (!response.IsSuccessStatusCode)
        {
            return ErrorResult<IReadOnlyList<Ticker>>("GetTickersAsync", response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!TryGetArray(doc.RootElement, "tickers", out var tickers))
        {
            return ParseError<IReadOnlyList<Ticker>>("tickers");
        }

        var results = new List<Ticker>();
        foreach (var item in tickers.EnumerateArray())
        {
            var symbol = ReadString(item, "symbol") ?? string.Empty;
            var last = ReadDecimal(item, "last") ?? 0m;
            var bid = ReadDecimal(item, "bid") ?? 0m;
            var ask = ReadDecimal(item, "ask") ?? 0m;
            var ts = ReadTimestamp(item, "time") ?? ReadTimestamp(item, "timestamp") ?? DateTimeOffset.UtcNow;

            if (!string.IsNullOrWhiteSpace(symbol))
            {
                results.Add(new Ticker(symbol, last, bid, ask, ts));
            }
        }

        CacheSet(cacheKey, results, _cacheOptions.TickersTtlSeconds);
        return new Result<IReadOnlyList<Ticker>>(true, results, null);
    }

    public async Task<Result<IReadOnlyList<Candle>>> GetOhlcvAsync(string symbol, Timeframe timeframe, int lookbackBars, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return new Result<IReadOnlyList<Candle>>(
                false,
                null,
                new Error("VALIDATION", "Symbol is required.", null));
        }

        if (lookbackBars <= 0)
        {
            return new Result<IReadOnlyList<Candle>>(
                false,
                null,
                new Error("VALIDATION", "Lookback bars must be greater than zero.", null));
        }

        var interval = TimeframeToMinutes(timeframe);
        var cacheKey = $"kraken:candles:{symbol}:{interval}:{lookbackBars}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<Candle>? cached) && cached is not null)
        {
            return new Result<IReadOnlyList<Candle>>(true, cached, null);
        }

        if (!_budget.TryConsume(_rateLimitOptions.CandlesCost))
        {
            return RateLimitError<IReadOnlyList<Candle>>();
        }

        var uri = $"history?symbol={Uri.EscapeDataString(symbol)}&interval={interval}&last={lookbackBars}";
        var response = await _httpClient.GetAsync(uri, ct);
        if (!response.IsSuccessStatusCode)
        {
            return ErrorResult<IReadOnlyList<Candle>>("GetOhlcvAsync", response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        List<Candle> results;
        if (TryGetArray(doc.RootElement, "candles", out var candles))
        {
            results = ParseCandles(candles);
        }
        else if (TryGetArray(doc.RootElement, "history", out var history))
        {
            results = BuildCandlesFromHistory(history, interval, lookbackBars);
        }
        else
        {
            return ParseError<IReadOnlyList<Candle>>("candles/history");
        }

        CacheSet(cacheKey, results, _cacheOptions.CandlesTtlSeconds);
        return new Result<IReadOnlyList<Candle>>(true, results, null);
    }

    private void CacheSet<T>(string key, T value, int ttlSeconds)
    {
        if (ttlSeconds <= 0)
        {
            return;
        }

        _cache.Set(key, value, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(ttlSeconds)
        });
    }

    private static int TimeframeToMinutes(Timeframe timeframe) => timeframe switch
    {
        Timeframe.M1 => 1,
        Timeframe.M5 => 5,
        Timeframe.M15 => 15,
        Timeframe.M30 => 30,
        Timeframe.H1 => 60,
        Timeframe.H2 => 120,
        Timeframe.H4 => 240,
        Timeframe.H12 => 720,
        Timeframe.D1 => 1440,
        _ => 1
    };

    private static Result<T> RateLimitError<T>()
    {
        return new Result<T>(
            false,
            default,
            new Error("RATE_LIMIT", "Kraken Futures rate-limit budget exhausted.", null));
    }

    private static Result<T> ErrorResult<T>(string operation, HttpStatusCode statusCode)
    {
        var meta = new Dictionary<string, string?>
        {
            ["status"] = ((int)statusCode).ToString(CultureInfo.InvariantCulture)
        };

        return new Result<T>(
            false,
            default,
            new Error("UPSTREAM_ERROR", $"Kraken Futures {operation} failed with status {(int)statusCode}.", meta));
    }

    private static Result<T> ParseError<T>(string field)
    {
        return new Result<T>(
            false,
            default,
            new Error("PARSE_ERROR", $"Kraken Futures response missing '{field}' array.", null));
    }

    private static List<Candle> ParseCandles(JsonElement candles)
    {
        var results = new List<Candle>();
        foreach (var item in candles.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Array)
            {
                var values = item.EnumerateArray().ToArray();
                if (values.Length < 5)
                {
                    continue;
                }

                var ts = ReadTimestampValue(values[0]) ?? DateTimeOffset.UtcNow;
                var open = ReadDecimalValue(values[1]) ?? 0m;
                var high = ReadDecimalValue(values[2]) ?? 0m;
                var low = ReadDecimalValue(values[3]) ?? 0m;
                var close = ReadDecimalValue(values[4]) ?? 0m;
                var volume = values.Length > 5 ? (ReadDecimalValue(values[5]) ?? 0m) : 0m;

                results.Add(new Candle(ts, open, high, low, close, volume));
                continue;
            }

            var tsObject = ReadTimestamp(item, "time") ?? DateTimeOffset.UtcNow;
            var openObject = ReadDecimal(item, "open") ?? 0m;
            var highObject = ReadDecimal(item, "high") ?? 0m;
            var lowObject = ReadDecimal(item, "low") ?? 0m;
            var closeObject = ReadDecimal(item, "close") ?? 0m;
            var volumeObject = ReadDecimal(item, "volume") ?? ReadDecimal(item, "vol") ?? 0m;

            results.Add(new Candle(tsObject, openObject, highObject, lowObject, closeObject, volumeObject));
        }

        return results;
    }

    private static List<Candle> BuildCandlesFromHistory(JsonElement history, int intervalMinutes, int maxBars)
    {
        var trades = new List<TradeSample>();
        foreach (var item in history.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var ts = ReadTimestamp(item, "time");
            var price = ReadDecimal(item, "price");
            if (ts is null || price is null)
            {
                continue;
            }

            var size = ReadDecimal(item, "size") ?? 0m;
            trades.Add(new TradeSample(ts.Value, price.Value, size));
        }

        if (trades.Count == 0)
        {
            return new List<Candle>();
        }

        trades.Sort(static (left, right) => left.TimeUtc.CompareTo(right.TimeUtc));

        var intervalSeconds = Math.Max(1, intervalMinutes) * 60;
        var buckets = new Dictionary<long, CandleBuilder>();

        foreach (var trade in trades)
        {
            var bucketStartSeconds = (trade.TimeUtc.ToUnixTimeSeconds() / intervalSeconds) * intervalSeconds;
            if (!buckets.TryGetValue(bucketStartSeconds, out var builder))
            {
                builder = new CandleBuilder(DateTimeOffset.FromUnixTimeSeconds(bucketStartSeconds));
                buckets[bucketStartSeconds] = builder;
            }

            builder.Update(trade.Price, trade.Size);
        }

        var ordered = buckets
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => kvp.Value.ToCandle())
            .ToList();

        if (maxBars > 0 && ordered.Count > maxBars)
        {
            ordered = ordered.Skip(ordered.Count - maxBars).ToList();
        }

        return ordered;
    }

    private sealed record TradeSample(DateTimeOffset TimeUtc, decimal Price, decimal Size);

    private sealed class CandleBuilder
    {
        private bool _hasValue;

        public CandleBuilder(DateTimeOffset openTimeUtc)
        {
            OpenTimeUtc = openTimeUtc;
        }

        public DateTimeOffset OpenTimeUtc { get; }

        public decimal Open { get; private set; }

        public decimal High { get; private set; }

        public decimal Low { get; private set; }

        public decimal Close { get; private set; }

        public decimal Volume { get; private set; }

        public void Update(decimal price, decimal size)
        {
            if (!_hasValue)
            {
                _hasValue = true;
                Open = price;
                High = price;
                Low = price;
                Close = price;
                Volume = size;
                return;
            }

            if (price > High)
            {
                High = price;
            }

            if (price < Low)
            {
                Low = price;
            }

            Close = price;
            Volume += size;
        }

        public Candle ToCandle()
        {
            if (!_hasValue)
            {
                return new Candle(OpenTimeUtc, 0m, 0m, 0m, 0m, 0m);
            }

            return new Candle(OpenTimeUtc, Open, High, Low, Close, Volume);
        }
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

        return ReadDecimalValue(value);
    }

    private static decimal? ReadDecimalValue(JsonElement value)
    {
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

    private static bool? ReadBool(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed))
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

        return ReadTimestampValue(value);
    }

    private static DateTimeOffset? ReadTimestampValue(JsonElement value)
    {
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
}
