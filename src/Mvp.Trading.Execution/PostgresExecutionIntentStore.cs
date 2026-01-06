using Npgsql;

namespace Mvp.Trading.Execution;

/// <summary>
/// Postgres persistence for execution intents.
/// </summary>
public sealed class PostgresExecutionIntentStore : IExecutionIntentStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresExecutionIntentStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task SaveAsync(Guid executionId, Guid planId, string mode, string status, DateTimeOffset createdAtUtc, CancellationToken ct)
    {
        const string sql = @"
insert into execution_intent (
    execution_id,
    plan_id,
    mode,
    status,
    created_at_utc
)
values (@execution_id, @plan_id, @mode, @status, @created_at_utc)
on conflict (execution_id) do update set
    status = excluded.status,
    mode = excluded.mode,
    created_at_utc = excluded.created_at_utc;";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("execution_id", executionId);
        cmd.Parameters.AddWithValue("plan_id", planId);
        cmd.Parameters.AddWithValue("mode", mode);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("created_at_utc", createdAtUtc.UtcDateTime);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
