using System;
using Mvp.Trading.Contracts;
using Mvp.Trading.Indicators;
using Npgsql;
using NpgsqlTypes;

namespace Mvp.Trading.Worker;

/// <summary>
/// Postgres-backed store for indicator snapshots.
/// </summary>
public sealed class PostgresIndicatorSnapshotStore : IIndicatorSnapshotStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresIndicatorSnapshotStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task UpsertAsync(IndicatorSnapshot snapshot, CancellationToken ct)
    {
        const string sql = @"
insert into indicator_snapshots (
    alert_id,
    correlation_id,
    computed_at_utc,
    evaluation_time_utc,
    symbol,
    mode,
    direction,
    snapshot_json
)
values (
    @alert_id,
    @correlation_id,
    @computed_at_utc,
    @evaluation_time_utc,
    @symbol,
    @mode,
    @direction,
    @snapshot_json
)
on conflict (alert_id) do update set
    correlation_id = excluded.correlation_id,
    computed_at_utc = excluded.computed_at_utc,
    evaluation_time_utc = excluded.evaluation_time_utc,
    symbol = excluded.symbol,
    mode = excluded.mode,
    direction = excluded.direction,
    snapshot_json = excluded.snapshot_json;";

        var json = IndicatorSnapshotJson.Serialize(snapshot);

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("alert_id", snapshot.AlertId);
        cmd.Parameters.AddWithValue("correlation_id", snapshot.CorrelationId);
        cmd.Parameters.AddWithValue("computed_at_utc", snapshot.ComputedAtUtc.UtcDateTime);
        cmd.Parameters.AddWithValue("evaluation_time_utc", snapshot.EvaluationTimeUtc.UtcDateTime);
        cmd.Parameters.AddWithValue("symbol", snapshot.Symbol);
        cmd.Parameters.AddWithValue("mode", snapshot.Mode);
        cmd.Parameters.AddWithValue("direction", snapshot.Direction);
        cmd.Parameters.Add("snapshot_json", NpgsqlDbType.Jsonb).Value = json;

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
