namespace Mvp.Trading.Api.Services;

/// <summary>
/// Tracks processed idempotency keys to prevent duplicates.
/// </summary>
public interface IIdempotencyStore
{
    bool TryAdd(string key);
}
