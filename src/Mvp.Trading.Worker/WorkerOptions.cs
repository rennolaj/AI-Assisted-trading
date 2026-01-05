namespace Mvp.Trading.Worker;

/// <summary>
/// Configuration for the alert processing worker.
/// </summary>
public sealed class WorkerOptions
{
    /// <summary>
    /// Redis connection string used for the alert queue.
    /// </summary>
    public string RedisConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Redis list key that stores queued alerts.
    /// </summary>
    public string AlertQueueKey { get; set; } = "mvp:alerts";

    /// <summary>
    /// Delay between polling attempts when the queue is empty.
    /// </summary>
    public int PollIntervalMs { get; set; } = 500;
}
