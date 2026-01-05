using System.Text.Json;
using Mvp.Trading.Contracts;
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

    public AlertWorker(
        IConnectionMultiplexer redis,
        IOptions<WorkerOptions> options,
        IAlertProcessingStore processingStore,
        ILogger<AlertWorker> logger)
    {
        _redis = redis;
        _options = options.Value;
        _processingStore = processingStore;
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
