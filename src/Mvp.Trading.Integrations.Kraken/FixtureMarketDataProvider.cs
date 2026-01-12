using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Integrations.Kraken;

/// <summary>
/// Fixture-backed market data provider for demo E2E validation.
/// </summary>
public sealed class FixtureMarketDataProvider : IMarketDataProvider
{
    private readonly IMarketDataProvider _fallback;
    private readonly MarketDataOptions _options;
    private readonly ILogger<FixtureMarketDataProvider> _logger;
    private readonly Lazy<FixtureCatalog> _catalog;

    public FixtureMarketDataProvider(
        IMarketDataProvider fallback,
        MarketDataOptions options,
        ILogger<FixtureMarketDataProvider> logger)
    {
        _fallback = fallback;
        _options = options;
        _logger = logger;
        _catalog = new Lazy<FixtureCatalog>(LoadCatalog);
    }

    public string ExchangeId => _fallback.ExchangeId;

    public Task<Result<IReadOnlyList<Instrument>>> GetInstrumentsAsync(CancellationToken ct)
    {
        return _fallback.GetInstrumentsAsync(ct);
    }

    public Task<Result<IReadOnlyList<Ticker>>> GetTickersAsync(CancellationToken ct)
    {
        return _fallback.GetTickersAsync(ct);
    }

    public Task<Result<IReadOnlyList<Candle>>> GetOhlcvAsync(
        string symbol,
        Timeframe timeframe,
        int lookbackBars,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return Task.FromResult(new Result<IReadOnlyList<Candle>>(
                false,
                null,
                new Error("FIXTURE_SYMBOL_MISSING", "Symbol is required.", null)));
        }

        if (lookbackBars <= 0)
        {
            return Task.FromResult(new Result<IReadOnlyList<Candle>>(
                false,
                null,
                new Error("FIXTURE_LOOKBACK_INVALID", "Lookback bars must be greater than zero.", null)));
        }

        var intervalMinutes = TimeframeToMinutes(timeframe);
        var catalog = _catalog.Value;
        var series = catalog.ResolveSeries(symbol, intervalMinutes);

        if (series is null)
        {
            return Task.FromResult(new Result<IReadOnlyList<Candle>>(
                false,
                null,
                new Error("FIXTURE_NOT_FOUND", $"Fixture series not found for {symbol} @ {intervalMinutes}m.", null)));
        }

        if (series.Candles.Count == 0)
        {
            return Task.FromResult(new Result<IReadOnlyList<Candle>>(
                false,
                null,
                new Error("FIXTURE_EMPTY", $"Fixture series for {symbol} @ {intervalMinutes}m was empty.", null)));
        }

        var resolved = EnsureLookback(series.Candles, lookbackBars, intervalMinutes, _options.ExtendFixtures);
        if (resolved is null)
        {
            return Task.FromResult(new Result<IReadOnlyList<Candle>>(
                false,
                null,
                new Error("FIXTURE_LOOKBACK_SHORT", $"Fixture has {series.Candles.Count} bars, need {lookbackBars}.", null)));
        }

        return Task.FromResult(new Result<IReadOnlyList<Candle>>(true, resolved, null));
    }

    private FixtureCatalog LoadCatalog()
    {
        var catalog = new FixtureCatalog();
        var basePath = ResolvePath(_options.FixturesPath);
        if (!Directory.Exists(basePath))
        {
            _logger.LogWarning("Fixture path {Path} does not exist.", basePath);
            return catalog;
        }

        foreach (var file in Directory.EnumerateFiles(basePath, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var series = LoadFixture(file);
                if (series is null)
                {
                    continue;
                }

                catalog.AddSeries(series);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load fixture {File}.", file);
            }
        }

        if (!catalog.HasData)
        {
            _logger.LogWarning("No fixtures loaded from {Path}.", basePath);
        }

        return catalog;
    }

    private static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.Combine(AppContext.BaseDirectory, path);
    }

    private static FixtureSeries? LoadFixture(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        if (!root.TryGetProperty("symbol", out var symbolElement) ||
            !root.TryGetProperty("intervalMinutes", out var intervalElement) ||
            !root.TryGetProperty("candles", out var candlesElement))
        {
            return null;
        }

        var symbol = symbolElement.GetString();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        var intervalMinutes = intervalElement.GetInt32();
        var candles = new List<Candle>();

        foreach (var item in candlesElement.EnumerateArray())
        {
            var values = item.ValueKind == JsonValueKind.Array ? item.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
            if (values.Length < 6)
            {
                continue;
            }

            var tsSeconds = ReadLong(values[0]);
            var open = ReadDecimal(values[1]);
            var high = ReadDecimal(values[2]);
            var low = ReadDecimal(values[3]);
            var close = ReadDecimal(values[4]);
            var volume = values.Length > 6 ? ReadDecimal(values[6]) : 0m;

            var openTime = DateTimeOffset.FromUnixTimeMilliseconds(tsSeconds);
            candles.Add(new Candle(openTime, open, high, low, close, volume));
        }

        candles = candles.OrderBy(c => c.OpenTimeUtc).ToList();
        return new FixtureSeries(symbol, intervalMinutes, candles);
    }

    private static IReadOnlyList<Candle>? EnsureLookback(
        IReadOnlyList<Candle> candles,
        int lookbackBars,
        int intervalMinutes,
        bool extendFixtures)
    {
        if (candles.Count >= lookbackBars)
        {
            return candles.Skip(candles.Count - lookbackBars).ToList();
        }

        if (!extendFixtures)
        {
            return null;
        }

        if (candles.Count == 0)
        {
            return null;
        }

        var interval = TimeSpan.FromMinutes(intervalMinutes);
        var needed = lookbackBars - candles.Count;
        var prefix = new List<Candle>(needed);
        var timeCursor = candles[0].OpenTimeUtc;

        for (var i = 0; i < needed; i++)
        {
            var source = candles[(candles.Count - 1 - (i % candles.Count))];
            timeCursor = timeCursor.Subtract(interval);
            prefix.Add(new Candle(
                timeCursor,
                source.Open,
                source.High,
                source.Low,
                source.Close,
                source.Volume));
        }

        prefix.Reverse();
        var result = new List<Candle>(lookbackBars);
        result.AddRange(prefix);
        result.AddRange(candles);
        return result;
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

    private static List<Candle> AggregateCandles(IReadOnlyList<Candle> candles, int intervalMinutes)
    {
        if (candles.Count == 0)
        {
            return new List<Candle>();
        }

        var intervalSeconds = Math.Max(1, intervalMinutes) * 60;
        var buckets = new SortedDictionary<long, CandleBuilder>();

        foreach (var candle in candles.OrderBy(c => c.OpenTimeUtc))
        {
            var bucketStartSeconds = (candle.OpenTimeUtc.ToUnixTimeSeconds() / intervalSeconds) * intervalSeconds;
            if (!buckets.TryGetValue(bucketStartSeconds, out var builder))
            {
                builder = new CandleBuilder(DateTimeOffset.FromUnixTimeSeconds(bucketStartSeconds));
                buckets[bucketStartSeconds] = builder;
            }

            builder.Update(candle);
        }

        return buckets.Values.Select(b => b.ToCandle()).ToList();
    }

    private static long ReadLong(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetInt64(),
            JsonValueKind.String when long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            _ => 0L
        };
    }

    private static decimal ReadDecimal(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) => value,
            _ => 0m
        };
    }

    private sealed record FixtureSeries(string Symbol, int IntervalMinutes, IReadOnlyList<Candle> Candles);

    private sealed class FixtureCatalog
    {
        private readonly Dictionary<string, Dictionary<int, FixtureSeries>> _seriesBySymbol =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new();

        public bool HasData
        {
            get
            {
                lock (_sync)
                {
                    return _seriesBySymbol.Count > 0;
                }
            }
        }

        public void AddSeries(FixtureSeries series)
        {
            lock (_sync)
            {
                if (!_seriesBySymbol.TryGetValue(series.Symbol, out var byInterval))
                {
                    byInterval = new Dictionary<int, FixtureSeries>();
                    _seriesBySymbol[series.Symbol] = byInterval;
                }

                if (byInterval.TryGetValue(series.IntervalMinutes, out var existing) &&
                    existing.Candles.Count >= series.Candles.Count)
                {
                    return;
                }

                byInterval[series.IntervalMinutes] = series;
            }
        }

        public FixtureSeries? ResolveSeries(string symbol, int intervalMinutes)
        {
            lock (_sync)
            {
                if (!_seriesBySymbol.TryGetValue(symbol, out var byInterval))
                {
                    return null;
                }

                if (byInterval.TryGetValue(intervalMinutes, out var exact))
                {
                    return exact;
                }

                var baseSeries = byInterval
                    .Where(pair => pair.Key <= intervalMinutes)
                    .OrderBy(pair => pair.Key)
                    .Select(pair => pair.Value)
                    .FirstOrDefault();

                if (baseSeries is null || baseSeries.IntervalMinutes == intervalMinutes)
                {
                    return baseSeries;
                }

                var aggregated = new FixtureSeries(
                    baseSeries.Symbol,
                    intervalMinutes,
                    AggregateCandles(baseSeries.Candles, intervalMinutes));
                byInterval[intervalMinutes] = aggregated;
                return aggregated;
            }
        }
    }

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

        public void Update(Candle candle)
        {
            if (!_hasValue)
            {
                Open = candle.Open;
                High = candle.High;
                Low = candle.Low;
                Close = candle.Close;
                Volume = candle.Volume;
                _hasValue = true;
                return;
            }

            if (candle.High > High)
            {
                High = candle.High;
            }

            if (candle.Low < Low)
            {
                Low = candle.Low;
            }

            Close = candle.Close;
            Volume += candle.Volume;
        }

        public Candle ToCandle()
        {
            return new Candle(OpenTimeUtc, Open, High, Low, Close, Volume);
        }
    }
}
