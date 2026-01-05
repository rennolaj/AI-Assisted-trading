using System;

namespace Mvp.Trading.Integrations.Kraken;

/// <summary>
/// Tracks Kraken Futures rate limit usage within a rolling window.
/// </summary>
public sealed class KrakenFuturesRateLimitBudget
{
    private readonly object _gate = new();
    private readonly KrakenFuturesRateLimitOptions _options;
    private DateTimeOffset _windowStartUtc;
    private int _usedCost;

    public KrakenFuturesRateLimitBudget(KrakenFuturesRateLimitOptions options)
    {
        _options = options;
        _windowStartUtc = DateTimeOffset.UtcNow;
    }

    public bool TryConsume(int cost)
    {
        if (cost <= 0)
        {
            return true;
        }

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            if ((now - _windowStartUtc).TotalSeconds >= _options.WindowSeconds)
            {
                _windowStartUtc = now;
                _usedCost = 0;
            }

            if (_usedCost + cost > _options.MaxCostPerWindow)
            {
                return false;
            }

            _usedCost += cost;
            return true;
        }
    }
}
