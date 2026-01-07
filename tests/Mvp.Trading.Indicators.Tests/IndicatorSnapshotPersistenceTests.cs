using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mvp.Trading.Api.Services;
using Mvp.Trading.Contracts;
using Mvp.Trading.Indicators;
using Mvp.Trading.Worker;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Mvp.Trading.Indicators.Tests;

/// <summary>
/// Integration test for snapshot persistence (opt-in via env vars).
/// </summary>
public sealed class IndicatorSnapshotPersistenceTests
{
    private const string EnabledEnv = "INDICATOR_SNAPSHOT_INTEGRATION_TESTS";
    private const string ConnectionEnv = "POSTGRES_CONNECTION_STRING";

    [Fact]
    public async Task Snapshot_Persists_And_IsRetrievable_ByAlertId()
    {
        if (!IsEnabled())
        {
            return;
        }

        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnv);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        await EnsureSchemaAsync(dataSource);

        var alertId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid().ToString("N");
        await InsertAlertAsync(dataSource, alertId, idempotencyKey);

        var snapshot = BuildSnapshot(alertId);
        var store = new PostgresIndicatorSnapshotStore(dataSource);
        var query = new PostgresIndicatorSnapshotQuery(dataSource);

        await store.UpsertAsync(snapshot, CancellationToken.None);
        var json = await query.GetJsonByAlertIdAsync(alertId, CancellationToken.None);

        Assert.NotNull(json);
        using var doc = JsonDocument.Parse(json!);
        Assert.Equal(alertId.ToString(), doc.RootElement.GetProperty("alertId").GetString());

        await CleanupAsync(dataSource, alertId, idempotencyKey);
    }

    private static bool IsEnabled()
    {
        return string.Equals(Environment.GetEnvironmentVariable(EnabledEnv), "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Environment.GetEnvironmentVariable(EnabledEnv), "true", StringComparison.OrdinalIgnoreCase);
    }

    private static IndicatorSnapshot BuildSnapshot(Guid alertId)
    {
        return new IndicatorSnapshot(
            alertId,
            Guid.NewGuid(),
            "BTCUSD.P",
            IndicatorDefaults.ScalpingMode,
            "LONG",
            Timeframe.M30,
            Timeframe.H2,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            new IndicatorParameters(14, 14, 12, 26, 9, new VolumeRule("SMA_RATIO", 20, 1.5m)),
            new[]
            {
                new TimeframeSnapshot(
                    Timeframe.M30,
                    new RsiState(35m, "NEUTRAL"),
                    new StochRsiState(42m, 38m, "NEUTRAL"),
                    new MacdState(1.2m, 1.0m, 0.2m, "BULLISH"),
                    new VolumeState(1.6m, "CONFIRMED", "SMA_RATIO:20:1.5"))
            },
            new IndicatorGates(true, true, true),
            new IndicatorConfirmations(true, true, true, true, true, true),
            new IndicatorScore(5, 100, 100, 100m),
            new IndicatorRisk("SAFE", "ALLOW", 1.25m, true, true, 4));
    }

    private static async Task EnsureSchemaAsync(NpgsqlDataSource dataSource)
    {
        const string sql = @"
create table if not exists idempotency_keys (
    idempotency_key text primary key,
    first_seen_utc timestamptz not null
);
create table if not exists alerts (
    alert_id uuid primary key,
    idempotency_key text not null references idempotency_keys(idempotency_key),
    received_at_utc timestamptz not null,
    source text not null,
    raw_payload text not null,
    alert_json jsonb not null
);
create table if not exists indicator_snapshots (
    alert_id uuid primary key references alerts(alert_id),
    correlation_id uuid not null,
    computed_at_utc timestamptz not null,
    evaluation_time_utc timestamptz not null,
    symbol text not null,
    mode text not null,
    direction text not null,
    snapshot_json jsonb not null
);";

        await using var cmd = dataSource.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertAlertAsync(NpgsqlDataSource dataSource, Guid alertId, string idempotencyKey)
    {
        const string insertKeys = "insert into idempotency_keys (idempotency_key, first_seen_utc) values (@key, @utc) on conflict do nothing;";
        const string insertAlert = @"
insert into alerts (alert_id, idempotency_key, received_at_utc, source, raw_payload, alert_json)
values (@alert_id, @key, @utc, @source, @raw_payload, @alert_json);";

        await using var cmd = dataSource.CreateCommand(insertKeys + insertAlert);
        cmd.Parameters.AddWithValue("key", idempotencyKey);
        cmd.Parameters.AddWithValue("utc", DateTimeOffset.UtcNow.UtcDateTime);
        cmd.Parameters.AddWithValue("alert_id", alertId);
        cmd.Parameters.AddWithValue("source", "test");
        cmd.Parameters.AddWithValue("raw_payload", "{}");
        var alertJson = JsonSerializer.Serialize(new { alertId });
        cmd.Parameters.Add("alert_json", NpgsqlTypes.NpgsqlDbType.Jsonb).Value = alertJson;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task CleanupAsync(NpgsqlDataSource dataSource, Guid alertId, string idempotencyKey)
    {
        const string sql = @"
delete from indicator_snapshots where alert_id = @alert_id;
delete from alerts where alert_id = @alert_id;
delete from idempotency_keys where idempotency_key = @key;";

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("alert_id", alertId);
        cmd.Parameters.AddWithValue("key", idempotencyKey);
        await cmd.ExecuteNonQueryAsync();
    }
}
