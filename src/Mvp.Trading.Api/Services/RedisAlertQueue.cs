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
        var db = _redis.GetDatabase();
        var payload = JsonSerializer.Serialize(alert, _jsonOptions);
        await db.ListRightPushAsync(_options.AlertQueueKey, payload).ConfigureAwait(false);
    }
}
