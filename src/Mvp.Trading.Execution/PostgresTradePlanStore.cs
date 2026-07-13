using System.Text.Json;
using Mvp.Trading.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace Mvp.Trading.Execution;

/// <summary>
/// Postgres persistence for trade plans.
/// </summary>
public sealed class PostgresTradePlanStore : ITradePlanStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly JsonSerializerOptions _jsonOptions;

    public PostgresTradePlanStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task SaveAsync(Guid alertId, TradePlan plan, CancellationToken ct)
    {
        const string sql = @"
insert into trade_plan (
    plan_id,
    alert_id,
    created_at_utc,
    plan_json
)
values (@plan_id, @alert_id, @created_at_utc, @plan_json)
on conflict (plan_id) do update set
    alert_id = excluded.alert_id,
    created_at_utc = excluded.created_at_utc,
    plan_json = excluded.plan_json;";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("plan_id", plan.PlanId);
        cmd.Parameters.AddWithValue("alert_id", alertId);
        cmd.Parameters.AddWithValue("created_at_utc", plan.CreatedAtUtc.UtcDateTime);
        var json = JsonSerializer.Serialize(plan, _jsonOptions);
        cmd.Parameters.Add("plan_json", NpgsqlDbType.Jsonb).Value = json;

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
