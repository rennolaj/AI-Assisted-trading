using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Api.Services;

/// <summary>
/// In-memory queue for alerts (dev-only, not durable).
/// </summary>
public sealed class InMemoryAlertQueue : IAlertQueue
{
    private readonly ConcurrentQueue<AlertEvent> _queue = new();

    public Task EnqueueAsync(AlertEvent alert, CancellationToken ct)
    {
        _queue.Enqueue(alert);
        return Task.CompletedTask;
    }
}
