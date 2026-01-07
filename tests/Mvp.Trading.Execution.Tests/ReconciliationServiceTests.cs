using Xunit;
using Mvp.Trading.Contracts;
using Mvp.Trading.Execution;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mvp.Trading.Execution.Tests;

/// <summary>
/// Unit tests for ReconciliationService.
/// </summary>
public sealed class ReconciliationServiceTests
{
    [Fact]
    public async Task ReconcileAsync_NoActiveExecutions_ReturnsSuccessWithZeroChecked()
    {
        // Arrange
        var intentStore = new FakeExecutionIntentStore(Array.Empty<ExecutionIntent>());
        var receiptStore = new FakeOrderReceiptStore();
        var tradingProvider = new FakeTradingProvider(Array.Empty<OpenOrder>());
        var reconciliationStore = new FakeReconciliationStore();
        
        var sut = new ReconciliationService(
            intentStore,
            receiptStore,
            tradingProvider,
            reconciliationStore,
            NullLogger<ReconciliationService>.Instance
        );

        // Act
        var result = await sut.ReconcileAsync();

        // Assert
        Assert.True(result.Ok);
        Assert.NotNull(result.Value);
        Assert.Equal(0, result.Value!.ExecutionsChecked);
        Assert.Equal(0, result.Value.DiscrepanciesFound);
        Assert.Empty(result.Value.Discrepancies);
    }

    [Fact]
    public async Task ReconcileAsync_AllOrdersMatch_ReturnsNoDiscrepancies()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var orderId = "order-123";
        var intent = new ExecutionIntent(executionId, Guid.NewGuid(), "KRAKEN_DEMO", "PLACED", DateTimeOffset.UtcNow);
        var receipt = new OrderReceipt("client-order-1", orderId, "PLACED", 1.0m, 50000m);
        var openOrder = new OpenOrder(orderId, "BTCUSD.P", "LONG", 1.0m, "LIMIT", 50000m);

        var intentStore = new FakeExecutionIntentStore(new[] { intent });
        var receiptStore = new FakeOrderReceiptStore();
        receiptStore.AddReceipts(executionId, new[] { receipt });
        var tradingProvider = new FakeTradingProvider(new[] { openOrder });
        var reconciliationStore = new FakeReconciliationStore();
        
        var sut = new ReconciliationService(
            intentStore,
            receiptStore,
            tradingProvider,
            reconciliationStore,
            NullLogger<ReconciliationService>.Instance
        );

        // Act
        var result = await sut.ReconcileAsync();

        // Assert
        Assert.True(result.Ok);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value!.ExecutionsChecked);
        Assert.Equal(0, result.Value.DiscrepanciesFound);
        Assert.Empty(result.Value.Discrepancies);
    }

    [Fact]
    public async Task ReconcileAsync_OrderMissingOnExchange_DetectsMissingDiscrepancy()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var orderId = "order-123";
        var intent = new ExecutionIntent(executionId, Guid.NewGuid(), "KRAKEN_DEMO", "PLACED", DateTimeOffset.UtcNow);
        var receipt = new OrderReceipt("client-order-1", orderId, "PLACED", 1.0m, 50000m);

        var intentStore = new FakeExecutionIntentStore(new[] { intent });
        var receiptStore = new FakeOrderReceiptStore();
        receiptStore.AddReceipts(executionId, new[] { receipt });
        var tradingProvider = new FakeTradingProvider(Array.Empty<OpenOrder>()); // No orders on exchange
        var reconciliationStore = new FakeReconciliationStore();
        
        var sut = new ReconciliationService(
            intentStore,
            receiptStore,
            tradingProvider,
            reconciliationStore,
            NullLogger<ReconciliationService>.Instance
        );

        // Act
        var result = await sut.ReconcileAsync();

        // Assert
        Assert.True(result.Ok);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value!.ExecutionsChecked);
        Assert.Equal(1, result.Value.DiscrepanciesFound);
        Assert.Single(result.Value.Discrepancies);

        var discrepancy = result.Value.Discrepancies[0];
        Assert.Equal(executionId, discrepancy.ExecutionId);
        Assert.Equal(ReconciliationDiscrepancyType.MISSING_ON_EXCHANGE, discrepancy.Type);
        Assert.Contains(orderId, discrepancy.Details);
    }

    [Fact]
    public async Task ReconcileAsync_OrphanedOrderOnExchange_DetectsOrphanedDiscrepancy()
    {
        // Arrange
        var orphanedOrderId = "orphan-order-456";
        var orphanedOrder = new OpenOrder(orphanedOrderId, "BTCUSD.P", "LONG", 1.0m, "LIMIT", 50000m);

        var intentStore = new FakeExecutionIntentStore(Array.Empty<ExecutionIntent>());
        var receiptStore = new FakeOrderReceiptStore();
        var tradingProvider = new FakeTradingProvider(new[] { orphanedOrder });
        var reconciliationStore = new FakeReconciliationStore();
        
        var sut = new ReconciliationService(
            intentStore,
            receiptStore,
            tradingProvider,
            reconciliationStore,
            NullLogger<ReconciliationService>.Instance
        );

        // Act
        var result = await sut.ReconcileAsync();

        // Assert
        Assert.True(result.Ok);
        Assert.NotNull(result.Value);
        Assert.Equal(0, result.Value!.ExecutionsChecked);
        Assert.Equal(1, result.Value.DiscrepanciesFound);
        Assert.Single(result.Value.Discrepancies);

        var discrepancy = result.Value.Discrepancies[0];
        Assert.Null(discrepancy.ExecutionId);
        Assert.Equal(ReconciliationDiscrepancyType.ORPHANED_ON_EXCHANGE, discrepancy.Type);
        Assert.Contains(orphanedOrderId, discrepancy.Details);
    }

    // Fake implementations for testing
    private sealed class FakeExecutionIntentStore : IExecutionIntentStore
    {
        private readonly IReadOnlyList<ExecutionIntent> _intents;

        public FakeExecutionIntentStore(IReadOnlyList<ExecutionIntent> intents)
        {
            _intents = intents;
        }

        public Task SaveAsync(Guid executionId, Guid planId, string mode, string status, DateTimeOffset createdAtUtc, CancellationToken ct)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ExecutionIntent>> GetActiveAsync(CancellationToken ct = default)
            => Task.FromResult(_intents);
    }

    private sealed class FakeOrderReceiptStore : IOrderReceiptStore
    {
        private readonly Dictionary<Guid, List<OrderReceipt>> _receipts = new();

        public void AddReceipts(Guid executionId, IReadOnlyList<OrderReceipt> receipts)
        {
            _receipts[executionId] = receipts.ToList();
        }

        public Task SaveAsync(Guid executionId, string orderKind, OrderReceipt receipt, CancellationToken ct)
            => Task.CompletedTask;

        public Task<IReadOnlyList<OrderReceipt>> GetByExecutionIdAsync(Guid executionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OrderReceipt>>(_receipts.GetValueOrDefault(executionId, new List<OrderReceipt>()));
    }

    private sealed class FakeTradingProvider : ITradingProvider
    {
        private readonly IReadOnlyList<OpenOrder> _openOrders;

        public FakeTradingProvider(IReadOnlyList<OpenOrder> openOrders)
        {
            _openOrders = openOrders;
        }

        public string ExchangeId => "test-exchange";

        public Task<Result<ApiKeyInfo>> CheckApiKeyAsync(CancellationToken ct)
            => Task.FromResult(new Result<ApiKeyInfo>(true, new ApiKeyInfo("test", Array.Empty<string>()), null));

        public Task<Result<OrderAck>> SendOrderAsync(SendOrderRequest request, CancellationToken ct)
            => Task.FromResult(new Result<OrderAck>(true, new OrderAck("order-1", "PLACED", DateTimeOffset.UtcNow), null));

        public Task<Result<IReadOnlyList<OpenOrder>>> GetOpenOrdersAsync(CancellationToken ct)
            => Task.FromResult(new Result<IReadOnlyList<OpenOrder>>(true, _openOrders, null));

        public Task<Result<CancelAck>> CancelOrderAsync(string orderId, CancellationToken ct)
            => Task.FromResult(new Result<CancelAck>(true, new CancelAck("ok"), null));

        public Task<Result<CancelAck>> CancelAllOrdersAsync(CancellationToken ct)
            => Task.FromResult(new Result<CancelAck>(true, new CancelAck("ok"), null));

        public Task<Result<DeadMansSwitchAck>> CancelAllOrdersAfterAsync(int timeoutSeconds, CancellationToken ct)
            => Task.FromResult(new Result<DeadMansSwitchAck>(true, new DeadMansSwitchAck(timeoutSeconds), null));
    }

    private sealed class FakeReconciliationStore : IReconciliationStore
    {
        public Task SaveReconciliationAsync(int executionsChecked, IReadOnlyList<ReconciliationDiscrepancy> discrepancies, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ReconciliationDiscrepancy>> GetUnresolvedDiscrepanciesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReconciliationDiscrepancy>>(Array.Empty<ReconciliationDiscrepancy>());

        public Task MarkDiscrepancyResolvedAsync(Guid discrepancyId, string resolutionNotes, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
