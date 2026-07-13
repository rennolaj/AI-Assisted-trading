using Npgsql;

namespace Mvp.Trading.Api.Services;

/// <summary>
/// Postgres-backed query for Elliott candidates.
/// </summary>
public sealed class PostgresElliottCandidatesQuery : IElliottCandidatesQuery
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresElliottCandidatesQuery(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<string?> GetJsonByAlertIdAsync(Guid alertId, CancellationToken ct)
    {
        const string sql = "select candidates_json from elliott_candidates where alert_id = @alert_id;";
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("alert_id", alertId);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }
}
