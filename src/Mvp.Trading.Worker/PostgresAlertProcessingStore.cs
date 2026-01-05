using Mvp.Trading.Contracts;
using Npgsql;

namespace Mvp.Trading.Worker;

/// <summary>
/// Postgres-backed store for alert processing status.
/// </summary>
public sealed class PostgresAlertProcessingStore : IAlertProcessingStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresAlertProcessingStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task UpsertAsync(AlertEvent alert, string status, string? errorMessage, CancellationToken ct)
    {
        const string sql = @"
insert into alert_processing (
    alert_id,
    idempotency_key,
    status,
    last_updated_utc,
    error_message
)
values (@alert_id, @idempotency_key, @status, @last_updated_utc, @error_message)
on conflict (alert_id) do update set
    status = excluded.status,
    last_updated_utc = excluded.last_updated_utc,
    error_message = excluded.error_message;";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("alert_id", alert.AlertId);
        cmd.Parameters.AddWithValue("idempotency_key", alert.IdempotencyKey);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("last_updated_utc", DateTimeOffset.UtcNow.UtcDateTime);
        cmd.Parameters.AddWithValue("error_message", (object?)errorMessage ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
