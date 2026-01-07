using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Mvp.Trading.Contracts;
using Npgsql;
using System.Text.Json;

namespace Mvp.Trading.Execution;

public sealed class KillSwitchService : IKillSwitchService
{
    private readonly string _connectionString;
    private readonly IMemoryCache _cache;
    private readonly ITradingProvider _tradingProvider;
    private readonly ILogger<KillSwitchService> _logger;
    private const string CacheKey = "kill_switch_status";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);

    public KillSwitchService(
        string connectionString,
        IMemoryCache cache,
        ITradingProvider tradingProvider,
        ILogger<KillSwitchService> logger)
    {
        _connectionString = connectionString;
        _cache = cache;
        _tradingProvider = tradingProvider;
        _logger = logger;
    }

    public async Task<bool> IsActiveAsync(CancellationToken ct = default)
    {
        // Check cache first for performance
        if (_cache.TryGetValue(CacheKey, out KillSwitchStatus? cached))
        {
            return cached!.Active;
        }

        var status = await GetStatusAsync(ct);
        return status.Active;
    }

    public async Task<KillSwitchStatus> GetStatusAsync(CancellationToken ct = default)
    {
        // Check cache first
        if (_cache.TryGetValue(CacheKey, out KillSwitchStatus? cached))
        {
            return cached!;
        }

        // Query database
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = "select value from system_state where key = 'kill_switch'";
        await using var cmd = new NpgsqlCommand(sql, conn);
        var jsonValue = await cmd.ExecuteScalarAsync(ct);

        if (jsonValue == null)
        {
            _logger.LogWarning("Kill switch state not found in database, assuming inactive");
            return new KillSwitchStatus(false, KillSwitchLevel.PAUSE_ALL, null, null);
        }

        var json = jsonValue.ToString()!;
        var doc = JsonDocument.Parse(json);
        var active = doc.RootElement.GetProperty("active").GetBoolean();
        var level = Enum.Parse<KillSwitchLevel>(doc.RootElement.GetProperty("level").GetString()!);
        var reason = doc.RootElement.TryGetProperty("reason", out var r) && r.ValueKind != JsonValueKind.Null 
            ? r.GetString() 
            : null;
        var activatedAt = doc.RootElement.TryGetProperty("activated_at", out var a) && a.ValueKind != JsonValueKind.Null
            ? a.GetDateTime()
            : (DateTime?)null;

        var status = new KillSwitchStatus(active, level, reason, activatedAt);

        // Cache for 5 seconds
        _cache.Set(CacheKey, status, CacheDuration);

        return status;
    }

    public async Task ActivateAsync(KillSwitchLevel level, string reason, string activatedBy, CancellationToken ct = default)
    {
        _logger.LogCritical("KILL SWITCH ACTIVATED: Level={Level}, Reason={Reason}, By={By}", level, reason, activatedBy);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var txn = await conn.BeginTransactionAsync(ct);

        try
        {
            // Update system_state
            var now = DateTime.UtcNow;
            var value = JsonSerializer.Serialize(new
            {
                active = true,
                level = level.ToString(),
                reason,
                activated_at = now
            });

            const string updateSql = @"
                update system_state 
                set value = @value::jsonb, updated_at_utc = @now, updated_by = @by
                where key = 'kill_switch'";
            
            await using var updateCmd = new NpgsqlCommand(updateSql, conn, txn);
            updateCmd.Parameters.AddWithValue("value", value);
            updateCmd.Parameters.AddWithValue("now", now);
            updateCmd.Parameters.AddWithValue("by", activatedBy);
            await updateCmd.ExecuteNonQueryAsync(ct);

            // Audit trail
            const string auditSql = @"
                insert into kill_switch_audit (action, level, reason, activated_by, timestamp_utc)
                values ('ACTIVATED', @level, @reason, @by, @now)";
            
            await using var auditCmd = new NpgsqlCommand(auditSql, conn, txn);
            auditCmd.Parameters.AddWithValue("level", level.ToString());
            auditCmd.Parameters.AddWithValue("reason", reason);
            auditCmd.Parameters.AddWithValue("by", activatedBy);
            auditCmd.Parameters.AddWithValue("now", now);
            await auditCmd.ExecuteNonQueryAsync(ct);

            await txn.CommitAsync(ct);

            // Invalidate cache
            _cache.Remove(CacheKey);

            // If EMERGENCY_STOP, cancel all open orders
            if (level == KillSwitchLevel.EMERGENCY_STOP)
            {
                _logger.LogCritical("Emergency stop: cancelling all open orders");
                await CancelAllOpenOrdersAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate kill switch");
            await txn.RollbackAsync(ct);
            throw;
        }
    }

    private async Task CancelAllOpenOrdersAsync(CancellationToken ct)
    {
        try
        {
            var openOrdersResult = await _tradingProvider.GetOpenOrdersAsync(ct);
            if (!openOrdersResult.Ok || openOrdersResult.Value == null)
            {
                _logger.LogError("Failed to fetch open orders for emergency stop: {Error}", openOrdersResult.Error?.Message);
                return;
            }

            var openOrders = openOrdersResult.Value;
            _logger.LogCritical("Emergency stop: found {Count} open orders to cancel", openOrders.Count);

            foreach (var order in openOrders)
            {
                if (string.IsNullOrEmpty(order.OrderId))
                {
                    continue;
                }

                var cancelResult = await _tradingProvider.CancelOrderAsync(order.OrderId, ct);
                if (cancelResult.Ok)
                {
                    _logger.LogInformation("Emergency stop: cancelled order {OrderId}", order.OrderId);
                }
                else
                {
                    _logger.LogError("Emergency stop: failed to cancel order {OrderId}: {Error}", 
                        order.OrderId, cancelResult.Error?.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during emergency order cancellation");
        }
    }

    public async Task DeactivateAsync(string deactivatedBy, string reason, CancellationToken ct = default)
    {
        _logger.LogWarning("Kill switch deactivated: By={By}, Reason={Reason}", deactivatedBy, reason);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var txn = await conn.BeginTransactionAsync(ct);

        try
        {
            // Update system_state
            var now = DateTime.UtcNow;
            var value = JsonSerializer.Serialize(new
            {
                active = false,
                level = "PAUSE_ALL",
                reason = (string?)null,
                activated_at = (DateTime?)null
            });

            const string updateSql = @"
                update system_state 
                set value = @value::jsonb, updated_at_utc = @now, updated_by = @by
                where key = 'kill_switch'";
            
            await using var updateCmd = new NpgsqlCommand(updateSql, conn, txn);
            updateCmd.Parameters.AddWithValue("value", value);
            updateCmd.Parameters.AddWithValue("now", now);
            updateCmd.Parameters.AddWithValue("by", deactivatedBy);
            await updateCmd.ExecuteNonQueryAsync(ct);

            // Audit trail
            const string auditSql = @"
                insert into kill_switch_audit (action, level, reason, activated_by, timestamp_utc)
                values ('DEACTIVATED', 'NONE', @reason, @by, @now)";
            
            await using var auditCmd = new NpgsqlCommand(auditSql, conn, txn);
            auditCmd.Parameters.AddWithValue("reason", reason);
            auditCmd.Parameters.AddWithValue("by", deactivatedBy);
            auditCmd.Parameters.AddWithValue("now", now);
            await auditCmd.ExecuteNonQueryAsync(ct);

            await txn.CommitAsync(ct);

            // Invalidate cache
            _cache.Remove(CacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deactivate kill switch");
            await txn.RollbackAsync(ct);
            throw;
        }
    }
}
