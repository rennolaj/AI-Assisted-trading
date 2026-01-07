using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mvp.Trading.Contracts.Telemetry;
using Mvp.Trading.Execution;

namespace Mvp.Trading.Worker;

/// <summary>
/// Background service that periodically reconciles internal state with exchange state.
/// </summary>
public sealed class ReconciliationWorker : BackgroundService
{
    private readonly IReconciliationService _reconciliationService;
    private readonly IKillSwitchService _killSwitchService;
    private readonly IMetricsService _metrics;
    private readonly ReconciliationOptions _options;
    private readonly ILogger<ReconciliationWorker> _logger;

    public ReconciliationWorker(
        IReconciliationService reconciliationService,
        IKillSwitchService killSwitchService,
        IMetricsService metrics,
        IOptions<ReconciliationOptions> options,
        ILogger<ReconciliationWorker> logger)
    {
        _reconciliationService = reconciliationService;
        _killSwitchService = killSwitchService;
        _metrics = metrics;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ReconciliationWorker started with interval {Interval}s", _options.PollingIntervalSeconds);

        // Wait a bit before first run to let system stabilize
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

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
                    _logger.LogWarning("ReconciliationWorker paused: Kill switch active at level {Level}", killSwitchStatus.Level);
                    await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
                    continue;
                }

                var result = await _reconciliationService.ReconcileAsync(stoppingToken);
                
                if (!result.Ok)
                {
                    _logger.LogError("Reconciliation failed: {Error}", result.Error?.Message);
                    _metrics.RecordError("ReconciliationWorker", "RECONCILIATION_FAILED");
                }
                else if (result.Value is not null)
                {
                    // Track discrepancies if any found
                    var discrepancyCount = result.Value.Discrepancies?.Count ?? 0;
                    if (discrepancyCount > 0)
                    {
                        _logger.LogWarning("Reconciliation found {Count} discrepancies", discrepancyCount);
                        _metrics.SetReconciliationDiscrepanciesGauge(discrepancyCount);
                    }
                    else
                    {
                        _metrics.SetReconciliationDiscrepanciesGauge(0);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reconciliation worker encountered an error");
                _metrics.RecordError("ReconciliationWorker", ex.GetType().Name);
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("ReconciliationWorker stopped");
    }
}
