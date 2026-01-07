using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mvp.Trading.Contracts;
using Mvp.Trading.Contracts.Telemetry;
using Mvp.Trading.Execution;

namespace Mvp.Trading.Worker;

/// <summary>
/// Background worker that continuously validates open trades against invalidation levels.
/// </summary>
public sealed class TradeMonitorWorker : BackgroundService
{
    private readonly IMarketDataProvider _marketData;
    private readonly IOpenTradeRepository _repository;
    private readonly IKillSwitchService _killSwitchService;
    private readonly IMetricsService _metrics;
    private readonly WorkerOptions _options;
    private readonly ILogger<TradeMonitorWorker> _logger;

    public TradeMonitorWorker(
        IMarketDataProvider marketData,
        IOpenTradeRepository repository,
        IKillSwitchService killSwitchService,
        IMetricsService metrics,
        IOptions<WorkerOptions> options,
        ILogger<TradeMonitorWorker> logger)
    {
        _marketData = marketData;
        _repository = repository;
        _killSwitchService = killSwitchService;
        _metrics = metrics;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Trade monitor started for exchange {ExchangeId}.", _marketData.ExchangeId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Check kill switch status - pause on PAUSE_ALL and EMERGENCY_STOP
                var killSwitchStatus = await _killSwitchService.GetStatusAsync(stoppingToken);
                if (killSwitchStatus.Active && 
                    (killSwitchStatus.Level == KillSwitchLevel.PAUSE_ALL ||
                     killSwitchStatus.Level == KillSwitchLevel.EMERGENCY_STOP))
                {
                    _logger.LogWarning("TradeMonitorWorker paused: Kill switch active at level {Level}", killSwitchStatus.Level);
                }
                else
                {
                    await MonitorOnceAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Trade monitor iteration failed.");
                _metrics.RecordError("TradeMonitorWorker", ex.GetType().Name);
            }

            try
            {
                await Task.Delay(_options.TradeMonitorIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task MonitorOnceAsync(CancellationToken ct)
    {
        var trades = await _repository.GetOpenTradesAsync(_marketData.ExchangeId, ct);
        
        // Update active trades gauge
        _metrics.SetActiveTradesGauge(trades.Count);
        
        if (trades.Count == 0)
        {
            return;
        }

        var tickersResult = await _marketData.GetTickersAsync(ct);
        if (!tickersResult.Ok || tickersResult.Value is null)
        {
            _logger.LogWarning("Failed to fetch tickers for trade monitoring: {Error}", tickersResult.Error?.Message);
            _metrics.RecordError("TradeMonitorWorker", "TICKER_FETCH_FAILED");
            return;
        }

        var tickerMap = tickersResult.Value
            .GroupBy(t => t.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var trade in trades)
        {
            if (!tickerMap.TryGetValue(trade.Symbol, out var ticker))
            {
                _logger.LogWarning("No ticker found for trade {TradeId} symbol {Symbol}.", trade.TradeId, trade.Symbol);
                continue;
            }

            var lastPrice = ticker.Last;
            if (trade.InvalidationPrice is null)
            {
                await _repository.UpdateHeartbeatAsync(trade.TradeId, lastPrice, ct);
                continue;
            }

            var invalidated = IsInvalidated(trade.Side, lastPrice, trade.InvalidationPrice.Value, out var reason);
            if (invalidated)
            {
                await _repository.MarkInvalidatedAsync(trade.TradeId, lastPrice, reason, ct);
                _logger.LogWarning("Trade {TradeId} invalidated: {Reason}", trade.TradeId, reason);
                _metrics.RecordStopLossTriggered(trade.Symbol);
                continue;
            }

            await _repository.UpdateHeartbeatAsync(trade.TradeId, lastPrice, ct);
        }
    }

    private static bool IsInvalidated(string side, decimal lastPrice, decimal invalidationPrice, out string reason)
    {
        reason = string.Empty;

        if (side.Equals("LONG", StringComparison.OrdinalIgnoreCase))
        {
            if (lastPrice <= invalidationPrice)
            {
                reason = $"Price {lastPrice} <= invalidation {invalidationPrice}.";
                return true;
            }

            return false;
        }

        if (side.Equals("SHORT", StringComparison.OrdinalIgnoreCase))
        {
            if (lastPrice >= invalidationPrice)
            {
                reason = $"Price {lastPrice} >= invalidation {invalidationPrice}.";
                return true;
            }

            return false;
        }

        reason = "Unknown side; cannot validate invalidation.";
        return false;
    }
}
