using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mvp.Trading.Worker;

/// <summary>
/// Retrieves and updates open trades for monitoring.
/// </summary>
public interface IOpenTradeRepository
{
    Task<IReadOnlyList<OpenTrade>> GetOpenTradesAsync(string exchangeId, CancellationToken ct);
    Task UpdateHeartbeatAsync(Guid tradeId, decimal lastPrice, CancellationToken ct);
    Task MarkInvalidatedAsync(Guid tradeId, decimal lastPrice, string reason, CancellationToken ct);
}
