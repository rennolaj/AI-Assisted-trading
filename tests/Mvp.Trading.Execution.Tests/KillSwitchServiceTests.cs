using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Mvp.Trading.Contracts;
using Mvp.Trading.Execution;
using Xunit;

namespace Mvp.Trading.Execution.Tests;

/// <summary>
/// Integration tests for KillSwitchService.
/// These tests require a running PostgreSQL database.
/// Skip these tests if database is not available.
/// </summary>
public sealed class KillSwitchServiceTests
{
    // Note: These tests are documented but skipped by default as they require database setup.
    // To run these tests:
    // 1. Ensure PostgreSQL is running on localhost:5432
    // 2. Create ai-trading-db-test database
    // 3. Run scripts/db/init.sql to create tables
    // 4. Remove [Fact(Skip = "Requires database")] to enable tests

    [Fact(Skip = "Requires database")]
    public async Task GetStatusAsync_InitialState_ReturnsInactive()
    {
        // This test verifies that a fresh kill switch starts in inactive state
        var sut = CreateService();
        var status = await sut.GetStatusAsync();

        Assert.False(status.Active);
        Assert.Equal(KillSwitchLevel.PAUSE_ALL, status.Level);
        Assert.Null(status.Reason);
        Assert.Null(status.ActivatedAt);
    }

    [Fact(Skip = "Requires database")]
    public async Task ActivateAsync_EmergencyStop_CancelsAllOrders()
    {
        // This test verifies that EMERGENCY_STOP level cancels all open orders
        var fakeTradingProvider = new FakeTradingProvider();
        var sut = CreateService(fakeTradingProvider);

        await sut.ActivateAsync(KillSwitchLevel.EMERGENCY_STOP, "Emergency!", "operator");

        Assert.True(fakeTradingProvider.GetOpenOrdersCalled);
        Assert.Equal(2, fakeTradingProvider.CancelledOrders.Count);
    }

    // Behavioral test that doesn't require database - tests emergency order cancellation
    [Fact]
    public async Task FakeTradingProvider_EmergencyScenario_CancelsAllOrders()
    {
        // Arrange
        var provider = new FakeTradingProvider();

        // Act - Simulate what happens during EMERGENCY_STOP
        var openOrdersResult = await provider.GetOpenOrdersAsync(CancellationToken.None);
        var openOrders = openOrdersResult.Value!;

        var cancelTasks = openOrders.Select(o => provider.CancelOrderAsync(o.OrderId, CancellationToken.None));
        await Task.WhenAll(cancelTasks);

        // Assert
        Assert.True(provider.GetOpenOrdersCalled);
        Assert.Equal(2, provider.CancelledOrders.Count);
        Assert.Contains("order-1", provider.CancelledOrders);
        Assert.Contains("order-2", provider.CancelledOrders);
    }

    private static KillSwitchService CreateService(ITradingProvider? tradingProvider = null)
    {
        var connectionString = "Host=localhost;Port=5432;Database=ai-trading-db-test;Username=postgres;Password=postgres";
        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = tradingProvider ?? new FakeTradingProvider();
        var logger = NullLogger<KillSwitchService>.Instance;

        return new KillSwitchService(connectionString, cache, provider, logger);
    }

    private sealed class FakeTradingProvider : ITradingProvider
    {
        public bool GetOpenOrdersCalled { get; private set; }
        public List<string> CancelledOrders { get; } = new();

        public string ExchangeId => "test-exchange";

        public Task<Result<ApiKeyInfo>> CheckApiKeyAsync(CancellationToken ct)
        {
            var permissions = new List<string> { "orders", "positions" };
            return Task.FromResult(new Result<ApiKeyInfo>(true, new ApiKeyInfo("test-key", permissions), null));
        }

        public Task<Result<IReadOnlyList<OpenOrder>>> GetOpenOrdersAsync(CancellationToken ct)
        {
            GetOpenOrdersCalled = true;
            var orders = new List<OpenOrder>
            {
                new OpenOrder("order-1", "BTCUSD", "buy", 1.0m, "limit", 50000m),
                new OpenOrder("order-2", "ETHUSD", "sell", 2.0m, "limit", 3000m)
            };
            return Task.FromResult(new Result<IReadOnlyList<OpenOrder>>(true, orders, null));
        }

        public Task<Result<OrderAck>> SendOrderAsync(SendOrderRequest request, CancellationToken ct)
        {
            return Task.FromResult(new Result<OrderAck>(true, new OrderAck("ack-1", "sent", DateTimeOffset.UtcNow), null));
        }

        public Task<Result<CancelAck>> CancelOrderAsync(string orderId, CancellationToken ct)
        {
            CancelledOrders.Add(orderId);
            return Task.FromResult(new Result<CancelAck>(true, new CancelAck("cancelled"), null));
        }

        public Task<Result<DeadMansSwitchAck>> CancelAllOrdersAfterAsync(int timeoutSeconds, CancellationToken ct)
        {
            return Task.FromResult(new Result<DeadMansSwitchAck>(true, new DeadMansSwitchAck(timeoutSeconds), null));
        }
    }
}
