using System;
using System.Threading;
using System.Threading.Tasks;
using Mvp.Trading.Api.Models;
using Npgsql;

namespace Mvp.Trading.Api.Services;

/// <summary>
/// Postgres-backed writer for open trades.
/// </summary>
public sealed class PostgresOpenTradeCommand : IOpenTradeCommand
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresOpenTradeCommand(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<Guid> CreateOpenTradeAsync(OpenTradeRequest request, CancellationToken ct)
    {
        const string sql = @"
insert into open_trades (
    trade_id,
    exchange_id,
    symbol,
    side,
    entry_price,
    invalidation_price,
    status,
    opened_at_utc
)
values (@trade_id, @exchange_id, @symbol, @side, @entry_price, @invalidation_price, 'open', @opened_at_utc);";

        var tradeId = Guid.NewGuid();
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("trade_id", tradeId);
        cmd.Parameters.AddWithValue("exchange_id", request.ExchangeId);
        cmd.Parameters.AddWithValue("symbol", request.Symbol);
        cmd.Parameters.AddWithValue("side", request.Side);
        cmd.Parameters.AddWithValue("entry_price", (object?)request.EntryPrice ?? DBNull.Value);
        cmd.Parameters.AddWithValue("invalidation_price", (object?)request.InvalidationPrice ?? DBNull.Value);
        cmd.Parameters.AddWithValue("opened_at_utc", DateTimeOffset.UtcNow.UtcDateTime);

        await cmd.ExecuteNonQueryAsync(ct);
        return tradeId;
    }
}
