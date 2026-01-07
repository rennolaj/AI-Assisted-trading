using System;
using System.Net.Http;
using System.Threading.Tasks;
using Mvp.Trading.Contracts;
using Mvp.Trading.Integrations.Kraken;
using Xunit;

namespace Mvp.Trading.Execution.Tests;

/// <summary>
/// Integration tests for Kraken Futures private trading endpoints.
/// These tests require API credentials and run against Kraken Demo environment.
/// 
/// Set KRAKEN_FUTURES_TRADING_TESTS=1 to enable.
/// Set KRAKEN_FUTURES_API_KEY and KRAKEN_FUTURES_API_SECRET for authentication.
/// </summary>
public sealed class KrakenFuturesTradingIntegrationTests : IDisposable
{
    private const string EnabledEnv = "KRAKEN_FUTURES_TRADING_TESTS";
    private const string ApiKeyEnv = "KRAKEN_FUTURES_API_KEY";
    private const string ApiSecretEnv = "KRAKEN_FUTURES_API_SECRET";
    private const string BaseUrlEnv = "KRAKEN_FUTURES_BASE_URL";
    private const string AuthBaseUrlEnv = "KRAKEN_FUTURES_AUTH_BASE_URL";
    private const string TestSymbolEnv = "KRAKEN_FUTURES_TEST_SYMBOL";

    private readonly HttpClient _httpClient;

    public KrakenFuturesTradingIntegrationTests()
    {
        _httpClient = new HttpClient();
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }

    [Fact]
    public async Task CheckApiKey_WithValidCredentials_ReturnsSuccess()
    {
        if (!IsEnabled())
        {
            return;
        }

        var provider = CreateProvider();

        var result = await provider.CheckApiKeyAsync(default);

        Assert.True(result.Ok, result.Error?.Message ?? "CheckApiKey failed");
        Assert.NotNull(result.Value);
        Assert.NotNull(result.Value!.Key);
    }

    [Fact]
    public async Task SendOrder_LimitOrder_ReturnsOrderAck()
    {
        if (!IsEnabled())
        {
            return;
        }

        var provider = CreateProvider();
        var symbol = GetTestSymbol();
        
        // Place a limit order far from market price (so it won't fill immediately)
        var request = new SendOrderRequest(
            Symbol: symbol,
            Side: "buy",
            Size: 1m, // Minimum size for most futures contracts
            OrderType: "lmt",
            LimitPrice: 1.0m, // Far below market to avoid execution
            StopPrice: null,
            ProcessBeforeUtc: null,
            ClientOrderId: $"test-{Guid.NewGuid():N}"
        );

        var result = await provider.SendOrderAsync(request, default);

        Assert.True(result.Ok, result.Error?.Message ?? "SendOrder failed");
        Assert.NotNull(result.Value);
        Assert.NotNull(result.Value!.OrderId);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.OrderId));
        
        // Clean up: cancel the order
        if (!string.IsNullOrWhiteSpace(result.Value.OrderId))
        {
            await provider.CancelOrderAsync(result.Value.OrderId, default);
        }
    }

    [Fact]
    public async Task GetOpenOrders_ReturnsOrderList()
    {
        if (!IsEnabled())
        {
            return;
        }

        var provider = CreateProvider();
        
        // Get open orders (may be empty, that's OK)
        var result = await provider.GetOpenOrdersAsync(default);
        
        Assert.True(result.Ok, result.Error?.Message ?? "GetOpenOrders failed");
        Assert.NotNull(result.Value);
        // Don't assert on count - could be 0 or more depending on account state
    }

    [Fact]
    public async Task CancelOrder_WithValidOrderId_ReturnsSuccess()
    {
        if (!IsEnabled())
        {
            return;
        }

        var provider = CreateProvider();
        var symbol = GetTestSymbol();
        
        // First, place an order
        var sendRequest = new SendOrderRequest(
            Symbol: symbol,
            Side: "buy",
            Size: 1m,
            OrderType: "lmt",
            LimitPrice: 1.0m,
            StopPrice: null,
            ProcessBeforeUtc: null,
            ClientOrderId: $"test-cancel-{Guid.NewGuid():N}"
        );

        var sendResult = await provider.SendOrderAsync(sendRequest, default);
        Assert.True(sendResult.Ok, "Failed to place order");
        
        string? orderId = sendResult.Value?.OrderId;
        Assert.NotNull(orderId);

        // Cancel the order
        var cancelResult = await provider.CancelOrderAsync(orderId!, default);
        
        Assert.True(cancelResult.Ok, cancelResult.Error?.Message ?? "CancelOrder failed");
        Assert.NotNull(cancelResult.Value);
    }

    [Fact]
    public async Task CancelAllOrdersAfter_WithValidTimeout_ReturnsSuccess()
    {
        if (!IsEnabled())
        {
            return;
        }

        var provider = CreateProvider();
        
        // Set dead-man's switch to 60 seconds
        var result = await provider.CancelAllOrdersAfterAsync(60, default);
        
        Assert.True(result.Ok, result.Error?.Message ?? "CancelAllOrdersAfter failed");
        Assert.NotNull(result.Value);
        Assert.Equal(60, result.Value!.TimeoutSeconds);
    }

    [Fact]
    public async Task CancelAllOrdersAfter_DisableWithZero_ReturnsSuccess()
    {
        if (!IsEnabled())
        {
            return;
        }

        var provider = CreateProvider();
        
        // Disable dead-man's switch
        var result = await provider.CancelAllOrdersAfterAsync(0, default);
        
        Assert.True(result.Ok, result.Error?.Message ?? "CancelAllOrdersAfter failed");
        Assert.NotNull(result.Value);
    }

    private bool IsEnabled()
    {
        var enabled = Environment.GetEnvironmentVariable(EnabledEnv);
        return string.Equals(enabled, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase);
    }

    private KrakenFuturesTradingProvider CreateProvider()
    {
        var apiKey = Environment.GetEnvironmentVariable(ApiKeyEnv);
        var apiSecret = Environment.GetEnvironmentVariable(ApiSecretEnv);

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
        {
            throw new InvalidOperationException(
                $"Missing required environment variables: {ApiKeyEnv} and {ApiSecretEnv}");
        }

        var options = new KrakenFuturesOptions
        {
            ApiKey = apiKey,
            ApiSecret = apiSecret,
            BaseUrl = Environment.GetEnvironmentVariable(BaseUrlEnv) 
                ?? "https://demo-futures.kraken.com/derivatives",
            AuthBaseUrl = Environment.GetEnvironmentVariable(AuthBaseUrlEnv)
                ?? "https://demo-futures.kraken.com/derivatives",
            TimeoutSeconds = 30
        };

        return new KrakenFuturesTradingProvider(_httpClient, options);
    }

    private string GetTestSymbol()
    {
        // Default to BTC futures contract if not specified
        return Environment.GetEnvironmentVariable(TestSymbolEnv) ?? "PF_XBTUSD";
    }
}
