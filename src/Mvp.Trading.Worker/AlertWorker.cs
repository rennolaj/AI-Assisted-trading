using System.Text.Json;
using Mvp.Trading.Contracts;
using Mvp.Trading.Elliott;
using Mvp.Trading.Indicators;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Mvp.Trading.Worker;

/// <summary>
/// Background worker that dequeues alerts from Redis and logs the normalized payload.
/// </summary>
public sealed class AlertWorker : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IAlertProcessingStore _processingStore;
    private readonly WorkerOptions _options;
    private readonly ILogger<AlertWorker> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IndicatorEngine _indicatorEngine;
    private readonly IIndicatorSnapshotStore _snapshotStore;
    private readonly SymbolMapper _symbolMapper;
    private readonly IElliottEngine _elliottEngine;
    private readonly IElliottCandidatesStore _elliottStore;
    private readonly ElliottRunConfig _elliottConfig;

    public AlertWorker(
        IConnectionMultiplexer redis,
        IOptions<WorkerOptions> options,
        IAlertProcessingStore processingStore,
        IndicatorEngine indicatorEngine,
        IIndicatorSnapshotStore snapshotStore,
        IElliottEngine elliottEngine,
        IElliottCandidatesStore elliottStore,
        ElliottRunConfig elliottConfig,
        SymbolMapper symbolMapper,
        ILogger<AlertWorker> logger)
    {
        _redis = redis;
        _options = options.Value;
        _processingStore = processingStore;
        _indicatorEngine = indicatorEngine;
        _snapshotStore = snapshotStore;
        _elliottEngine = elliottEngine;
        _elliottStore = elliottStore;
        _elliottConfig = elliottConfig;
        _symbolMapper = symbolMapper;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker started. Queue={QueueKey}", _options.AlertQueueKey);

        while (!stoppingToken.IsCancellationRequested)
        {
            var payload = await DequeueAsync(stoppingToken);
            if (payload.IsNullOrEmpty)
            {
                await Task.Delay(_options.PollIntervalMs, stoppingToken);
                continue;
            }

            try
            {
                var alert = JsonSerializer.Deserialize<AlertEvent>(payload.ToString(), _jsonOptions);
                if (alert is null)
                {
                    _logger.LogWarning("Dequeued payload was null after deserialization.");
                    continue;
                }

                await _processingStore.UpsertAsync(alert, "processing", null, stoppingToken);

                try
                {
                    _logger.LogInformation(
                        "Processing alert {AlertId} ({Symbol}/{Interval})",
                        alert.AlertId,
                        alert.Tv.Ticker,
                        alert.Tv.Interval);

                    var symbol = string.IsNullOrWhiteSpace(alert.Intent.SymbolHint)
                        ? alert.Tv.Ticker
                        : alert.Intent.SymbolHint;
                    symbol = _symbolMapper.Resolve(alert.Tv.Exchange, symbol);
                    var input = new IndicatorInput(
                        alert.AlertId,
                        Guid.NewGuid(),
                        symbol,
                        alert.Intent.DirectionHint,
                        DateTimeOffset.UtcNow);

                    var snapshotResult = await _indicatorEngine.ComputeAsync(input, stoppingToken);
                    if (!snapshotResult.Ok || snapshotResult.Value is null)
                    {
                        var errorMessage = snapshotResult.Error?.Message ?? "Indicator snapshot computation failed.";
                        throw new InvalidOperationException(errorMessage);
                    }

                    await _snapshotStore.UpsertAsync(snapshotResult.Value, stoppingToken);

                    var elliottCandidates = await _elliottEngine.GenerateCandidatesAsync(
                        symbol,
                        _elliottConfig.BaseTimeframe,
                        _elliottConfig.Parameters,
                        input.EvaluationTimeUtc,
                        stoppingToken);

                    await _elliottStore.UpsertAsync(
                        alert.AlertId,
                        DateTimeOffset.UtcNow,
                        input.EvaluationTimeUtc,
                        symbol,
                        _elliottConfig.BaseTimeframe,
                        _elliottConfig.Parameters,
                        elliottCandidates,
                        stoppingToken);

                    await _processingStore.UpsertAsync(alert, "succeeded", null, stoppingToken);
                }
                catch (Exception ex)
                {
                    await _processingStore.UpsertAsync(alert, "failed", ex.Message, stoppingToken);
                    _logger.LogError(ex, "Processing failed for alert {AlertId}.", alert.AlertId);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize alert payload from Redis.");
            }
        }
    }

    private async Task<RedisValue> DequeueAsync(CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        return await db.ListLeftPopAsync(_options.AlertQueueKey).ConfigureAwait(false);
    }
}
