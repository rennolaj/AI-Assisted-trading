namespace Mvp.Trading.Api.Services;

/// <summary>
/// Configuration for Redis-backed queues.
/// </summary>
public sealed class RedisOptions
{
    /// <summary>
    /// Connection string used by the Redis client.
    /// </summary>
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>
    /// Redis list key used for alert enqueueing.
    /// </summary>
    public string AlertQueueKey { get; init; } = "mvp:alerts";
}
