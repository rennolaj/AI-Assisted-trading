using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Api.Services;

/// <summary>
/// In-memory persistence for alerts (dev-only, not durable).
/// </summary>
public sealed class InMemoryAlertStore : IAlertStore
{
    private readonly ConcurrentQueue<AlertRecord> _records = new();

    public Task StoreAsync(string rawPayload, AlertEvent alert, CancellationToken ct)
    {
        _records.Enqueue(new AlertRecord(rawPayload, alert));
        return Task.CompletedTask;
    }

    private sealed record AlertRecord(string RawPayload, AlertEvent Alert);
}
