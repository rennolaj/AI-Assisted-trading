using Npgsql;

namespace Mvp.Trading.Api.Services;

/// <summary>
/// Postgres-backed query for indicator snapshots.
/// </summary>
public sealed class PostgresIndicatorSnapshotQuery : IIndicatorSnapshotQuery
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresIndicatorSnapshotQuery(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<string?> GetJsonByAlertIdAsync(Guid alertId, CancellationToken ct)
    {
        const string sql = "select snapshot_json from indicator_snapshots where alert_id = @alert_id;";
        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("alert_id", alertId);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }
}
