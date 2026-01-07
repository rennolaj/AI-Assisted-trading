using Microsoft.Extensions.Logging;
using Mvp.Trading.Contracts;
using System.Text.Json;

namespace Mvp.Trading.Execution;

/// <summary>
/// Reconciles internal execution state against exchange open orders.
/// </summary>
public sealed class ReconciliationService : IReconciliationService
{
    private readonly IExecutionIntentStore _intentStore;
    private readonly IOrderReceiptStore _receiptStore;
    private readonly ITradingProvider _tradingProvider;
    private readonly IReconciliationStore _reconciliationStore;
    private readonly ILogger<ReconciliationService> _logger;

    public ReconciliationService(
        IExecutionIntentStore intentStore,
        IOrderReceiptStore receiptStore,
        ITradingProvider tradingProvider,
        IReconciliationStore reconciliationStore,
        ILogger<ReconciliationService> logger)
    {
        _intentStore = intentStore;
        _receiptStore = receiptStore;
        _tradingProvider = tradingProvider;
        _reconciliationStore = reconciliationStore;
        _logger = logger;
    }

    public async Task<Result<ReconciliationResult>> ReconcileAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting reconciliation loop");

        try
        {
            // 1. Get all active execution intents (not completed/failed)
            var activeIntents = await _intentStore.GetActiveAsync(ct);

            // 2. Get open orders from exchange
            var openOrdersResult = await _tradingProvider.GetOpenOrdersAsync(ct);
            if (!openOrdersResult.Ok || openOrdersResult.Value == null)
            {
                _logger.LogError("Failed to fetch open orders from exchange: {Error}", openOrdersResult.Error?.Message);
                return new Result<ReconciliationResult>(false, default, openOrdersResult.Error);
            }

            // Build dictionary for fast lookup by exchange order ID
            var exchangeOrders = openOrdersResult.Value
                .Where(o => !string.IsNullOrEmpty(o.OrderId))
                .ToDictionary(o => o.OrderId, o => o);

            var discrepancies = new List<ReconciliationDiscrepancy>();

            // 3. Compare internal state vs exchange state
            foreach (var intent in activeIntents)
            {
                await CheckExecutionIntentAsync(intent, exchangeOrders, discrepancies, ct);
            }

            // 4. Check for orphaned orders (on exchange but not in our system)
            // This includes orders on exchange when we have no active intents
            foreach (var orphan in exchangeOrders.Values)
            {
                var discrepancy = new ReconciliationDiscrepancy(
                    null, // No execution_id since it's orphaned
                    ReconciliationDiscrepancyType.ORPHANED_ON_EXCHANGE,
                    "NOT_FOUND",
                    JsonSerializer.Serialize(new { orphan.OrderId, orphan.Symbol, orphan.Side, orphan.Type }),
                    $"Order {orphan.OrderId} found on exchange but not in internal state"
                );
                discrepancies.Add(discrepancy);
                
                _logger.LogError("Orphaned order found on exchange: {OrderId} ({Symbol})", orphan.OrderId, orphan.Symbol);
            }

            // 5. Persist reconciliation results
            await _reconciliationStore.SaveReconciliationAsync(activeIntents.Count, discrepancies, ct);

            var result = new ReconciliationResult(
                activeIntents.Count,
                discrepancies.Count,
                discrepancies
            );

            if (discrepancies.Count > 0)
            {
                _logger.LogWarning("Reconciliation complete: checked {Count} executions, found {Discrepancies} discrepancies",
                    result.ExecutionsChecked, result.DiscrepanciesFound);
            }
            else
            {
                _logger.LogInformation("Reconciliation complete: checked {Count} executions, no discrepancies found",
                    result.ExecutionsChecked);
            }

            return new Result<ReconciliationResult>(true, result, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reconciliation failed with exception");
            return new Result<ReconciliationResult>(false, default, new Error("RECONCILIATION_ERROR", ex.Message, null));
        }
    }

    private async Task CheckExecutionIntentAsync(
        ExecutionIntent intent,
        Dictionary<string, OpenOrder> exchangeOrders,
        List<ReconciliationDiscrepancy> discrepancies,
        CancellationToken ct)
    {
        var receipts = await _receiptStore.GetByExecutionIdAsync(intent.ExecutionId, ct);

        foreach (var receipt in receipts)
        {
            // Skip receipts without exchange order ID (not yet placed)
            if (string.IsNullOrEmpty(receipt.ExchangeOrderId))
            {
                continue;
            }

            // Check if order exists on exchange
            if (!exchangeOrders.ContainsKey(receipt.ExchangeOrderId))
            {
                // Order in our system but not on exchange
                var discrepancy = new ReconciliationDiscrepancy(
                    intent.ExecutionId,
                    ReconciliationDiscrepancyType.MISSING_ON_EXCHANGE,
                    JsonSerializer.Serialize(new { receipt.ClientOrderId, receipt.ExchangeOrderId, receipt.Status }),
                    "NOT_FOUND",
                    $"Order {receipt.ExchangeOrderId} placed internally but not found on exchange"
                );
                discrepancies.Add(discrepancy);

                _logger.LogError("Reconciliation discrepancy: Order {OrderId} missing on exchange", receipt.ExchangeOrderId);
            }
            else
            {
                // Order exists, remove from dictionary (to find orphans later)
                exchangeOrders.Remove(receipt.ExchangeOrderId);
            }
        }
    }
}
