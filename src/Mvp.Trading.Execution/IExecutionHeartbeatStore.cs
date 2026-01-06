namespace Mvp.Trading.Execution;

/// <summary>
/// Persists and evaluates execution heartbeat state.
/// </summary>
public interface IExecutionHeartbeatStore
{
    Task<HeartbeatStatus> UpsertAndCheckAsync(string serviceName, int staleThresholdSeconds, CancellationToken ct);
}

/// <summary>
/// Heartbeat evaluation result.
/// </summary>
public sealed record HeartbeatStatus(bool IsStale, DateTimeOffset? LastBeatUtc);
