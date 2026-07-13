using Npgsql;

namespace Mvp.Trading.Api.Services;

/// <summary>
/// Postgres-backed alert processing status lookup.
/// </summary>
public sealed class PostgresAlertProcessingQuery : IAlertProcessingQuery
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresAlertProcessingQuery(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<AlertProcessingStatus?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct)
    {
        const string sql = @"
select alert_id, idempotency_key, status, last_updated_utc, error_message
from alert_processing
where idempotency_key = @idempotency_key
limit 1;";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("idempotency_key", idempotencyKey);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var alertId = reader.GetGuid(0);
        var key = reader.GetString(1);
        var status = reader.GetString(2);
        var updated = reader.GetDateTime(3);
        var error = reader.IsDBNull(4) ? null : reader.GetString(4);

        var updatedUtc = DateTime.SpecifyKind(updated, DateTimeKind.Utc);
        return new AlertProcessingStatus(alertId, key, status, new DateTimeOffset(updatedUtc), error);
    }
}
