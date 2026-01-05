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

    public bool TryAdd(string key)
    {
        const string sql = @"
insert into idempotency_keys (idempotency_key, first_seen_utc)
values (@idempotency_key, @first_seen_utc)
on conflict (idempotency_key) do nothing;";

        using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("idempotency_key", key);
        cmd.Parameters.AddWithValue("first_seen_utc", DateTimeOffset.UtcNow.UtcDateTime);

        return cmd.ExecuteNonQuery() == 1;
    }
}
