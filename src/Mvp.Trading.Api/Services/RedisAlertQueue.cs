using System.Text.Json;
using Mvp.Trading.Contracts;
using StackExchange.Redis;
using Microsoft.Extensions.Options;

namespace Mvp.Trading.Api.Services;

/// <summary>
/// Redis-backed queue for alert enqueueing.
/// </summary>
public sealed class RedisAlertQueue : IAlertQueue
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;

    public RedisAlertQueue(IConnectionMultiplexer redis, IOptions<RedisOptions> options)
    {
        _redis = redis;
        _options = options.Value;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task EnqueueAsync(AlertEvent alert, CancellationToken ct)
    {
        // StackExchange.Redis async operations do not accept a CancellationToken;
        // honor cancellation before the push. The push itself is deliberately not
        // WaitAsync(ct)-wrapped: abandoning an in-flight destructive write would
        // leave it ambiguous whether the alert was enqueued.
        ct.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        var payload = JsonSerializer.Serialize(alert, _jsonOptions);
        await db.ListRightPushAsync(_options.AlertQueueKey, payload).ConfigureAwait(false);
    }
}
