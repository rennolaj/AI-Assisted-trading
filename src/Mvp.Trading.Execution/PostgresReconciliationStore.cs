using Npgsql;
using System.Text.Json;

namespace Mvp.Trading.Execution;

/// <summary>
/// Postgres persistence for reconciliation results and discrepancies.
/// </summary>
public sealed class PostgresReconciliationStore : IReconciliationStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresReconciliationStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task SaveReconciliationAsync(
        int executionsChecked, 
        IReadOnlyList<ReconciliationDiscrepancy> discrepancies, 
        CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var txn = await conn.BeginTransactionAsync(ct);

        try
        {
            // Insert each discrepancy
            foreach (var discrepancy in discrepancies)
            {
                const string insertSql = @"
insert into reconciliation_discrepancy (
    execution_id,
    detected_at_utc,
    discrepancy_type,
    internal_state,
    exchange_state,
    details,
    resolved
)
values (@execution_id, @detected_at_utc, @discrepancy_type, @internal_state::jsonb, @exchange_state::jsonb, @details, false)";

                await using var cmd = new NpgsqlCommand(insertSql, conn, txn);
                cmd.Parameters.AddWithValue("execution_id", (object?)discrepancy.ExecutionId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("detected_at_utc", DateTimeOffset.UtcNow.UtcDateTime);
                cmd.Parameters.AddWithValue("discrepancy_type", discrepancy.Type.ToString());
                cmd.Parameters.AddWithValue("internal_state", discrepancy.InternalState);
                cmd.Parameters.AddWithValue("exchange_state", discrepancy.ExchangeState);
                cmd.Parameters.AddWithValue("details", discrepancy.Details);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // Update reconciliation_state for each execution checked
            // (This is simplified - in production you might want to update per execution)
            
            await txn.CommitAsync(ct);
        }
        catch
        {
            await txn.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<ReconciliationDiscrepancy>> GetUnresolvedDiscrepanciesAsync(
        CancellationToken ct = default)
    {
        const string sql = @"
select discrepancy_id, execution_id, discrepancy_type, internal_state, exchange_state, details
from reconciliation_discrepancy
where resolved = false
order by detected_at_utc desc
limit 100";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var results = new List<ReconciliationDiscrepancy>();
        while (await reader.ReadAsync(ct))
        {
            var executionId = await reader.IsDBNullAsync(1, ct) ? (Guid?)null : reader.GetGuid(1);
            var type = Enum.Parse<ReconciliationDiscrepancyType>(reader.GetString(2));
            var internalState = reader.GetString(3);
            var exchangeState = reader.GetString(4);
            var details = reader.GetString(5);

            results.Add(new ReconciliationDiscrepancy(
                executionId,
                type,
                internalState,
                exchangeState,
                details
            ));
        }

        return results;
    }

    public async Task MarkDiscrepancyResolvedAsync(
        Guid discrepancyId, 
        string resolutionNotes, 
        CancellationToken ct = default)
    {
        const string sql = @"
update reconciliation_discrepancy
set resolved = true,
    resolved_at_utc = @resolved_at_utc,
    resolution_notes = @resolution_notes
where discrepancy_id = @discrepancy_id";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("discrepancy_id", discrepancyId);
        cmd.Parameters.AddWithValue("resolved_at_utc", DateTimeOffset.UtcNow.UtcDateTime);
        cmd.Parameters.AddWithValue("resolution_notes", resolutionNotes);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
