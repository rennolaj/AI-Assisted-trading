using Npgsql;

namespace Mvp.Trading.Api.Services;

/// <summary>
/// Postgres-backed idempotency store for alert processing.
/// </summary>
public sealed class PostgresIdempotencyStore : IIdempotencyStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresIdempotencyStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<bool> TryAddAsync(string key, CancellationToken ct)
    {
        const string sql = @"
insert into idempotency_keys (idempotency_key, first_seen_utc)
values (@idempotency_key, @first_seen_utc)
on conflict (idempotency_key) do nothing;";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("idempotency_key", key);
        cmd.Parameters.AddWithValue("first_seen_utc", DateTimeOffset.UtcNow.UtcDateTime);

        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }
}
