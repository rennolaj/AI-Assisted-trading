using System.Text.Json;
using Mvp.Trading.Contracts;
using Mvp.Trading.Elliott;
using Npgsql;
using NpgsqlTypes;

namespace Mvp.Trading.Worker;

/// <summary>
/// Postgres-backed store for Elliott candidates.
/// </summary>
public sealed class PostgresElliottCandidatesStore : IElliottCandidatesStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresElliottCandidatesStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task UpsertAsync(
        Guid alertId,
        DateTimeOffset computedAtUtc,
        DateTimeOffset evaluationTimeUtc,
        string symbol,
        Timeframe baseTimeframe,
        ElliottParameters parameters,
        ElliottCandidates candidates,
        CancellationToken ct)
    {
        const string sql = @"
insert into elliott_candidates (
    alert_id,
    computed_at_utc,
    evaluation_time_utc,
    symbol,
    base_timeframe,
    parameters_json,
    candidates_json
)
values (
    @alert_id,
    @computed_at_utc,
    @evaluation_time_utc,
    @symbol,
    @base_timeframe,
    @parameters_json,
    @candidates_json
)
on conflict (alert_id) do update set
    computed_at_utc = excluded.computed_at_utc,
    evaluation_time_utc = excluded.evaluation_time_utc,
    symbol = excluded.symbol,
    base_timeframe = excluded.base_timeframe,
    parameters_json = excluded.parameters_json,
    candidates_json = excluded.candidates_json;";

        var parametersJson = JsonSerializer.Serialize(parameters, ElliottCandidatesJson.Options);
        var candidatesJson = ElliottCandidatesJson.Serialize(candidates);

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("alert_id", alertId);
        cmd.Parameters.AddWithValue("computed_at_utc", computedAtUtc.UtcDateTime);
        cmd.Parameters.AddWithValue("evaluation_time_utc", evaluationTimeUtc.UtcDateTime);
        cmd.Parameters.AddWithValue("symbol", symbol);
        cmd.Parameters.AddWithValue("base_timeframe", baseTimeframe.ToString());
        cmd.Parameters.Add("parameters_json", NpgsqlDbType.Jsonb).Value = parametersJson;
        cmd.Parameters.Add("candidates_json", NpgsqlDbType.Jsonb).Value = candidatesJson;

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
