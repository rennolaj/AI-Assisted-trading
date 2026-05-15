# ADR-017 — Backup and Recovery Plan

| Field | Value |
|-------|-------|
| **Status** | PROPOSED (Informational) |
| **Milestone** | M18.9 |
| **Date** | 2025-01 |
| **Author** | Architecture Review |
| **Deciders** | Project Owner |
| **MCSB v2 Domains** | BR-1, BR-2, BR-3 (Backup and Recovery) |
| **Note** | This ADR is primarily informational — it documents the backup strategy and RTO/RPO targets. Implementation is mostly configuration changes, not code changes. |

---

## Context

The system manages the following stateful data:

| Data Store | Contents | Loss Impact |
|------------|----------|-------------|
| PostgreSQL | Open trades, execution intents, order receipts, reconciliation records, kill switch state | **HIGH** — loss of open_trades means unknown position state; financial risk |
| Redis | Alert queue (`mvp:alerts`), in-flight signal queue | **MEDIUM** — data is transient; lost alerts require replay from TradingView; no historical data stored |
| Prometheus volumes | Metrics time-series (docker volume `prometheus-data`) | **LOW** — observability only; not business-critical |
| Grafana volumes | Dashboard configuration (docker volume `grafana-data`) | **LOW** — can be recreated from config files in repo |

Current backup status:
- **PostgreSQL**: Azure PostgreSQL Flexible Server has **automatic PITR backups enabled by default on all tiers** (Burstable, General Purpose, Memory Optimized — there is no "Basic" tier). The default retention is 7 days. No explicit configuration is needed to enable backups, but retention period and geo-redundancy should be reviewed for production.
- **Redis**: Azure Cache for Redis Basic SKU does **not support data persistence or backup**. All Redis data is lost on restart or failover.
- **Prometheus/Grafana**: Docker volumes with no backup.

This violates MCSB v2 BR-1 ("Ensure regular automated backups") and creates financial risk from trade position data loss.

---

## Decision

**Enable automatic backups on Azure PostgreSQL Flexible Server (7-day retention). Accept that Redis has no backup (transient data by design). Document RTO/RPO targets and a restore runbook. Evaluate Redis SKU upgrade for persistence if queue reliability requirements increase.**

---

## RTO / RPO Targets

| Component | RPO (data loss tolerance) | RTO (restore time target) | Approach |
|-----------|--------------------------|--------------------------|----------|
| PostgreSQL | 1 hour | 4 hours | PITR from automated backup |
| Redis | 1 hour (queue drain window) | 30 minutes | Restart + replay from TradingView |
| Application containers | 0 minutes (stateless) | 15 minutes | ACR redeploy |
| Prometheus/Grafana | 7 days | 2 hours | Config from repo; data rebuild |

**PostgreSQL RPO of 1 hour** means at most 1 hour of trade history could be lost. Given that the KillSwitch table must be accurate, the restore procedure includes a reconciliation step to verify open positions against Kraken Futures actual positions before re-enabling trading.

---

## PostgreSQL Backup Configuration

### Azure Automatic Backup

```hcl
resource "azurerm_postgresql_flexible_server" "pg" {
  # ...
  
  backup_retention_days        = 7     # minimum for compliance; 35 days max
  geo_redundant_backup_enabled = false # enable for production-critical; adds ~50% cost
  
  high_availability {
    mode                      = "SameZone"  # HA replica for failover; requires General Purpose SKU
    standby_availability_zone = "2"
  }
}
```

**Note**: High Availability (HA) requires moving from the Burstable SKU (lowest cost) to General Purpose — adds ~$50-100/month. For MVP with live Kraken trading, this is recommended. For demo-only, accept single-zone risk.

### Point-in-Time Restore (PITR)

Azure PostgreSQL Flexible Server backs up:
- Full backup: weekly
- Differential backup: daily
- Transaction log backup: every 5-15 minutes

PITR allows restore to any point within the retention window (7 days). RPO is effectively ~15 minutes (transaction log frequency), better than our 1-hour target.

---

## Redis: Accept No Backup at Basic SKU

Redis Basic SKU does not support:
- RDB (point-in-time snapshots)
- AOF (append-only file persistence)
- Geo-replication
- Zone redundancy
- Private endpoints (see ADR-011)

**Current Redis contents**: The `mvp:alerts` queue holds TradingView alerts waiting to be processed by `AlertWorker`. In normal operation, the queue drains within seconds. Worst-case loss scenario: Redis restarts while alerts are in queue → those alerts are lost → missed trade signal.

**Mitigation for MVP**:
1. AlertWorker processes alerts promptly (designed for low-latency processing)
2. TradingView can re-send alerts if the webhook endpoint returns 5xx (but TradingView does not auto-retry)
3. Operational procedure: if Redis restart is planned, drain the queue first (verify empty)

**If queue reliability becomes critical**: Upgrade to Standard/Premium SKU (~$50-200/month) which supports RDB persistence. Alternatively, replace Redis queue with Azure Service Bus (durable, dead-letter queue, guaranteed delivery) — evaluated in M19 if reliability requirements grow.

---

## Restore Runbook (PostgreSQL)

```markdown
# PostgreSQL Restore Procedure

## When to use:
- Data corruption detected in PostgreSQL tables
- Server failure with no HA replica available
- Accidental DROP of table or DELETE without WHERE

## Prerequisites:
- Azure CLI access with Contributor role on resource group
- Kraken Futures credentials available (for position reconciliation)
- KillSwitch MUST be in EMERGENCY_STOP state before and during restore

## Steps:

### 1. Activate KillSwitch immediately
POST /api/kill-switch/activate
Body: {"level": "EmergencyStop", "reason": "DB restore in progress"}
Confirm: No new trades will execute

### 2. Identify restore target time
az postgres flexible-server show \
  --name aiassist26-postgres \
  --resource-group aiassist26-rg \
  --query "backup.earliestRestoreDate"

# Choose restore point (use PITR timestamp, e.g. "2025-01-15T10:00:00Z")

### 3. Initiate Point-in-Time Restore
az postgres flexible-server restore \
  --name aiassist26-postgres-restored \
  --resource-group aiassist26-rg \
  --source-server aiassist26-postgres \
  --restore-time "2025-01-15T10:00:00Z"

# Wait 15-30 minutes for restore to complete

### 4. Verify restored data
# Connect to restored server and verify:
SELECT COUNT(*) FROM open_trades;
SELECT * FROM kill_switch_status ORDER BY updated_at DESC LIMIT 5;

### 5. Reconcile with Kraken Futures
# Compare open_trades in restored DB against actual positions in Kraken:
# GET https://futures.kraken.com/derivatives/api/v3/openpositions
# For each discrepancy: manually update DB to match actual positions

### 6. Switch application to restored server
# Update App Service configuration: Postgres__ConnectionString → new server hostname
# Or: promote restored server using DNS CNAME swap

### 7. Verify application health
# Check /health endpoint on API
# Verify AlertWorker is processing test alert
# Confirm KillSwitch is correctly loaded from DB

### 8. Re-enable trading
POST /api/kill-switch/deactivate
Body: {"reason": "Restore verified — reconciliation complete"}
```

---

## Grafana Dashboard Backup

Grafana dashboards are provisioned from files in `config/grafana/`:

```
config/grafana/provisioning/datasources/   — Prometheus data source config
config/grafana/provisioning/dashboards/    — Dashboard provisioning config
config/grafana/dashboards/                 — Dashboard JSON definitions
```

These are committed to the repository — dashboards are effectively version-controlled and recoverable at any time. No additional backup needed for Grafana configuration.

The `grafana-data` Docker volume contains:
- User preferences and sessions (low value)
- Grafana internal SQLite database (low value)

**Decision**: Accept loss of `grafana-data` volume on restart. Dashboard configuration is in git.

---

## Consequences

### Positive
- **Trade position safety**: 7-day PITR protects against data corruption and accidental deletion
- **RPO < 15 minutes**: Transaction log backups mean minimal data loss even on server failure
- **Runbook documented**: Clear procedure prevents panic-driven mistakes during an incident
- **MCSB compliance**: satisfies BR-1, BR-2, BR-3
- **Grafana configuration in git**: no additional backup needed for monitoring dashboards

### Negative / Tradeoffs
- **Redis data loss accepted**: For MVP, acceptable. Must document and communicate operational risk.
- **HA requires SKU upgrade**: Moving to General Purpose PostgreSQL doubles DB cost (~$50/month extra for smallest General Purpose instance). For live production trading, this cost is justified.
- **No cross-region backup**: `geo_redundant_backup_enabled = false` — region outage would require restore from backup, not failover. Acceptable for hobby/MVP level.

### Neutral
- PostgreSQL automatic backups are charged at 1x provisioned storage for free, additional storage at standard rates. At MVP scale, backup storage cost is negligible.

---

## Alternatives Considered

### A1: pg_dump to Azure Blob Storage (application-managed backup)
- Script runs `pg_dump` daily, uploads to Blob Storage
- More control over backup timing and format
- Requires additional infrastructure (Azure Blob, backup script, scheduled task)
- **Rejected**: Azure PITR is superior (continuous, automated, platform-managed) with no additional code

### A2: Replace Redis queue with Azure Service Bus
- Azure Service Bus has durable queues with dead-letter, guaranteed delivery, long retention
- Eliminates the Redis backup concern entirely for the queue use case
- Additional cost: ~$10/month for Basic tier
- **Deferred to M19**: evaluate if TradingView alert delivery reliability becomes a documented problem

### A3: Enable geo-redundant backup from day one
- Protects against full Azure region outage
- 50% cost premium on backup storage
- **Deferred**: region outage is an extreme scenario for MVP; enable when moving to production-grade

---

## References
- MCSB v2 Backup and Recovery: https://learn.microsoft.com/en-us/security/benchmark/azure/mcsb-v2-backup-recovery
- Azure PostgreSQL Flexible Server backup: https://learn.microsoft.com/en-us/azure/postgresql/flexible-server/concepts-backup-restore
- Azure Cache for Redis persistence: https://learn.microsoft.com/en-us/azure/azure-cache-for-redis/cache-how-to-premium-persistence
- Azure PostgreSQL PITR: https://learn.microsoft.com/en-us/azure/postgresql/flexible-server/how-to-restore-server-portal
