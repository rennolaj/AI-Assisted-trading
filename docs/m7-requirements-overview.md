# M7 - Hardening and Observability: Requirements Overview

**Created**: 2025-01-XX  
**Status**: Requirements Analysis  
**Goal**: Resilience, monitoring, and reconciliation for production readiness

## Executive Summary

M7 is the final milestone before MVP production deployment. This document analyzes the three M7 stories to identify:
- ✅ **What infrastructure already exists** (can leverage immediately)
- 🔍 **What needs research** (design decisions required)
- 🏗️ **What needs implementation** (new code to write)

## M7.1 - Reconciliation Loop for Orders/Positions

### Goal
Continuous background process that verifies exchange state matches internal execution state, detects discrepancies, and handles drift scenarios.

### What Exists Already ✅

**Database Infrastructure:**
- `reconciliation_state` table exists in `scripts/db/init.sql` (lines 115-121):
  ```sql
  create table if not exists reconciliation_state (
      execution_id uuid primary key references execution_intent(execution_id),
      status text not null,
      details text,
      last_checked_utc timestamptz not null
  );
  ```
- `fill_receipt` table exists for tracking actual fills
- `order_receipt` table exists for tracking placed orders
- `execution_intent` table exists for tracking intended executions

**Exchange API Methods:**
The `ITradingProvider` interface (ExchangeAbstractions.cs, lines 45-77) already provides:
- `GetOpenOrdersAsync()` - queries current open orders from exchange
- `CancelOrderAsync()` - ability to cancel discrepant orders
- No explicit "GetOrderStatus(orderId)" method but open orders list includes all active orders

**Implementation in Kraken Provider:**
- `KrakenFuturesTradingProvider.cs` implements `GetOpenOrdersAsync()` (line 70)
- Returns `Result<IReadOnlyList<OpenOrder>>` structure

**Data Store Interfaces:**
- `IExecutionIntentStore` - query execution intents
- `IOrderReceiptStore` - query order receipts
- `ITradePlanStore` - comment mentions "for audit and reconciliation" (line 4)

### What Needs Research 🔍

**Reconciliation Strategy:**
1. **Polling Frequency**: How often to reconcile? (Every 30s? 1min? 5min?)
2. **Reconciliation Scope**: 
   - Check only open trades?
   - Also verify closed trades match fills?
   - Verify positions match expectations?
3. **Drift Scenarios**:
   - What if order placed internally but not on exchange? (network failure)
   - What if order exists on exchange but not internally? (manual intervention or crash)
   - What if fill happened but not recorded? (webhook missed)
   - What if stop-loss triggered but not detected? (price gap)
4. **Remediation Policy**:
   - Auto-cancel orphaned exchange orders?
   - Alert only for manual intervention?
   - Auto-retry failed orders?
   - Circuit breaker after N discrepancies?

**Position Verification:**
- Does Kraken Futures API provide position query endpoint? (Research needed)
- How to handle partial fills vs expected quantity?
- How to verify stop-loss and take-profit orders still active?

### What Needs Implementation 🏗️

**Background Worker:**
- New `ReconciliationWorker.cs` inheriting from `BackgroundService`
- Similar pattern to `TradeMonitorWorker.cs` (ExecuteAsync loop with cancellation)
- Configurable poll interval (appsettings.json)

**Reconciliation Service:**
- New `IReconciliationService` interface in `Mvp.Trading.Execution`
- Implementation: `ReconciliationService.cs`
- Core method: `ReconcileAsync(CancellationToken ct)` that:
  1. Queries all active `execution_intent` records with status != 'COMPLETED'
  2. Queries `order_receipt` for each execution
  3. Calls `GetOpenOrdersAsync()` from exchange
  4. Compares internal vs exchange state
  5. Updates `reconciliation_state` table with findings
  6. Returns list of discrepancies

**Discrepancy Handling:**
- Define `ReconciliationStatus` enum: `OK`, `MISSING_ON_EXCHANGE`, `ORPHANED_ON_EXCHANGE`, `FILL_MISMATCH`, `ERROR`
- Logging strategy for each discrepancy type
- Alert mechanism (log as ERROR? Write to alerts table? External alerting?)

**Configuration:**
- Add `ReconciliationOptions` class:
  ```csharp
  public sealed class ReconciliationOptions
  {
      public int PollingIntervalSeconds { get; set; } = 60;
      public bool AutoCancelOrphans { get; set; } = false;
      public int MaxDiscrepanciesBeforeCircuitBreaker { get; set; } = 10;
  }
  ```

**Testing Requirements:**
- Unit tests for reconciliation logic (mocked exchange responses)
- Integration test: place order, simulate network failure, verify reconciliation detects it
- Chaos test: manually add orphan order on exchange, verify detection

---

## M7.2 - Kill Switch and Fail-Closed Chaos Tests

### Goal
Manual kill switch to halt all trading activity + automated chaos tests to verify system fails closed under various failure scenarios.

### What Exists Already ✅

**Heartbeat Infrastructure:**
- `IExecutionHeartbeatStore` interface exists (IExecutionHeartbeatStore.cs)
- `PostgresExecutionHeartbeatStore` implementation exists
- `execution_heartbeat` table in database
- `ExecutionService.cs` already checks heartbeat before executing (lines 43-46):
  ```csharp
  var heartbeat = await _heartbeatStore.UpsertAndCheckAsync(ServiceName, settings.StaleThresholdSeconds, ct);
  if (heartbeat.IsStale)
  {
      return Fail("EXECUTION_HEARTBEAT_STALE", "Execution heartbeat is stale; refusing to execute.");
  }
  ```

**Dead-Man's Switch on Exchange:**
- `ITradingProvider.CancelAllOrdersAfterAsync(int timeoutSeconds)` method exists
- Kraken Futures implements this feature (exchange-side dead-man's switch)
- This is **exchange-level** kill switch (if heartbeat stops, exchange cancels all orders)

### What Needs Research 🔍

**Kill Switch Design:**
1. **Trigger Mechanism**:
   - REST API endpoint (POST /api/killswitch/activate)?
   - Redis flag (write 'KILL_SWITCH_ACTIVE' key)?
   - Database flag (kill_switch table)?
   - Environment variable (requires restart)?
   - File-based flag (/tmp/kill-switch.flag)?
2. **Scope of Kill Switch**:
   - Stop accepting new alerts?
   - Stop processing queued alerts?
   - Cancel all open orders immediately?
   - Pause monitoring workers?
   - Shut down entire system?
3. **Recovery Process**:
   - Manual deactivation only?
   - Auto-recovery after timeout?
   - Require specific steps to resume?
4. **Kill Switch Levels**:
   - Level 1: Pause new trades only (monitoring continues)
   - Level 2: Cancel open orders + pause
   - Level 3: Full system shutdown

**Chaos Test Scenarios:**
1. Database connection failure during execution
2. Redis connection failure during alert processing
3. Exchange API timeout/failure during order placement
4. LLM provider timeout/failure during adjudication
5. Worker crash mid-execution
6. Partial network failure (can read but not write)
7. Invalid data in database (corrupted JSON fields)
8. Race condition: multiple workers processing same alert

**Fail-Closed Verification:**
- For each scenario, define expected behavior (log error + no execution? retry? circuit breaker?)
- Current code already has fail-closed patterns (heartbeat check, Result<T> error handling)
- Need to systematically test each failure mode

### What Needs Implementation 🏗️

**Kill Switch Infrastructure:**

**Option A: Database Flag (Recommended)**
- New table: `system_state` with columns: `key text primary key, value text, updated_at_utc timestamptz`
- Check `SELECT value FROM system_state WHERE key = 'kill_switch'` before execution
- Fast to query, survives restarts, can be updated via API or direct SQL

**Option B: Redis Flag**
- Write `KILL_SWITCH_ACTIVE` key to Redis
- Fast to check (in-memory)
- Volatile (lost on Redis restart unless persisted)

**API Endpoint:**
- New `KillSwitchController.cs` in `Mvp.Trading.Api`:
  ```csharp
  [ApiController]
  [Route("api/[controller]")]
  public sealed class KillSwitchController : ControllerBase
  {
      [HttpPost("activate")]
      public async Task<IActionResult> Activate([FromBody] KillSwitchRequest request)
      
      [HttpPost("deactivate")]
      public async Task<IActionResult> Deactivate()
      
      [HttpGet("status")]
      public async Task<IActionResult> GetStatus()
  }
  ```

**Kill Switch Service:**
- New `IKillSwitchService` interface:
  ```csharp
  public interface IKillSwitchService
  {
      Task<bool> IsActiveAsync(CancellationToken ct);
      Task ActivateAsync(string reason, CancellationToken ct);
      Task DeactivateAsync(CancellationToken ct);
      Task<KillSwitchStatus> GetStatusAsync(CancellationToken ct);
  }
  ```

**Integration Points:**
- `AlertWorker.cs` - check kill switch before processing alerts
- `ExecutionService.cs` - check kill switch before execution (in addition to heartbeat)
- `TradeMonitorWorker.cs` - check kill switch before monitoring

**Chaos Testing Framework:**
- New test project: `Mvp.Trading.Chaos.Tests`
- Use Testcontainers for Docker-based integration tests
- Toxiproxy for network failure simulation
- Custom failure injection middleware for database/Redis failures
- Test matrix:
  ```
  | Scenario                | Expected Result                    | Verified? |
  |-------------------------|------------------------------------|-----------|
  | DB connection lost      | No execution, error logged         | [ ]       |
  | Redis connection lost   | Alert queuing fails, error logged  | [ ]       |
  | Exchange API timeout    | Execution fails, no order placed   | [ ]       |
  | LLM timeout             | Alert rejected, fail-closed        | [ ]       |
  | Worker crash            | Alert reprocessed after restart    | [ ]       |
  | Kill switch activated   | All operations paused              | [ ]       |
  | Invalid JSON in DB      | Deserialization fails, logged      | [ ]       |
  ```

**Configuration:**
- Add `KillSwitchOptions`:
  ```csharp
  public sealed class KillSwitchOptions
  {
      public string StorageType { get; set; } = "Database"; // "Database" or "Redis"
      public bool CancelOrdersOnActivate { get; set; } = true;
      public bool PauseWorkersOnActivate { get; set; } = true;
  }
  ```

---

## M7.3 - Metrics and Tracing

### Goal
Comprehensive observability: metrics collection, distributed tracing, and monitoring dashboards for queue lag, error rates, and execution outcomes.

### What Exists Already ✅

**Logging Infrastructure:**
- `ILogger<T>` used throughout codebase (13+ matches in grep search)
- Structured logging in:
  - `AlertWorker.cs` (line 25)
  - `TradeMonitorWorker.cs` (line 21)
  - `ExecutionService.cs`
  - `IndicatorEngine.cs` (line 19)
  - `McpGatewayRouter.cs` (line 14)

**No Existing Metrics/Tracing:**
- No OpenTelemetry packages found in .csproj files
- No Prometheus instrumentation
- No health check endpoints
- No custom metrics collection

### What Needs Research 🔍

**Metrics Strategy:**
1. **Technology Choice**:
   - OpenTelemetry (industry standard, vendor-neutral)?
   - Prometheus .NET client (simpler, Prometheus-specific)?
   - Application Insights (Azure-native)?
   - Custom metrics to PostgreSQL (query-based dashboards)?
   
2. **Metrics to Collect**:
   - **Queue Metrics**:
     - Redis queue depth (alerts pending)
     - Average queue wait time
     - Queue processing rate (alerts/minute)
   - **Execution Metrics**:
     - Orders placed (count, by direction)
     - Orders filled (count, by direction)
     - Orders rejected (count, by rejection reason)
     - Execution latency (alert received → order placed)
   - **Error Metrics**:
     - Alert processing errors (count, by error type)
     - Exchange API errors (count, by endpoint)
     - LLM provider errors (count, by error type)
     - Database errors (count, by operation)
   - **Business Metrics**:
     - Active trades (gauge)
     - Daily PnL (if position tracking added)
     - Invalidation hits (stop-loss triggered count)
     - Trade plan outcomes (count by outcome type)

3. **Tracing Requirements**:
   - Trace entire alert → execution flow?
   - Distributed tracing across API → Worker → Database?
   - Correlation IDs for request tracking?
   - Sampling rate (100%? 10%? Adaptive?)

4. **Dashboard Design**:
   - Grafana (requires Prometheus/InfluxDB)?
   - Custom web dashboard (query PostgreSQL)?
   - Cloud provider dashboards (Azure Monitor, AWS CloudWatch)?
   - Alert rules (queue depth > threshold, error rate spike)?

5. **Health Checks**:
   - ASP.NET Core Health Checks?
   - Check database connectivity?
   - Check Redis connectivity?
   - Check exchange API connectivity?
   - `/health` endpoint for container orchestrators?

### What Needs Implementation 🏗️

**Phase 1: Basic Metrics Collection**

**Add NuGet Packages:**
```xml
<PackageReference Include="OpenTelemetry" Version="1.7.0" />
<PackageReference Include="OpenTelemetry.Exporter.Prometheus.AspNetCore" Version="1.7.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.7.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.7.0" />
```

**Metrics Service:**
- New `IMetricsService` interface in `Mvp.Trading.Contracts`:
  ```csharp
  public interface IMetricsService
  {
      void RecordAlertProcessed(string outcome); // "accepted", "rejected", "error"
      void RecordExecutionOutcome(string outcome); // "filled", "placed", "rejected", "error"
      void RecordOrderPlaced(string direction, string orderType);
      void RecordExecutionLatency(TimeSpan latency);
      void RecordApiError(string provider, string endpoint);
      void SetActiveTradesGauge(int count);
      void SetQueueDepthGauge(int count);
  }
  ```

**Implementation:**
- `OpenTelemetryMetricsService.cs` using System.Diagnostics.Metrics API:
  ```csharp
  public sealed class OpenTelemetryMetricsService : IMetricsService
  {
      private readonly Meter _meter;
      private readonly Counter<long> _alertsProcessed;
      private readonly Counter<long> _executionsProcessed;
      private readonly Counter<long> _ordersPlaced;
      private readonly Histogram<double> _executionLatency;
      private readonly Counter<long> _apiErrors;
      private readonly ObservableGauge<int> _activeTrades;
      private readonly ObservableGauge<int> _queueDepth;
  }
  ```

**Integration Points:**
- `AlertWorker.cs` - call `RecordAlertProcessed()` after processing
- `ExecutionService.cs` - call `RecordExecutionOutcome()` after execution
- `KrakenFuturesTradingProvider.cs` - call `RecordApiError()` on API failures
- New background worker: `MetricsCollectionWorker.cs` - polls queue depth every 10s

**Phase 2: Distributed Tracing**

**Add Tracing Packages:**
```xml
<PackageReference Include="OpenTelemetry.Instrumentation.StackExchangeRedis" Version="1.0.0" />
<PackageReference Include="Npgsql.OpenTelemetry" Version="8.0.0" />
```

**Activity Sources:**
- Register ActivitySource in each service
- Instrument critical paths:
  - Alert ingestion → adjudication → execution → order placement
  - Market data retrieval
  - Database queries

**Correlation IDs:**
- Add `correlationId` to alert processing context
- Pass through entire pipeline
- Include in all log messages

**Phase 3: Health Checks**

**Add Health Check Package:**
```xml
<PackageReference Include="AspNetCore.HealthChecks.Redis" Version="7.0.0" />
<PackageReference Include="AspNetCore.HealthChecks.Npgsql" Version="7.0.0" />
```

**Configure in Program.cs:**
```csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres")
    .AddRedis(redisConnectionString, name: "redis")
    .AddCheck<ExchangeApiHealthCheck>("exchange-api");

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready"); // readiness probe
app.MapHealthChecks("/health/live");  // liveness probe
```

**Custom Health Checks:**
- `ExchangeApiHealthCheck.cs` - calls `CheckApiKeyAsync()` periodically
- `WorkerHealthCheck.cs` - verifies background workers running

**Phase 4: Prometheus + Grafana**

**Update docker-compose.yml:**
```yaml
services:
  prometheus:
    image: prom/prometheus:latest
    ports:
      - "9090:9090"
    volumes:
      - ./config/prometheus.yml:/etc/prometheus/prometheus.yml
  
  grafana:
    image: grafana/grafana:latest
    ports:
      - "3000:3000"
    volumes:
      - ./config/grafana/dashboards:/etc/grafana/provisioning/dashboards
```

**Prometheus Config:**
```yaml
scrape_configs:
  - job_name: 'mvp-trading-api'
    static_configs:
      - targets: ['api:8080']
  - job_name: 'mvp-trading-worker'
    static_configs:
      - targets: ['worker:8081']
```

**Grafana Dashboard Panels:**
1. Queue Metrics:
   - Queue depth over time (line chart)
   - Processing rate (alerts/min, line chart)
   - Average queue wait time (line chart)
2. Execution Metrics:
   - Orders placed per hour (bar chart)
   - Execution outcomes (pie chart: filled, rejected, error)
   - Execution latency P50/P95/P99 (line chart)
3. Error Metrics:
   - Error rate over time (line chart)
   - Error types breakdown (stacked bar chart)
   - API error rate by provider (line chart)
4. Business Metrics:
   - Active trades (gauge)
   - Trade outcomes (pie chart: win, loss, breakeven)
   - Daily trade volume (bar chart)

**Alert Rules (Grafana):**
- Queue depth > 100 for 5 minutes → Slack/email alert
- Error rate > 5% for 10 minutes → Slack/email alert
- No alerts processed in 1 hour → Slack/email alert
- Heartbeat stale → Slack/email alert

---

## Implementation Priority

### Critical Path (Must Have for M7 Completion):
1. **M7.1 Reconciliation Loop** - Production-critical for detecting execution drift
2. **M7.2 Kill Switch** - Safety-critical for halting trading in emergencies
3. **M7.3 Basic Metrics** - Phase 1 (metrics collection + Prometheus endpoint)
4. **M7.2 Chaos Tests** - Validation of fail-closed behavior

### Nice to Have (Can Defer):
- M7.3 Phase 2 (distributed tracing) - useful but not blocking
- M7.3 Phase 4 (Grafana dashboards) - can use Prometheus query UI initially
- M7.1 Auto-remediation - start with detection only, manual remediation

### Research Priority:
1. **M7.1**: Reconciliation strategy and drift scenarios (2-4 hours research)
2. **M7.2**: Kill switch trigger mechanism (1-2 hours research)
3. **M7.3**: Metrics technology choice (1 hour research, likely OpenTelemetry)
4. **M7.3**: Dashboard design (2 hours prototyping)

---

## Open Questions

**M7.1 Reconciliation:**
- ❓ Does Kraken Futures API provide position query endpoint (beyond open orders)?
- ❓ Should we reconcile on every execution or on background poll?
- ❓ What is acceptable latency between execution and reconciliation?

**M7.2 Kill Switch:**
- ❓ Should kill switch immediately cancel open orders or just pause new ones?
- ❓ Who has authority to activate kill switch (API key required? manual SQL only)?
- ❓ Should there be multiple kill switch levels (pause vs full stop)?

**M7.3 Metrics:**
- ❓ Is OpenTelemetry overkill for MVP or is it worth the setup cost?
- ❓ Should metrics be push (to collector) or pull (Prometheus scrape)?
- ❓ Do we need custom dashboards or is Prometheus query UI sufficient initially?

**General:**
- ❓ What is the target "done when" metric for M7? (All 3 stories complete? Subset?)
- ❓ Is there a production deployment target date driving M7 timeline?
- ❓ Should M7 include load testing (stress test with high alert volume)?

---

## Next Steps

1. **Review this document** - confirm research priorities and implementation approach
2. **Answer open questions** - make design decisions before coding
3. **Create M7.1 implementation plan** - detailed reconciliation loop design
4. **Spike: Reconciliation strategy** - research Kraken API position endpoints
5. **Spike: Kill switch mechanism** - prototype database flag approach
6. **Spike: OpenTelemetry setup** - add packages and basic metrics
7. **Update backlog** - break M7 stories into sub-tasks with estimates

---

## Success Criteria

**M7.1 Complete When:**
- ✅ Reconciliation loop running in background (configurable interval)
- ✅ Detects and logs discrepancies between internal state and exchange
- ✅ Updates `reconciliation_state` table with findings
- ✅ Integration test verifies detection of orphaned orders
- ✅ No auto-remediation (detection only)

**M7.2 Complete When:**
- ✅ Kill switch API endpoint implemented (activate/deactivate/status)
- ✅ Kill switch checked before alert processing and execution
- ✅ Manual test: activate kill switch, verify no new executions
- ✅ Chaos test suite runs 8+ failure scenarios
- ✅ All chaos tests verify fail-closed behavior (no unintended executions)

**M7.3 Complete When:**
- ✅ OpenTelemetry metrics collection implemented (Phase 1)
- ✅ `/metrics` endpoint exposes Prometheus-compatible metrics
- ✅ Health check endpoints (`/health`, `/health/ready`, `/health/live`)
- ✅ Prometheus scraping configured in docker-compose
- ✅ At least 10 key metrics instrumented (queue, execution, errors)
- ✅ Documentation of available metrics and dashboard examples

**M7 Milestone Complete When:**
- ✅ All three stories above meet completion criteria
- ✅ Documentation updated (README, operational runbook)
- ✅ "Done when" from backlog met: "failures fail closed; dashboards and alerts defined"
