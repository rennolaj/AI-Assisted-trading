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
    public async Task GetStatusAsync_SeededInactiveRow_ReturnsInactive()
    {
        // Requires the kill_switch row seeded by scripts/db/init.sql
        // (active=false, PAUSE_ALL). Without that row the service fails
        // CLOSED — see GetStatusAsync_MissingStateRow_ReturnsFailClosed.
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

    [Fact(Skip = "Requires database")]
    public async Task GetStatusAsync_MissingStateRow_ReturnsFailClosed()
    {
        // Fail-closed behavior: a reachable database WITHOUT a seeded kill_switch
        // row must return an active EMERGENCY_STOP status (never fail-open).
        var sut = CreateService();
        var status = await sut.GetStatusAsync();

        Assert.True(status.Active);
        Assert.Equal(KillSwitchLevel.EMERGENCY_STOP, status.Level);
        Assert.NotNull(status.Reason);
    }

    [Fact]
    public async Task GetStatusAsync_DatabaseUnreachable_ReturnsFailClosedEmergencyStop()
    {
        var sut = CreateService(connectionString: UnreachableConnectionString);

        var status = await sut.GetStatusAsync();

        Assert.True(status.Active);
        Assert.Equal(KillSwitchLevel.EMERGENCY_STOP, status.Level);
        Assert.NotNull(status.Reason);
        Assert.NotNull(status.ActivatedAt);
    }

    [Fact]
    public async Task IsActiveAsync_DatabaseUnreachable_ReturnsTrue()
    {
        var sut = CreateService(connectionString: UnreachableConnectionString);

        var active = await sut.IsActiveAsync();

        Assert.True(active);
    }

    [Fact]
    public async Task GetStatusAsync_FailClosedStatus_IsNotCached()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = CreateService(connectionString: UnreachableConnectionString, cache: cache);

        var status = await sut.GetStatusAsync();

        Assert.True(status.Active);
        Assert.False(cache.TryGetValue(CacheKey, out _));
    }

    [Fact]
    public async Task GetStatusAsync_CachedStatus_ReturnedWithoutDatabase()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var cached = new KillSwitchStatus(false, KillSwitchLevel.PAUSE_ALL, null, null);
        cache.Set(CacheKey, cached, TimeSpan.FromSeconds(5));
        var sut = CreateService(connectionString: UnreachableConnectionString, cache: cache);

        var status = await sut.GetStatusAsync();

        Assert.Same(cached, status);
        Assert.False(status.Active);
    }

    [Fact]
    public async Task GetStatusAsync_CancelledToken_ThrowsOperationCanceled()
    {
        var sut = CreateService(connectionString: UnreachableConnectionString);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Caller-initiated cancellation must propagate, never be converted
        // into a fail-closed status.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.GetStatusAsync(cts.Token));
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

    private const string CacheKey = KillSwitchService.CacheKey;

    // Port 1 refuses connections immediately; Timeout=1 bounds the worst case.
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1";

    private static KillSwitchService CreateService(
        ITradingProvider? tradingProvider = null,
        string? connectionString = null,
        IMemoryCache? cache = null)
    {
        connectionString ??= "Host=localhost;Port=5432;Database=ai-trading-db-test;Username=postgres;Password=postgres";
        cache ??= new MemoryCache(new MemoryCacheOptions());
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
