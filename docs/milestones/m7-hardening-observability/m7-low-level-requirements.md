# M7 - Low-Level Requirements and Implementation Plan

**Created**: 2026-01-07  
**Status**: Implementation Ready  
**Based On**: m7-requirements-overview.md analysis

## Design Decisions and Recommendations

### M7.1 - Reconciliation Loop: Decisions

**Q: Polling Frequency?**  
✅ **Decision**: Start with 60-second polling interval (configurable)
- Rationale: Balance between detection speed and API rate limits
- Can be tuned down to 30s after observing load

**Q: Reconciliation Scope?**  
✅ **Decision**: Phase 1 - Open orders only; Phase 2 - Add position verification
- Rationale: Open orders are critical path, positions are nice-to-have
- Kraken Futures provides `/openpositions` endpoint for future enhancement

**Q: Drift Scenarios and Remediation?**  
✅ **Decision**: Detection only, no auto-remediation (manual intervention required)
- Rationale: Fail-safe approach for MVP, avoid cascading automated corrections
- Remediation actions:
  - `MISSING_ON_EXCHANGE`: Log ERROR + alert (possible network failure during placement)
  - `ORPHANED_ON_EXCHANGE`: Log ERROR + alert (manual intervention - cancel or adopt)
  - `FILL_MISMATCH`: Log ERROR + alert (webhook missed, query fill details)
  - `INVALIDATION_TRIGGERED`: Log INFO (stop-loss hit, expected behavior)

**Q: Position Verification?**  
✅ **Decision**: Defer to Phase 2 (after M7.1 core complete)
- Use Kraken's `GET /api/v3/openpositions` endpoint
- Compare position size vs executed entry orders

### M7.2 - Kill Switch: Decisions

**Q: Trigger Mechanism?**  
✅ **Decision**: Database flag (primary) + Redis cache (performance)
- Table: `system_state` with key='kill_switch', value=JSON with reason/timestamp
- Redis cache for fast reads, database as source of truth
- API endpoint for activation/deactivation with authentication

**Q: Scope of Kill Switch?**  
✅ **Decision**: Three-level kill switch with configurable behavior
- **Level 1 (PAUSE_NEW)**: Stop accepting new alerts, continue monitoring open trades
- **Level 2 (PAUSE_ALL)**: Pause all workers, keep API/health checks running
- **Level 3 (EMERGENCY_STOP)**: Cancel all open orders + pause all operations
- Default: Level 2 (PAUSE_ALL)

**Q: Recovery Process?**  
✅ **Decision**: Manual deactivation only via authenticated API call
- Require explicit reason for deactivation (audit trail)
- System resumes normal operation after deactivation
- Log activation/deactivation events to dedicated audit table

**Q: Kill Switch Authority?**  
✅ **Decision**: API endpoint with shared secret authentication
- Environment variable: `KILL_SWITCH_SECRET` (like webhook secret)
- Fallback: Direct database update (for emergencies when API unreachable)
- Log all activation/deactivation with timestamp + reason

### M7.3 - Metrics and Tracing: Decisions

**Q: Technology Choice?**  
✅ **Decision**: OpenTelemetry + Prometheus + Grafana
- Rationale: Industry standard, vendor-neutral, widely supported
- Migration path to cloud observability platforms if needed

**Q: Metrics to Collect?**  
✅ **Decision**: 15 core metrics across 4 categories (see detailed list below)

**Q: Tracing Requirements?**  
✅ **Decision**: Phase 1 - Basic metrics only; Phase 2 - Add distributed tracing
- Correlation IDs: Add now (cheap, enables future tracing)
- Full distributed tracing: Defer until metrics prove useful

**Q: Dashboard Design?**  
✅ **Decision**: Grafana with 4 pre-built dashboards
- Operational Dashboard (queue, errors, latency)
- Execution Dashboard (orders, outcomes, PnL)
- System Health Dashboard (dependencies, resource usage)
- Alert Rules Dashboard (active alerts, history)

**Q: Health Checks?**  
✅ **Decision**: ASP.NET Core Health Checks with 3 endpoints
- `/health` - Overall health (all checks)
- `/health/ready` - Readiness probe (Kubernetes)
- `/health/live` - Liveness probe (Kubernetes)

---

## M7.1 - Reconciliation Loop: Implementation Tasks

### Task M7.1.1: Database Schema Updates
**File**: `scripts/db/init.sql`

```sql
-- Enhance reconciliation_state table with more details
alter table reconciliation_state 
  add column if not exists reconciliation_type text not null default 'OPEN_ORDERS',
  add column if not exists discrepancy_count int not null default 0,
  add column if not exists last_error text;

-- Create reconciliation_discrepancy table for detailed tracking
create table if not exists reconciliation_discrepancy (
    discrepancy_id uuid primary key default gen_random_uuid(),
    execution_id uuid not null references execution_intent(execution_id),
    detected_at_utc timestamptz not null default now(),
    discrepancy_type text not null, -- MISSING_ON_EXCHANGE, ORPHANED_ON_EXCHANGE, FILL_MISMATCH, etc.
    internal_state jsonb not null,
    exchange_state jsonb not null,
    details text,
    resolved boolean not null default false,
    resolved_at_utc timestamptz,
    resolution_notes text
);

create index idx_reconciliation_discrepancy_execution on reconciliation_discrepancy(execution_id);
create index idx_reconciliation_discrepancy_detected on reconciliation_discrepancy(detected_at_utc);
create index idx_reconciliation_discrepancy_unresolved on reconciliation_discrepancy(resolved) where resolved = false;
```

### Task M7.1.2: Reconciliation Service Interface
**File**: `src/Mvp.Trading.Execution/IReconciliationService.cs`

```csharp
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Execution;

public interface IReconciliationService
{
    /// <summary>
    /// Reconcile all active execution intents against exchange state.
    /// Returns list of detected discrepancies.
    /// </summary>
    Task<Result<ReconciliationResult>> ReconcileAsync(CancellationToken ct = default);
}

public sealed record ReconciliationResult(
    int ExecutionsChecked,
    int DiscrepanciesFound,
    IReadOnlyList<ReconciliationDiscrepancy> Discrepancies
);

public sealed record ReconciliationDiscrepancy(
    Guid ExecutionId,
    ReconciliationDiscrepancyType Type,
    string InternalState,
    string ExchangeState,
    string Details
);

public enum ReconciliationDiscrepancyType
{
    MISSING_ON_EXCHANGE,      // Order placed internally but not on exchange
    ORPHANED_ON_EXCHANGE,     // Order on exchange but not in internal state
    FILL_MISMATCH,            // Fill quantity doesn't match expectations
    INVALIDATION_TRIGGERED,   // Stop-loss triggered (informational)
    STATUS_MISMATCH,          // Order status differs (e.g. canceled vs open)
    ERROR                     // Reconciliation error (network, API, etc.)
}
```

### Task M7.1.3: Reconciliation Service Implementation
**File**: `src/Mvp.Trading.Execution/ReconciliationService.cs`

```csharp
using Microsoft.Extensions.Logging;
using Mvp.Trading.Contracts;
using Mvp.Trading.Integrations;

namespace Mvp.Trading.Execution;

public sealed class ReconciliationService : IReconciliationService
{
    private readonly IExecutionIntentStore _intentStore;
    private readonly IOrderReceiptStore _receiptStore;
    private readonly ITradingProvider _tradingProvider;
    private readonly IReconciliationStore _reconciliationStore;
    private readonly ILogger<ReconciliationService> _logger;

    public ReconciliationService(
        IExecutionIntentStore intentStore,
        IOrderReceiptStore receiptStore,
        ITradingProvider tradingProvider,
        IReconciliationStore reconciliationStore,
        ILogger<ReconciliationService> logger)
    {
        _intentStore = intentStore;
        _receiptStore = receiptStore;
        _tradingProvider = tradingProvider;
        _reconciliationStore = reconciliationStore;
        _logger = logger;
    }

    public async Task<Result<ReconciliationResult>> ReconcileAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting reconciliation loop");

        try
        {
            // 1. Get all active execution intents (not completed/failed)
            var activeIntents = await _intentStore.GetActiveAsync(ct);
            
            // 2. Get open orders from exchange
            var openOrdersResult = await _tradingProvider.GetOpenOrdersAsync(ct);
            if (!openOrdersResult.IsSuccess)
            {
                _logger.LogError("Failed to fetch open orders from exchange: {Error}", openOrdersResult.ErrorMessage);
                return Result<ReconciliationResult>.Fail("RECONCILIATION_EXCHANGE_ERROR", openOrdersResult.ErrorMessage ?? "Unknown error");
            }

            var exchangeOrders = openOrdersResult.Value.ToDictionary(o => o.OrderId, o => o);
            var discrepancies = new List<ReconciliationDiscrepancy>();

            // 3. Compare internal state vs exchange state
            foreach (var intent in activeIntents)
            {
                var receipts = await _receiptStore.GetByExecutionIdAsync(intent.ExecutionId, ct);
                
                foreach (var receipt in receipts)
                {
                    // Check if order exists on exchange
                    if (!exchangeOrders.ContainsKey(receipt.OrderId))
                    {
                        // Order in our system but not on exchange
                        var discrepancy = new ReconciliationDiscrepancy(
                            intent.ExecutionId,
                            ReconciliationDiscrepancyType.MISSING_ON_EXCHANGE,
                            $"OrderId={receipt.OrderId}, Status={receipt.Status}",
                            "NOT_FOUND",
                            $"Order {receipt.OrderId} placed internally but not found on exchange"
                        );
                        discrepancies.Add(discrepancy);
                        
                        _logger.LogError("Reconciliation discrepancy: {Discrepancy}", discrepancy);
                    }
                    else
                    {
                        // Order exists, check status match
                        var exchangeOrder = exchangeOrders[receipt.OrderId];
                        if (receipt.Status != exchangeOrder.Status)
                        {
                            var discrepancy = new ReconciliationDiscrepancy(
                                intent.ExecutionId,
                                ReconciliationDiscrepancyType.STATUS_MISMATCH,
                                $"OrderId={receipt.OrderId}, Status={receipt.Status}",
                                $"Status={exchangeOrder.Status}",
                                $"Status mismatch: internal={receipt.Status}, exchange={exchangeOrder.Status}"
                            );
                            discrepancies.Add(discrepancy);
                            
                            _logger.LogWarning("Status mismatch for order {OrderId}: internal={Internal}, exchange={Exchange}",
                                receipt.OrderId, receipt.Status, exchangeOrder.Status);
                        }
                        
                        // Remove from dictionary (to find orphans later)
                        exchangeOrders.Remove(receipt.OrderId);
                    }
                }
            }

            // 4. Check for orphaned orders (on exchange but not in our system)
            foreach (var orphan in exchangeOrders.Values)
            {
                var discrepancy = new ReconciliationDiscrepancy(
                    Guid.Empty, // No execution_id since it's orphaned
                    ReconciliationDiscrepancyType.ORPHANED_ON_EXCHANGE,
                    "NOT_FOUND",
                    $"OrderId={orphan.OrderId}, Symbol={orphan.Symbol}, Status={orphan.Status}",
                    $"Order {orphan.OrderId} found on exchange but not in internal state"
                );
                discrepancies.Add(discrepancy);
                
                _logger.LogError("Orphaned order found on exchange: {OrderId}", orphan.OrderId);
            }

            // 5. Persist reconciliation results
            await _reconciliationStore.SaveReconciliationAsync(activeIntents.Count, discrepancies, ct);

            var result = new ReconciliationResult(
                activeIntents.Count,
                discrepancies.Count,
                discrepancies
            );

            _logger.LogInformation("Reconciliation complete: checked {Count} executions, found {Discrepancies} discrepancies",
                result.ExecutionsChecked, result.DiscrepanciesFound);

            return Result<ReconciliationResult>.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reconciliation failed with exception");
            return Result<ReconciliationResult>.Fail("RECONCILIATION_ERROR", ex.Message);
        }
    }
}
```

### Task M7.1.4: Reconciliation Store Interface
**File**: `src/Mvp.Trading.Execution/IReconciliationStore.cs`

```csharp
namespace Mvp.Trading.Execution;

public interface IReconciliationStore
{
    Task SaveReconciliationAsync(int executionsChecked, IReadOnlyList<ReconciliationDiscrepancy> discrepancies, CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationDiscrepancy>> GetUnresolvedDiscrepanciesAsync(CancellationToken ct = default);
    Task MarkDiscrepancyResolvedAsync(Guid discrepancyId, string resolutionNotes, CancellationToken ct = default);
}
```

### Task M7.1.5: Reconciliation Background Worker
**File**: `src/Mvp.Trading.Worker/ReconciliationWorker.cs`

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mvp.Trading.Execution;

namespace Mvp.Trading.Worker;

public sealed class ReconciliationWorker : BackgroundService
{
    private readonly IReconciliationService _reconciliationService;
    private readonly ReconciliationOptions _options;
    private readonly ILogger<ReconciliationWorker> _logger;

    public ReconciliationWorker(
        IReconciliationService reconciliationService,
        IOptions<ReconciliationOptions> options,
        ILogger<ReconciliationWorker> logger)
    {
        _reconciliationService = reconciliationService;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ReconciliationWorker started with interval {Interval}s", _options.PollingIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _reconciliationService.ReconcileAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reconciliation worker encountered an error");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("ReconciliationWorker stopped");
    }
}

public sealed class ReconciliationOptions
{
    public int PollingIntervalSeconds { get; set; } = 60;
}
```

---

## M7.2 - Kill Switch: Implementation Tasks

### Task M7.2.1: Database Schema for Kill Switch
**File**: `scripts/db/init.sql`

```sql
-- System state table for kill switch and other global flags
create table if not exists system_state (
    key text primary key,
    value jsonb not null,
    updated_at_utc timestamptz not null default now(),
    updated_by text
);

-- Kill switch audit trail
create table if not exists kill_switch_audit (
    audit_id uuid primary key default gen_random_uuid(),
    action text not null, -- ACTIVATED, DEACTIVATED
    level text not null,  -- PAUSE_NEW, PAUSE_ALL, EMERGENCY_STOP
    reason text not null,
    activated_by text,
    timestamp_utc timestamptz not null default now()
);

create index idx_kill_switch_audit_timestamp on kill_switch_audit(timestamp_utc desc);

-- Initialize kill switch as inactive
insert into system_state (key, value, updated_by)
values ('kill_switch', '{"active": false, "level": "PAUSE_ALL", "reason": null, "activated_at": null}'::jsonb, 'system')
on conflict (key) do nothing;
```

### Task M7.2.2: Kill Switch Service Interface
**File**: `src/Mvp.Trading.Execution/IKillSwitchService.cs`

```csharp
namespace Mvp.Trading.Execution;

public interface IKillSwitchService
{
    Task<bool> IsActiveAsync(CancellationToken ct = default);
    Task<KillSwitchStatus> GetStatusAsync(CancellationToken ct = default);
    Task ActivateAsync(KillSwitchLevel level, string reason, string activatedBy, CancellationToken ct = default);
    Task DeactivateAsync(string deactivatedBy, string reason, CancellationToken ct = default);
}

public sealed record KillSwitchStatus(
    bool Active,
    KillSwitchLevel Level,
    string? Reason,
    DateTime? ActivatedAt
);

public enum KillSwitchLevel
{
    PAUSE_NEW,        // Stop accepting new alerts only
    PAUSE_ALL,        // Pause all workers, keep API running
    EMERGENCY_STOP    // Cancel all open orders + pause everything
}
```

### Task M7.2.3: Kill Switch Service Implementation
**File**: `src/Mvp.Trading.Execution/KillSwitchService.cs`

```csharp
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
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
        var json = await cmd.ExecuteScalarAsync(ct) as string;

        if (json == null)
        {
            _logger.LogWarning("Kill switch state not found in database, assuming inactive");
            return new KillSwitchStatus(false, KillSwitchLevel.PAUSE_ALL, null, null);
        }

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
                await _tradingProvider.CancelAllOrdersAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate kill switch");
            await txn.RollbackAsync(ct);
            throw;
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
```

### Task M7.2.4: Kill Switch API Controller
**File**: `src/Mvp.Trading.Api/Controllers/KillSwitchController.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Mvp.Trading.Execution;

namespace Mvp.Trading.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class KillSwitchController : ControllerBase
{
    private readonly IKillSwitchService _killSwitchService;
    private readonly KillSwitchApiOptions _options;
    private readonly ILogger<KillSwitchController> _logger;

    public KillSwitchController(
        IKillSwitchService killSwitchService,
        IOptions<KillSwitchApiOptions> options,
        ILogger<KillSwitchController> logger)
    {
        _killSwitchService = killSwitchService;
        _options = options.Value;
        _logger = logger;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var status = await _killSwitchService.GetStatusAsync(ct);
        return Ok(status);
    }

    [HttpPost("activate")]
    public async Task<IActionResult> Activate([FromBody] KillSwitchActivationRequest request, CancellationToken ct)
    {
        // Validate secret
        if (request.Secret != _options.Secret)
        {
            _logger.LogWarning("Invalid kill switch secret provided from {IP}", HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { error = "Invalid secret" });
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { error = "Reason is required" });
        }

        await _killSwitchService.ActivateAsync(request.Level, request.Reason, request.ActivatedBy ?? "API", ct);

        return Ok(new { message = "Kill switch activated", level = request.Level, reason = request.Reason });
    }

    [HttpPost("deactivate")]
    public async Task<IActionResult> Deactivate([FromBody] KillSwitchDeactivationRequest request, CancellationToken ct)
    {
        // Validate secret
        if (request.Secret != _options.Secret)
        {
            _logger.LogWarning("Invalid kill switch secret provided from {IP}", HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { error = "Invalid secret" });
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { error = "Reason is required" });
        }

        await _killSwitchService.DeactivateAsync(request.DeactivatedBy ?? "API", request.Reason, ct);

        return Ok(new { message = "Kill switch deactivated", reason = request.Reason });
    }
}

public sealed record KillSwitchActivationRequest(
    string Secret,
    KillSwitchLevel Level,
    string Reason,
    string? ActivatedBy = null
);

public sealed record KillSwitchDeactivationRequest(
    string Secret,
    string Reason,
    string? DeactivatedBy = null
);

public sealed class KillSwitchApiOptions
{
    public string Secret { get; set; } = string.Empty;
}
```

### Task M7.2.5: Integrate Kill Switch into Workers
**File**: `src/Mvp.Trading.Worker/AlertWorker.cs` (modify ExecuteAsync)

```csharp
// Add to AlertWorker.ExecuteAsync loop, before processing alert:

// Check kill switch
var killSwitchActive = await _killSwitchService.IsActiveAsync(stoppingToken);
if (killSwitchActive)
{
    _logger.LogWarning("Kill switch is active, pausing alert processing");
    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); // Wait and check again
    continue;
}

// ... existing alert processing logic
```

**File**: `src/Mvp.Trading.Execution/ExecutionService.cs` (modify ExecuteAsync)

```csharp
// Add to ExecutionService.ExecuteAsync, after heartbeat check:

// Check kill switch
var killSwitchActive = await _killSwitchService.IsActiveAsync(ct);
if (killSwitchActive)
{
    return Fail("KILL_SWITCH_ACTIVE", "Kill switch is active; refusing to execute.");
}

// ... existing execution logic
```

### Task M7.2.6: Chaos Testing Framework
**File**: `tests/Mvp.Trading.Chaos.Tests/FailClosedTests.cs`

```csharp
using Xunit;
using FluentAssertions;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Microsoft.Extensions.DependencyInjection;
using Mvp.Trading.Execution;

namespace Mvp.Trading.Chaos.Tests;

public sealed class FailClosedTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgresContainer = null!;
    private RedisContainer _redisContainer = null!;

    public async Task InitializeAsync()
    {
        _postgresContainer = new PostgreSqlBuilder().Build();
        await _postgresContainer.StartAsync();

        _redisContainer = new RedisBuilder().Build();
        await _redisContainer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }

    [Fact]
    public async Task DatabaseConnectionLost_ShouldFailClosed_NoExecution()
    {
        // Arrange: Set up execution service with valid database
        // Act: Stop database container mid-execution
        // Assert: Execution returns error, no order placed
    }

    [Fact]
    public async Task RedisConnectionLost_ShouldFailClosed_AlertNotQueued()
    {
        // Arrange: Set up alert worker with valid Redis
        // Act: Stop Redis container
        // Assert: Alert enqueue returns error, no processing
    }

    [Fact]
    public async Task ExchangeApiTimeout_ShouldFailClosed_NoOrderPlaced()
    {
        // Arrange: Configure trading provider with timeout
        // Act: Simulate API timeout (Toxiproxy)
        // Assert: Execution returns error, no order placed
    }

    [Fact]
    public async Task LlmProviderTimeout_ShouldFailClosed_AlertRejected()
    {
        // Arrange: Configure MCP gateway with timeout
        // Act: Simulate LLM timeout
        // Assert: Alert rejected, no execution
    }

    [Fact]
    public async Task WorkerCrashMidExecution_ShouldRecoverGracefully()
    {
        // Arrange: Start execution, capture state
        // Act: Kill worker process
        // Assert: Alert reprocessed after restart, no duplicate orders
    }

    [Fact]
    public async Task KillSwitchActivated_ShouldPauseAllOperations()
    {
        // Arrange: System running normally
        // Act: Activate kill switch via API
        // Assert: All workers pause, no new executions
    }

    [Fact]
    public async Task InvalidJsonInDatabase_ShouldFailClosed_LogError()
    {
        // Arrange: Insert corrupted JSON into trade_plan table
        // Act: Attempt to read trade plan
        // Assert: Deserialization fails, error logged, no execution
    }

    [Fact]
    public async Task HeartbeatStale_ShouldRefuseExecution()
    {
        // Arrange: Set heartbeat to stale (timestamp > threshold)
        // Act: Attempt execution
        // Assert: Execution rejected with HEARTBEAT_STALE error
    }
}
```

---

## M7.3 - Metrics and Tracing: Implementation Tasks

### Task M7.3.1: Add OpenTelemetry Packages
**File**: `src/Mvp.Trading.Api/Mvp.Trading.Api.csproj`

```xml
<ItemGroup>
  <PackageReference Include="OpenTelemetry" Version="1.9.0" />
  <PackageReference Include="OpenTelemetry.Exporter.Prometheus.AspNetCore" Version="1.9.0" />
  <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.9.0" />
  <PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.9.0" />
  <PackageReference Include="AspNetCore.HealthChecks.Redis" Version="8.0.0" />
  <PackageReference Include="AspNetCore.HealthChecks.Npgsql" Version="8.0.0" />
</ItemGroup>
```

**File**: `src/Mvp.Trading.Worker/Mvp.Trading.Worker.csproj`

```xml
<ItemGroup>
  <PackageReference Include="OpenTelemetry" Version="1.9.0" />
  <PackageReference Include="OpenTelemetry.Exporter.Prometheus.HttpListener" Version="1.9.0" />
</ItemGroup>
```

### Task M7.3.2: Metrics Service Interface
**File**: `src/Mvp.Trading.Contracts/Telemetry/IMetricsService.cs`

```csharp
namespace Mvp.Trading.Contracts.Telemetry;

public interface IMetricsService
{
    // Alert Processing Metrics
    void RecordAlertReceived(string exchange, string symbol);
    void RecordAlertProcessed(string outcome); // "accepted", "rejected_indicator", "rejected_elliott", "rejected_llm", "error"
    void RecordAlertProcessingDuration(TimeSpan duration);

    // Execution Metrics
    void RecordExecutionOutcome(string outcome); // "filled", "placed", "rejected_risk", "rejected_heartbeat", "error"
    void RecordExecutionDuration(TimeSpan duration, string stage); // stage: "total", "llm_adjudication", "order_placement"
    void RecordOrderPlaced(string direction, string orderType); // direction: "LONG"/"SHORT", type: "LIMIT"/"MARKET"

    // Order Management Metrics
    void RecordOrderCancelled(string reason);
    void RecordOrderFilled(string symbol, string direction, decimal quantity, decimal price);
    void RecordStopLossTriggered(string symbol);
    void RecordTakeProfitHit(string symbol, int targetNumber);

    // Error Metrics
    void RecordError(string component, string errorType); // component: "AlertWorker", "ExecutionService", etc.
    void RecordApiError(string provider, string endpoint, string errorCode);

    // System Metrics (Gauges)
    void SetActiveTradesGauge(int count);
    void SetQueueDepthGauge(int count);
    void SetReconciliationDiscrepanciesGauge(int count);
}
```

### Task M7.3.3: OpenTelemetry Metrics Service
**File**: `src/Mvp.Trading.Contracts/Telemetry/OpenTelemetryMetricsService.cs`

```csharp
using System.Diagnostics.Metrics;

namespace Mvp.Trading.Contracts.Telemetry;

public sealed class OpenTelemetryMetricsService : IMetricsService
{
    private readonly Meter _meter;
    
    // Counters
    private readonly Counter<long> _alertsReceived;
    private readonly Counter<long> _alertsProcessed;
    private readonly Counter<long> _executionsProcessed;
    private readonly Counter<long> _ordersPlaced;
    private readonly Counter<long> _ordersCancelled;
    private readonly Counter<long> _ordersFilled;
    private readonly Counter<long> _stopLossTriggered;
    private readonly Counter<long> _takeProfitHit;
    private readonly Counter<long> _errors;
    private readonly Counter<long> _apiErrors;
    
    // Histograms
    private readonly Histogram<double> _alertProcessingDuration;
    private readonly Histogram<double> _executionDuration;
    
    // Gauges (ObservableGauge with callbacks)
    private int _activeTrades;
    private int _queueDepth;
    private int _reconciliationDiscrepancies;

    public OpenTelemetryMetricsService()
    {
        _meter = new Meter("Mvp.Trading", "1.0.0");

        // Initialize counters
        _alertsReceived = _meter.CreateCounter<long>("alerts_received_total", "alerts", "Total alerts received");
        _alertsProcessed = _meter.CreateCounter<long>("alerts_processed_total", "alerts", "Total alerts processed");
        _executionsProcessed = _meter.CreateCounter<long>("executions_processed_total", "executions", "Total executions processed");
        _ordersPlaced = _meter.CreateCounter<long>("orders_placed_total", "orders", "Total orders placed");
        _ordersCancelled = _meter.CreateCounter<long>("orders_cancelled_total", "orders", "Total orders cancelled");
        _ordersFilled = _meter.CreateCounter<long>("orders_filled_total", "orders", "Total orders filled");
        _stopLossTriggered = _meter.CreateCounter<long>("stop_loss_triggered_total", "events", "Total stop-loss triggers");
        _takeProfitHit = _meter.CreateCounter<long>("take_profit_hit_total", "events", "Total take-profit hits");
        _errors = _meter.CreateCounter<long>("errors_total", "errors", "Total errors");
        _apiErrors = _meter.CreateCounter<long>("api_errors_total", "errors", "Total API errors");

        // Initialize histograms
        _alertProcessingDuration = _meter.CreateHistogram<double>("alert_processing_duration_seconds", "seconds", "Alert processing duration");
        _executionDuration = _meter.CreateHistogram<double>("execution_duration_seconds", "seconds", "Execution duration");

        // Initialize gauges
        _meter.CreateObservableGauge("active_trades", () => _activeTrades, "trades", "Current active trades");
        _meter.CreateObservableGauge("queue_depth", () => _queueDepth, "alerts", "Current queue depth");
        _meter.CreateObservableGauge("reconciliation_discrepancies", () => _reconciliationDiscrepancies, "discrepancies", "Current unresolved discrepancies");
    }

    public void RecordAlertReceived(string exchange, string symbol)
    {
        _alertsReceived.Add(1, new KeyValuePair<string, object?>("exchange", exchange), 
                                 new KeyValuePair<string, object?>("symbol", symbol));
    }

    public void RecordAlertProcessed(string outcome)
    {
        _alertsProcessed.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
    }

    public void RecordAlertProcessingDuration(TimeSpan duration)
    {
        _alertProcessingDuration.Record(duration.TotalSeconds);
    }

    public void RecordExecutionOutcome(string outcome)
    {
        _executionsProcessed.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
    }

    public void RecordExecutionDuration(TimeSpan duration, string stage)
    {
        _executionDuration.Record(duration.TotalSeconds, new KeyValuePair<string, object?>("stage", stage));
    }

    public void RecordOrderPlaced(string direction, string orderType)
    {
        _ordersPlaced.Add(1, new KeyValuePair<string, object?>("direction", direction),
                              new KeyValuePair<string, object?>("order_type", orderType));
    }

    public void RecordOrderCancelled(string reason)
    {
        _ordersCancelled.Add(1, new KeyValuePair<string, object?>("reason", reason));
    }

    public void RecordOrderFilled(string symbol, string direction, decimal quantity, decimal price)
    {
        _ordersFilled.Add(1, new KeyValuePair<string, object?>("symbol", symbol),
                              new KeyValuePair<string, object?>("direction", direction));
    }

    public void RecordStopLossTriggered(string symbol)
    {
        _stopLossTriggered.Add(1, new KeyValuePair<string, object?>("symbol", symbol));
    }

    public void RecordTakeProfitHit(string symbol, int targetNumber)
    {
        _takeProfitHit.Add(1, new KeyValuePair<string, object?>("symbol", symbol),
                                new KeyValuePair<string, object?>("target", targetNumber));
    }

    public void RecordError(string component, string errorType)
    {
        _errors.Add(1, new KeyValuePair<string, object?>("component", component),
                        new KeyValuePair<string, object?>("error_type", errorType));
    }

    public void RecordApiError(string provider, string endpoint, string errorCode)
    {
        _apiErrors.Add(1, new KeyValuePair<string, object?>("provider", provider),
                           new KeyValuePair<string, object?>("endpoint", endpoint),
                           new KeyValuePair<string, object?>("error_code", errorCode));
    }

    public void SetActiveTradesGauge(int count)
    {
        _activeTrades = count;
    }

    public void SetQueueDepthGauge(int count)
    {
        _queueDepth = count;
    }

    public void SetReconciliationDiscrepanciesGauge(int count)
    {
        _reconciliationDiscrepancies = count;
    }
}
```

### Task M7.3.4: Configure OpenTelemetry in Program.cs
**File**: `src/Mvp.Trading.Api/Program.cs` (add to builder configuration)

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

// Add OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("mvp-trading-api"))
    .WithMetrics(metrics => metrics
        .AddMeter("Mvp.Trading")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddPrometheusExporter());

// Add metrics service
builder.Services.AddSingleton<IMetricsService, OpenTelemetryMetricsService>();

// Add health checks
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration["Postgres:ConnectionString"]!, name: "postgres")
    .AddRedis(builder.Configuration["Redis:ConnectionString"]!, name: "redis");

// ... existing app.Build()

// Map health check endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/live");

// Map Prometheus metrics endpoint
app.MapPrometheusScrapingEndpoint();
```

### Task M7.3.5: Integrate Metrics into Alert Worker
**File**: `src/Mvp.Trading.Worker/AlertWorker.cs` (modify ExecuteAsync)

```csharp
// Add metrics tracking to alert processing:

var stopwatch = Stopwatch.StartNew();

try
{
    _metricsService.RecordAlertReceived(alertEvent.Exchange, alertEvent.Symbol);
    
    // ... existing processing logic
    
    if (result.IsSuccess)
    {
        _metricsService.RecordAlertProcessed("accepted");
    }
    else
    {
        var outcome = result.ErrorCode switch
        {
            "INDICATOR_REJECTION" => "rejected_indicator",
            "ELLIOTT_NO_CANDIDATES" => "rejected_elliott",
            "LLM_REJECTION" => "rejected_llm",
            _ => "error"
        };
        _metricsService.RecordAlertProcessed(outcome);
    }
}
catch (Exception ex)
{
    _metricsService.RecordError("AlertWorker", ex.GetType().Name);
    _metricsService.RecordAlertProcessed("error");
}
finally
{
    stopwatch.Stop();
    _metricsService.RecordAlertProcessingDuration(stopwatch.Elapsed);
}
```

### Task M7.3.6: Update docker-compose for Observability
**File**: `docker-compose.yml`

```yaml
services:
  # ... existing services (api, worker, postgres, redis)
  
  prometheus:
    image: prom/prometheus:latest
    ports:
      - "9090:9090"
    volumes:
      - ./config/prometheus.yml:/etc/prometheus/prometheus.yml
      - prometheus-data:/prometheus
    command:
      - '--config.file=/etc/prometheus/prometheus.yml'
      - '--storage.tsdb.path=/prometheus'
    networks:
      - mvp-trading-network

  grafana:
    image: grafana/grafana:latest
    ports:
      - "3000:3000"
    environment:
      - GF_SECURITY_ADMIN_PASSWORD=admin
      - GF_USERS_ALLOW_SIGN_UP=false
    volumes:
      - ./config/grafana/provisioning:/etc/grafana/provisioning
      - grafana-data:/var/lib/grafana
    depends_on:
      - prometheus
    networks:
      - mvp-trading-network

volumes:
  prometheus-data:
  grafana-data:
```

### Task M7.3.7: Prometheus Configuration
**File**: `config/prometheus.yml`

```yaml
global:
  scrape_interval: 15s
  evaluation_interval: 15s

scrape_configs:
  - job_name: 'mvp-trading-api'
    static_configs:
      - targets: ['api:8080']
    metrics_path: '/metrics'

  - job_name: 'mvp-trading-worker'
    static_configs:
      - targets: ['worker:8081']
    metrics_path: '/metrics'
```

### Task M7.3.8: Grafana Dashboard Configuration
**File**: `config/grafana/provisioning/dashboards/dashboard.yml`

```yaml
apiVersion: 1

providers:
  - name: 'MVP Trading'
    orgId: 1
    folder: ''
    type: file
    disableDeletion: false
    updateIntervalSeconds: 10
    allowUiUpdates: true
    options:
      path: /etc/grafana/provisioning/dashboards
```

**File**: `config/grafana/provisioning/dashboards/mvp-trading-dashboard.json`

```json
{
  "dashboard": {
    "title": "MVP Trading - Operational Dashboard",
    "panels": [
      {
        "title": "Alert Processing Rate",
        "targets": [
          {
            "expr": "rate(alerts_processed_total[5m])",
            "legendFormat": "{{outcome}}"
          }
        ],
        "type": "graph"
      },
      {
        "title": "Queue Depth",
        "targets": [
          {
            "expr": "queue_depth"
          }
        ],
        "type": "graph"
      },
      {
        "title": "Execution Outcomes",
        "targets": [
          {
            "expr": "executions_processed_total",
            "legendFormat": "{{outcome}}"
          }
        ],
        "type": "piechart"
      },
      {
        "title": "Error Rate",
        "targets": [
          {
            "expr": "rate(errors_total[5m])",
            "legendFormat": "{{component}} - {{error_type}}"
          }
        ],
        "type": "graph"
      },
      {
        "title": "Active Trades",
        "targets": [
          {
            "expr": "active_trades"
          }
        ],
        "type": "stat"
      },
      {
        "title": "Execution Latency (P95)",
        "targets": [
          {
            "expr": "histogram_quantile(0.95, execution_duration_seconds_bucket)"
          }
        ],
        "type": "graph"
      }
    ]
  }
}
```

---

## Testing Strategy

### M7.1 - Reconciliation Testing
- **Unit Tests**: Mock exchange responses, verify discrepancy detection logic
- **Integration Tests**: Place orders, simulate network failure, verify detection
- **Manual Test**: Run reconciliation against live demo environment, verify logging

### M7.2 - Kill Switch Testing
- **Unit Tests**: Verify database state changes, cache invalidation
- **Integration Tests**: Activate kill switch, verify workers pause
- **Chaos Tests**: Run entire chaos test suite (8 scenarios)
- **Manual Test**: Activate EMERGENCY_STOP, verify all orders cancelled

### M7.3 - Metrics Testing
- **Unit Tests**: Verify metrics recording (counter increments, histogram records)
- **Integration Tests**: Run full alert → execution flow, scrape /metrics endpoint
- **Manual Test**: Access Grafana dashboard, verify metrics appear
- **Load Test**: Send 100 alerts, verify Prometheus scrapes successfully

---

## Configuration Updates

### appsettings.json Additions

```json
{
  "Reconciliation": {
    "PollingIntervalSeconds": 60
  },
  "KillSwitch": {
    "Secret": "SET_IN_ENVIRONMENT_VARIABLE",
    "CancelOrdersOnEmergencyStop": true
  },
  "OpenTelemetry": {
    "ServiceName": "mvp-trading-api",
    "MetricsPort": 8080
  }
}
```

### .env Additions

```bash
# Kill Switch Configuration
KILL_SWITCH_SECRET=your-secret-here-change-in-production

# OpenTelemetry (optional, defaults work)
OTEL_SERVICE_NAME=mvp-trading-api
```

---

## Documentation Updates

### docs/operations/m7-operational-runbook.md (to be created)

```markdown
# M7 Operational Runbook

## Reconciliation Loop
- Runs every 60 seconds (configurable)
- Queries `reconciliation_discrepancy` table for unresolved issues
- Manual intervention required for discrepancies

## Kill Switch Activation

### API Method (Recommended)
```bash
curl -X POST http://localhost:8080/api/killswitch/activate \
  -H "Content-Type: application/json" \
  -d '{
    "secret": "YOUR_KILL_SWITCH_SECRET",
    "level": "PAUSE_ALL",
    "reason": "Emergency stop - detected anomaly",
    "activatedBy": "ops-team"
  }'
```

### Database Method (Emergency Fallback)
```sql
update system_state 
set value = '{"active": true, "level": "EMERGENCY_STOP", "reason": "Manual emergency stop", "activated_at": "2026-01-07T12:00:00Z"}'::jsonb
where key = 'kill_switch';
```

## Metrics and Dashboards
- Prometheus: http://localhost:9090
- Grafana: http://localhost:3000 (admin/admin)
- Metrics endpoint: http://localhost:8080/metrics
```

### README.md Updates

Add section:
```markdown
## Observability

### Health Checks
- `/health` - Overall system health
- `/health/ready` - Readiness probe (Kubernetes)
- `/health/live` - Liveness probe (Kubernetes)

### Metrics
- `/metrics` - Prometheus metrics endpoint
- Grafana dashboard: http://localhost:3000
- Prometheus UI: http://localhost:9090

### Kill Switch
Emergency stop via API:
```bash
curl -X POST http://localhost:8080/api/killswitch/activate \
  -H "Content-Type: application/json" \
  -d '{"secret": "$KILL_SWITCH_SECRET", "level": "EMERGENCY_STOP", "reason": "Emergency", "activatedBy": "ops"}'
```
```

---

## Success Metrics

### M7.1 Complete When:
- ✅ ReconciliationWorker running with 60s interval
- ✅ Discrepancies logged to `reconciliation_discrepancy` table
- ✅ Integration test detects missing orders
- ✅ No crashes observed in 24h test run

### M7.2 Complete When:
- ✅ Kill switch API endpoints functional
- ✅ Workers respect kill switch (pause when active)
- ✅ 8/8 chaos tests passing
- ✅ Manual activation/deactivation verified

### M7.3 Complete When:
- ✅ 15+ metrics instrumented and exposed
- ✅ Prometheus scraping successfully
- ✅ Grafana dashboard displays metrics
- ✅ Health check endpoints return 200 OK

---

## Implementation Timeline (Estimated)

**Week 1**: M7.1 - Reconciliation Loop (12-16 hours)
- Day 1-2: Database schema + ReconciliationService
- Day 3: ReconciliationWorker + store implementation
- Day 4: Integration tests + manual verification

**Week 2**: M7.2 - Kill Switch (10-14 hours)
- Day 1: Kill switch service + database schema
- Day 2: API controller + worker integration
- Day 3: Chaos test framework setup
- Day 4: Chaos test implementation + validation

**Week 3**: M7.3 - Metrics (14-18 hours)
- Day 1-2: OpenTelemetry setup + metrics service
- Day 3: Instrument all workers and services
- Day 4: Prometheus + Grafana configuration
- Day 5: Dashboard creation + documentation

**Total**: 36-48 hours (1.5-2 weeks for solo developer)

---

## Risk Mitigation

**Reconciliation Risks:**
- ❌ Risk: Reconciliation loop detects false positives
- ✅ Mitigation: Manual review required, no auto-remediation
- ❌ Risk: Exchange API rate limits exhausted
- ✅ Mitigation: Polling interval configurable, respect existing rate limits

**Kill Switch Risks:**
- ❌ Risk: Accidental activation
- ✅ Mitigation: Require secret + reason, full audit trail
- ❌ Risk: Kill switch database/Redis unreachable
- ✅ Mitigation: Heartbeat already provides safety, kill switch is extra layer

**Metrics Risks:**
- ❌ Risk: Metrics collection adds latency
- ✅ Mitigation: OpenTelemetry is non-blocking, minimal overhead
- ❌ Risk: Prometheus scraping overloads API
- ✅ Mitigation: 15s scrape interval is standard, /metrics endpoint lightweight

---

## Next Steps

1. **Review this document** - confirm all decisions and estimates
2. **Answer any remaining questions** - resolve open items
3. **Start with M7.1** - highest priority (production-critical)
4. **Create feature branches**: `feature/m7.1-reconciliation`, `feature/m7.2-kill-switch`, `feature/m7.3-metrics`
5. **Commit frequently** - small, incremental changes with clear commit messages
6. **Test as you go** - don't wait until end to test integration

Ready to start implementation? 🚀
