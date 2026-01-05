using System.Text.Json;
using Mvp.Trading.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace Mvp.Trading.Api.Services;

/// <summary>
/// Postgres-backed persistence for raw payloads and normalized alert events.
/// </summary>
public sealed class PostgresAlertStore : IAlertStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly JsonSerializerOptions _jsonOptions;

    public PostgresAlertStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task StoreAsync(string rawPayload, AlertEvent alert, CancellationToken ct)
    {
        const string sql = @"
insert into alerts (
    alert_id,
    idempotency_key,
    received_at_utc,
    source,
    raw_payload,
    alert_json
)
values (@alert_id, @idempotency_key, @received_at_utc, @source, @raw_payload, @alert_json)
on conflict (idempotency_key) do nothing;";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("alert_id", alert.AlertId);
        cmd.Parameters.AddWithValue("idempotency_key", alert.IdempotencyKey);
        cmd.Parameters.AddWithValue("received_at_utc", alert.ReceivedAtUtc.UtcDateTime);
        cmd.Parameters.AddWithValue("source", alert.Source);
        cmd.Parameters.AddWithValue("raw_payload", rawPayload);
        var alertJson = JsonSerializer.Serialize(alert, _jsonOptions);
        cmd.Parameters.Add("alert_json", NpgsqlDbType.Jsonb).Value = alertJson;

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
