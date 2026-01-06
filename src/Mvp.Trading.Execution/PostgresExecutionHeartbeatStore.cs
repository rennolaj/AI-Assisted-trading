using Npgsql;

namespace Mvp.Trading.Execution;

/// <summary>
/// Postgres heartbeat store for execution services.
/// </summary>
public sealed class PostgresExecutionHeartbeatStore : IExecutionHeartbeatStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresExecutionHeartbeatStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<HeartbeatStatus> UpsertAndCheckAsync(string serviceName, int staleThresholdSeconds, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? lastBeat = null;

        const string selectSql = "select last_beat_utc from execution_heartbeat where service_name = @service_name;";
        await using (var selectCmd = _dataSource.CreateCommand(selectSql))
        {
            selectCmd.Parameters.AddWithValue("service_name", serviceName);
            await using var reader = await selectCmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var ts = reader.GetDateTime(0);
                lastBeat = new DateTimeOffset(ts, TimeSpan.Zero);
            }
        }

        var isStale = lastBeat.HasValue &&
                      (now - lastBeat.Value).TotalSeconds > staleThresholdSeconds;

        const string upsertSql = @"
insert into execution_heartbeat (service_name, last_beat_utc, stale_threshold_seconds)
values (@service_name, @last_beat_utc, @stale_threshold_seconds)
on conflict (service_name) do update set
    last_beat_utc = excluded.last_beat_utc,
    stale_threshold_seconds = excluded.stale_threshold_seconds;";

        await using var cmd = _dataSource.CreateCommand(upsertSql);
        cmd.Parameters.AddWithValue("service_name", serviceName);
        cmd.Parameters.AddWithValue("last_beat_utc", now.UtcDateTime);
        cmd.Parameters.AddWithValue("stale_threshold_seconds", staleThresholdSeconds);
        await cmd.ExecuteNonQueryAsync(ct);

        return new HeartbeatStatus(isStale, lastBeat);
    }
}
