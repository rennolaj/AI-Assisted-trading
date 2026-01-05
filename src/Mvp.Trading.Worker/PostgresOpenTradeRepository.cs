using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Mvp.Trading.Worker;

/// <summary>
/// Postgres-backed repository for open trades.
/// </summary>
public sealed class PostgresOpenTradeRepository : IOpenTradeRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresOpenTradeRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<OpenTrade>> GetOpenTradesAsync(string exchangeId, CancellationToken ct)
    {
        const string sql = @"
select trade_id, exchange_id, symbol, side, invalidation_price
from open_trades
where status = 'open' and exchange_id = @exchange_id;";

        var results = new List<OpenTrade>();
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("exchange_id", exchangeId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var tradeId = reader.GetGuid(0);
            var exId = reader.GetString(1);
            var symbol = reader.GetString(2);
            var side = reader.GetString(3);
            var invalidationPrice = reader.IsDBNull(4) ? (decimal?)null : reader.GetDecimal(4);

            results.Add(new OpenTrade(tradeId, exId, symbol, side, invalidationPrice));
        }

        return results;
    }

    public async Task UpdateHeartbeatAsync(Guid tradeId, decimal lastPrice, CancellationToken ct)
    {
        const string sql = @"
update open_trades
set last_checked_utc = @checked_utc,
    last_price = @last_price
where trade_id = @trade_id;";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("checked_utc", DateTimeOffset.UtcNow.UtcDateTime);
        cmd.Parameters.AddWithValue("last_price", lastPrice);
        cmd.Parameters.AddWithValue("trade_id", tradeId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkInvalidatedAsync(Guid tradeId, decimal lastPrice, string reason, CancellationToken ct)
    {
        const string sql = @"
update open_trades
set status = 'invalidated',
    invalidated_at_utc = @invalidated_utc,
    last_checked_utc = @invalidated_utc,
    last_price = @last_price,
    invalidation_reason = @reason
where trade_id = @trade_id;";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("invalidated_utc", DateTimeOffset.UtcNow.UtcDateTime);
        cmd.Parameters.AddWithValue("last_price", lastPrice);
        cmd.Parameters.AddWithValue("reason", reason);
        cmd.Parameters.AddWithValue("trade_id", tradeId);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
