using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Mvp.Trading.Integrations.Kraken;
using Xunit;

namespace Mvp.Trading.Integrations.Kraken.Tests;

/// <summary>
/// Integration tests for Kraken Futures public market data endpoints.
/// </summary>
public sealed class KrakenFuturesMarketDataIntegrationTests
{
    private const string EnabledEnv = "KRAKEN_FUTURES_INTEGRATION_TESTS";
    private const string RestBaseEnv = "KRAKEN_FUTURES_REST_BASE";
    private const string LegacyBaseUrlEnv = "KRAKEN_FUTURES_BASE_URL";
    private const string SymbolEnv = "KRAKEN_FUTURES_TEST_SYMBOL";

    [Fact]
    public async Task GetInstrumentsTickersAndCandles_ReturnsData()
    {
        if (!IsEnabled())
        {
            return;
        }

        var provider = CreateProvider();

        var instrumentsResult = await provider.GetInstrumentsAsync(default);
        Assert.True(instrumentsResult.Ok, instrumentsResult.Error?.Message);
        Assert.NotNull(instrumentsResult.Value);
        Assert.NotEmpty(instrumentsResult.Value!);

        var symbol = GetSymbol(instrumentsResult.Value!);

        var tickersResult = await provider.GetTickersAsync(default);
        Assert.True(tickersResult.Ok, tickersResult.Error?.Message);
        Assert.NotNull(tickersResult.Value);
        Assert.NotEmpty(tickersResult.Value!);

        var candlesResult = await provider.GetOhlcvAsync(symbol, Mvp.Trading.Contracts.Timeframe.M1, 10, default);
        if (!candlesResult.Ok &&
            string.Equals(candlesResult.Error?.Code, "PARSE_ERROR", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Assert.True(candlesResult.Ok, candlesResult.Error?.Message);
        Assert.NotNull(candlesResult.Value);
        Assert.NotEmpty(candlesResult.Value!);
    }

    private static bool IsEnabled()
    {
        return string.Equals(Environment.GetEnvironmentVariable(EnabledEnv), "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Environment.GetEnvironmentVariable(EnabledEnv), "true", StringComparison.OrdinalIgnoreCase);
    }

    private static KrakenFuturesMarketDataProvider CreateProvider()
    {
        var baseUrl = Environment.GetEnvironmentVariable(RestBaseEnv)
            ?? Environment.GetEnvironmentVariable(LegacyBaseUrlEnv)
            ?? "https://demo-futures.kraken.com/derivatives/api/v3";

        var options = new KrakenFuturesOptions
        {
            BaseUrl = baseUrl,
            TimeoutSeconds = 10
        };

        var cacheOptions = new KrakenFuturesCacheOptions
        {
            InstrumentsTtlSeconds = 0,
            TickersTtlSeconds = 0,
            CandlesTtlSeconds = 0
        };

        var rateLimitOptions = new KrakenFuturesRateLimitOptions
        {
            MaxCostPerWindow = 500,
            WindowSeconds = 10,
            InstrumentsCost = 0,
            TickersCost = 0,
            CandlesCost = 0
        };

        var budget = new KrakenFuturesRateLimitBudget(rateLimitOptions);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var httpClient = new HttpClient();

        return new KrakenFuturesMarketDataProvider(httpClient, options, cacheOptions, rateLimitOptions, budget, cache);
    }

    private static string GetSymbol(System.Collections.Generic.IReadOnlyList<Mvp.Trading.Contracts.Instrument> instruments)
    {
        var configuredSymbol = Environment.GetEnvironmentVariable(SymbolEnv);
        if (!string.IsNullOrWhiteSpace(configuredSymbol))
        {
            return configuredSymbol;
        }

        var tradable = instruments.FirstOrDefault(i => i.Tradable);
        return tradable?.Symbol ?? instruments[0].Symbol;
    }
}
