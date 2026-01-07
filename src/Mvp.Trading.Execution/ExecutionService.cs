using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Mvp.Trading.Contracts;
using Mvp.Trading.Contracts.Telemetry;
using Mvp.Trading.Risk;

namespace Mvp.Trading.Execution;

/// <summary>
/// Executes trade plans using configured execution settings.
/// </summary>
public sealed class ExecutionService : IExecutionService
{
    private const string ServiceName = "execution-service";
    private const string KrakenExchangeId = "kraken-futures";
    private readonly IExecutionSettingsProvider _settingsProvider;
    private readonly ITradePlanStore _planStore;
    private readonly IExecutionIntentStore _intentStore;
    private readonly IOrderReceiptStore _receiptStore;
    private readonly IExecutionHeartbeatStore _heartbeatStore;
    private readonly ITradingProvider _tradingProvider;
    private readonly IMetricsService _metrics;

    public ExecutionService(
        IExecutionSettingsProvider settingsProvider,
        ITradePlanStore planStore,
        IExecutionIntentStore intentStore,
        IOrderReceiptStore receiptStore,
        IExecutionHeartbeatStore heartbeatStore,
        ITradingProvider tradingProvider,
        IMetricsService metrics)
    {
        _settingsProvider = settingsProvider;
        _planStore = planStore;
        _intentStore = intentStore;
        _receiptStore = receiptStore;
        _heartbeatStore = heartbeatStore;
        _tradingProvider = tradingProvider;
        _metrics = metrics;
    }

    public async Task<Result<ExecutionReceipt>> ExecuteAsync(ExecutionRequest request, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var settings = _settingsProvider.GetSettings();
        var heartbeat = await _heartbeatStore.UpsertAndCheckAsync(ServiceName, settings.StaleThresholdSeconds, ct);
        
        if (heartbeat.IsStale)
        {
            _metrics.RecordExecutionOutcome("rejected_heartbeat");
            _metrics.RecordExecutionDuration(stopwatch.Elapsed, "total");
            return Fail("EXECUTION_HEARTBEAT_STALE", "Execution heartbeat is stale; refusing to execute.");
        }

        await _planStore.SaveAsync(request.AlertId, request.Plan, ct);

        var executionId = ComputeExecutionId(request.Plan);
        var createdAt = DateTimeOffset.UtcNow;

        var mode = NormalizeMode(settings.Mode);
        if (IsSimulated(mode))
        {
            var entryReceipt = new OrderReceipt(
                $"{executionId}-ENTRY",
                null,
                "FILLED",
                request.Plan.Quantity,
                request.Plan.EntryLimitPrice);

            var stopReceipt = new OrderReceipt(
                $"{executionId}-STOP",
                null,
                "PLACED",
                request.Plan.Quantity,
                request.Plan.StopLossPrice);

            await _intentStore.SaveAsync(executionId, request.Plan.PlanId, mode, "SIMULATED", createdAt, ct);
            await _receiptStore.SaveAsync(executionId, "ENTRY", entryReceipt, ct);
            await _receiptStore.SaveAsync(executionId, "STOP", stopReceipt, ct);
            await SaveTakeProfitReceiptsAsync(executionId, request.Plan, ct);

            var receipt = new ExecutionReceipt(
                executionId,
                request.Plan.PlanId,
                mode,
                "SIMULATED_FILLED",
                createdAt,
                entryReceipt,
                stopReceipt,
                "simulated execution");

            _metrics.RecordExecutionOutcome("filled");
            _metrics.RecordOrderPlaced(request.Plan.Side, "MARKET");
            _metrics.RecordOrderFilled(request.Plan.Symbol, request.Plan.Side, request.Plan.Quantity, request.Plan.EntryLimitPrice);
            _metrics.RecordExecutionDuration(stopwatch.Elapsed, "total");
            return new Result<ExecutionReceipt>(true, receipt, null);
        }

        if (IsKrakenMode(mode))
        {
            var result = await ExecuteKrakenAsync(request, executionId, createdAt, mode, settings, stopwatch, ct);
            _metrics.RecordExecutionDuration(stopwatch.Elapsed, "total");
            return result;
        }

        await _intentStore.SaveAsync(executionId, request.Plan.PlanId, mode, "NOT_IMPLEMENTED", createdAt, ct);
        _metrics.RecordExecutionOutcome("error");
        _metrics.RecordExecutionDuration(stopwatch.Elapsed, "total");
        return Fail("EXECUTION_MODE_UNSUPPORTED", $"Execution mode '{mode}' is not implemented yet.");
    }

    private static Guid ComputeExecutionId(TradePlan plan)
    {
        var seed = string.Join(
            "|",
            plan.PlanId.ToString(),
            plan.Symbol,
            plan.Side,
            FormatDecimal(plan.EntryLimitPrice),
            FormatDecimal(plan.StopLossPrice),
            FormatDecimal(plan.Quantity));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static string FormatDecimal(decimal value)
    {
        return value.ToString("G29", CultureInfo.InvariantCulture);
    }

    private async Task<Result<ExecutionReceipt>> ExecuteKrakenAsync(
        ExecutionRequest request,
        Guid executionId,
        DateTimeOffset createdAt,
        string mode,
        ExecutionSettings settings,
        Stopwatch stopwatch,
        CancellationToken ct)
    {
        if (!string.Equals(_tradingProvider.ExchangeId, KrakenExchangeId, StringComparison.OrdinalIgnoreCase))
        {
            await _intentStore.SaveAsync(executionId, request.Plan.PlanId, mode, "PROVIDER_MISMATCH", createdAt, ct);
            _metrics.RecordExecutionOutcome("error");
            return Fail("EXECUTION_PROVIDER_MISMATCH", "Configured trading provider does not match Kraken Futures.");
        }

        if (!TryMapSides(request.Plan.Side, out var entrySide, out var exitSide))
        {
            await _intentStore.SaveAsync(executionId, request.Plan.PlanId, mode, "INVALID_SIDE", createdAt, ct);
            _metrics.RecordExecutionOutcome("error");
            return Fail("EXECUTION_INVALID_SIDE", $"Unsupported trade side '{request.Plan.Side}'.");
        }

        await _intentStore.SaveAsync(executionId, request.Plan.PlanId, mode, "SUBMITTING", createdAt, ct);

        var deadman = await _tradingProvider.CancelAllOrdersAfterAsync(settings.HeartbeatIntervalSeconds, ct);
        if (!deadman.Ok || deadman.Value is null)
        {
            await _intentStore.SaveAsync(executionId, request.Plan.PlanId, mode, "DEADMAN_FAILED", createdAt, ct);
            _metrics.RecordExecutionOutcome("error");
            return Fail(
                "EXECUTION_DEADMAN_FAILED",
                deadman.Error?.Message ?? "Dead-man's switch request failed.");
        }

        var entryClientOrderId = $"{executionId}-ENTRY";
        var entryOrderType = MapEntryOrderType(request.Plan.EntryType);
        var entryRequest = new SendOrderRequest(
            request.Plan.Symbol,
            entrySide,
            request.Plan.Quantity,
            entryOrderType,
            request.Plan.EntryLimitPrice,
            null,
            null,
            entryClientOrderId);

        var entryResult = await SendWithRetriesAsync(
            () => _tradingProvider.SendOrderAsync(entryRequest, ct),
            settings.MaxOrderRetries,
            ct);

        if (!entryResult.Ok || entryResult.Value is null)
        {
            await _intentStore.SaveAsync(executionId, request.Plan.PlanId, mode, "ENTRY_FAILED", createdAt, ct);
            _metrics.RecordExecutionOutcome("error");
            return Fail(
                "EXECUTION_ENTRY_FAILED",
                entryResult.Error?.Message ?? "Entry order failed.");
        }

        var entryReceipt = new OrderReceipt(
            entryClientOrderId,
            entryResult.Value.OrderId,
            entryResult.Value.Status,
            request.Plan.Quantity,
            request.Plan.EntryLimitPrice);

        await _receiptStore.SaveAsync(executionId, "ENTRY", entryReceipt, ct);
        await _intentStore.SaveAsync(executionId, request.Plan.PlanId, mode, "ENTRY_PLACED", createdAt, ct);
        _metrics.RecordOrderPlaced(request.Plan.Side, entryOrderType.ToUpperInvariant());
        
        if (entryResult.Value.Status == "FILLED")
        {
            _metrics.RecordOrderFilled(request.Plan.Symbol, request.Plan.Side, request.Plan.Quantity, request.Plan.EntryLimitPrice);
        }

        var stopClientOrderId = $"{executionId}-STOP";
        var stopRequest = new SendOrderRequest(
            request.Plan.Symbol,
            exitSide,
            request.Plan.Quantity,
            "stp",
            null,
            request.Plan.StopLossPrice,
            null,
            stopClientOrderId);

        var stopResult = await SendWithRetriesAsync(
            () => _tradingProvider.SendOrderAsync(stopRequest, ct),
            settings.MaxOrderRetries,
            ct);

        if (!stopResult.Ok || stopResult.Value is null)
        {
            await _intentStore.SaveAsync(executionId, request.Plan.PlanId, mode, "STOP_FAILED", createdAt, ct);
            await TryCancelAsync(entryResult.Value.OrderId, ct);
            _metrics.RecordExecutionOutcome("error");
            return Fail(
                "EXECUTION_STOP_FAILED",
                stopResult.Error?.Message ?? "Stop order failed.");
        }

        var stopReceipt = new OrderReceipt(
            stopClientOrderId,
            stopResult.Value.OrderId,
            stopResult.Value.Status,
            request.Plan.Quantity,
            request.Plan.StopLossPrice);

        await _receiptStore.SaveAsync(executionId, "STOP", stopReceipt, ct);
        _metrics.RecordOrderPlaced(request.Plan.Side, "STP");

        var tpErrors = await PlaceTakeProfitOrdersAsync(executionId, request.Plan, exitSide, settings, ct);
        var status = tpErrors.Count == 0 ? "ORDERS_PLACED" : "ORDERS_PLACED_TP_PARTIAL";
        var note = tpErrors.Count == 0
            ? "kraken demo execution"
            : $"kraken demo execution with {tpErrors.Count} take-profit failures";

        await _intentStore.SaveAsync(executionId, request.Plan.PlanId, mode, status, createdAt, ct);

        // Record successful execution outcome
        var outcome = entryResult.Value.Status == "FILLED" ? "filled" : "placed";
        _metrics.RecordExecutionOutcome(outcome);

        var receipt = new ExecutionReceipt(
            executionId,
            request.Plan.PlanId,
            mode,
            status,
            createdAt,
            entryReceipt,
            stopReceipt,
            note);

        return new Result<ExecutionReceipt>(true, receipt, null);
    }

    private async Task SaveTakeProfitReceiptsAsync(Guid executionId, TradePlan plan, CancellationToken ct)
    {
        if (plan.TakeProfitTargets is null || plan.TakeProfitTargets.Count == 0)
        {
            return;
        }

        for (var i = 0; i < plan.TakeProfitTargets.Count; i++)
        {
            var target = plan.TakeProfitTargets[i];
            var orderKind = $"TAKE_PROFIT_{i + 1}";
            var receipt = new OrderReceipt(
                $"{executionId}-TP-{i + 1}",
                null,
                "SIMULATED",
                target.Quantity,
                target.Price);

            await _receiptStore.SaveAsync(executionId, orderKind, receipt, ct);
        }
    }

    private async Task<IReadOnlyList<string>> PlaceTakeProfitOrdersAsync(
        Guid executionId,
        TradePlan plan,
        string exitSide,
        ExecutionSettings settings,
        CancellationToken ct)
    {
        var errors = new List<string>();

        if (plan.TakeProfitTargets is null || plan.TakeProfitTargets.Count == 0)
        {
            return errors;
        }

        for (var i = 0; i < plan.TakeProfitTargets.Count; i++)
        {
            var target = plan.TakeProfitTargets[i];
            if (target.Price <= 0m || target.Quantity <= 0m)
            {
                errors.Add($"TP_{i + 1}_INVALID");
                continue;
            }

            var clientOrderId = $"{executionId}-TP-{i + 1}";
            var tpRequest = new SendOrderRequest(
                plan.Symbol,
                exitSide,
                target.Quantity,
                "take_profit",
                null,
                target.Price,
                null,
                clientOrderId);

            var tpResult = await SendWithRetriesAsync(
                () => _tradingProvider.SendOrderAsync(tpRequest, ct),
                settings.MaxOrderRetries,
                ct);

            if (!tpResult.Ok || tpResult.Value is null)
            {
                errors.Add($"TP_{i + 1}_FAILED");
                _metrics.RecordError("ExecutionService", "TP_PLACEMENT_FAILED");
                continue;
            }

            var receipt = new OrderReceipt(
                clientOrderId,
                tpResult.Value.OrderId,
                tpResult.Value.Status,
                target.Quantity,
                target.Price);

            var orderKind = $"TAKE_PROFIT_{i + 1}";
            await _receiptStore.SaveAsync(executionId, orderKind, receipt, ct);
            _metrics.RecordOrderPlaced(plan.Side, "TAKE_PROFIT");
        }

        return errors;
    }

    private async Task<Result<OrderAck>> SendWithRetriesAsync(
        Func<Task<Result<OrderAck>>> action,
        int maxRetries,
        CancellationToken ct)
    {
        var attempts = 0;
        var retries = Math.Max(0, maxRetries);

        while (true)
        {
            var result = await action();
            if (result.Ok || attempts >= retries)
            {
                return result;
            }

            attempts++;
            await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
        }
    }

    private async Task TryCancelAsync(string orderId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return;
        }

        await _tradingProvider.CancelOrderAsync(orderId, ct);
    }

    private static bool TryMapSides(string side, out string entrySide, out string exitSide)
    {
        entrySide = string.Empty;
        exitSide = string.Empty;

        if (string.IsNullOrWhiteSpace(side))
        {
            return false;
        }

        var normalized = side.Trim().ToUpperInvariant();
        if (normalized == "LONG" || normalized == "BUY")
        {
            entrySide = "buy";
            exitSide = "sell";
            return true;
        }

        if (normalized == "SHORT" || normalized == "SELL")
        {
            entrySide = "sell";
            exitSide = "buy";
            return true;
        }

        return false;
    }

    private static string MapEntryOrderType(string? entryType)
    {
        if (string.IsNullOrWhiteSpace(entryType))
        {
            return "lmt";
        }

        var normalized = entryType.Trim().ToUpperInvariant();
        if (normalized.Contains("IOC", StringComparison.Ordinal))
        {
            return "ioc";
        }

        if (normalized.Contains("MARKET", StringComparison.Ordinal) || normalized == "MKT")
        {
            return "mkt";
        }

        if (normalized.Contains("LIMIT", StringComparison.Ordinal))
        {
            return "lmt";
        }

        return "lmt";
    }

    private static bool IsSimulated(string mode)
    {
        return string.Equals(mode, "SIMULATED", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKrakenMode(string mode)
    {
        return mode.StartsWith("KRAKEN", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeMode(string? mode)
    {
        return string.IsNullOrWhiteSpace(mode) ? "SIMULATED" : mode.Trim();
    }

    private static Result<ExecutionReceipt> Fail(string code, string message)
    {
        return new Result<ExecutionReceipt>(false, null, new Error(code, message, null));
    }
}
