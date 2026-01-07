using Mvp.Trading.Contracts;
using Npgsql;

namespace Mvp.Trading.Execution;

/// <summary>
/// Postgres persistence for order receipts.
/// </summary>
public sealed class PostgresOrderReceiptStore : IOrderReceiptStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresOrderReceiptStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task SaveAsync(Guid executionId, string orderKind, OrderReceipt receipt, CancellationToken ct)
    {
        const string sql = @"
insert into order_receipt (
    receipt_id,
    execution_id,
    order_kind,
    client_order_id,
    exchange_order_id,
    status,
    qty,
    price,
    created_at_utc
)
values (@receipt_id, @execution_id, @order_kind, @client_order_id, @exchange_order_id, @status, @qty, @price, @created_at_utc);";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("receipt_id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("execution_id", executionId);
        cmd.Parameters.AddWithValue("order_kind", orderKind);
        cmd.Parameters.AddWithValue("client_order_id", receipt.ClientOrderId);
        cmd.Parameters.AddWithValue("exchange_order_id", (object?)receipt.ExchangeOrderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status", receipt.Status);
        cmd.Parameters.AddWithValue("qty", (object?)receipt.Qty ?? DBNull.Value);
        cmd.Parameters.AddWithValue("price", (object?)receipt.Price ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created_at_utc", DateTimeOffset.UtcNow.UtcDateTime);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<OrderReceipt>> GetByExecutionIdAsync(Guid executionId, CancellationToken ct = default)
    {
        const string sql = @"
select client_order_id, exchange_order_id, status, qty, price
from order_receipt
where execution_id = @execution_id
order by created_at_utc";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("execution_id", executionId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var results = new List<OrderReceipt>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new OrderReceipt(
                reader.GetString(0),
                await reader.IsDBNullAsync(1, ct) ? null : reader.GetString(1),
                reader.GetString(2),
                await reader.IsDBNullAsync(3, ct) ? null : reader.GetDecimal(3),
                await reader.IsDBNullAsync(4, ct) ? null : reader.GetDecimal(4)
            ));
        }

        return results;
    }
}
