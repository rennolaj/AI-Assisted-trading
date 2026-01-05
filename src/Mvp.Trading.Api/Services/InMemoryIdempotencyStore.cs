using System.Collections.Concurrent;

namespace Mvp.Trading.Api.Services;

/// <summary>
/// In-memory idempotency store (dev-only, not durable).
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, byte> _keys = new();

    public bool TryAdd(string key)
    {
        return _keys.TryAdd(key, 0);
    }
}
