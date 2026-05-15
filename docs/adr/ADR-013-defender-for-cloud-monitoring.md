# ADR-013 — Microsoft Defender for Cloud and Centralized Monitoring

| Field | Value |
|-------|-------|
| **Status** | PROPOSED |
| **Milestone** | M18.5 |
| **Date** | 2025-01 |
| **Author** | Architecture Review |
| **Deciders** | Project Owner |
| **MCSB v2 Domains** | LT-1, LT-2, LT-3, LT-4 (Logging & Threat Detection); PV-1, PV-5 (Posture & Vulnerability) |
| **Depends on** | ADR-009, ADR-010, ADR-011 (foundational security must be in place before CSPM is meaningful) |

---

## Context

Current monitoring state:
- **Local**: Prometheus + Grafana collecting .NET metrics from API and Worker (port 9464)
- **Azure**: State backup indicates API on App Service and Worker/monitoring/Ollama on ACI, with no diagnostic settings configured, no Log Analytics workspace, and no Defender for Cloud
- **Application logging**: `ILogger<T>` → console output → App Service/ACI logs, currently ephemeral unless diagnostics are configured
- **Alerting**: No Azure Monitor alerts configured; no incident response path

Gaps against MCSB v2:
- **LT-1**: No centralized log collection (logs are ephemeral in App Service/ACI without diagnostics)
- **LT-2**: No threat detection on PostgreSQL or Redis
- **LT-3**: No security information and event management (SIEM) integration
- **LT-4**: No network flow logging
- **PV-1**: No CSPM posture score — unknown security baseline
- **PV-5**: No container vulnerability assessment in ACR

Microsoft Defender for Cloud provides CSPM (Cloud Security Posture Management) with a free Foundational tier and paid Defender plans for specific resource types.

---

## Decision

**Enable Microsoft Defender for Cloud Foundational CSPM (free) on the Azure subscription. Enable paid Defender plans for Containers and Databases. Create a Log Analytics workspace and connect App Service, ACI/container, PostgreSQL, Key Vault, and network diagnostic logs to it. Configure critical alerts for trading-safety events.**

---

## Defender for Cloud Plan Selection

| Plan | Cost | Coverage | Decision |
|------|------|----------|----------|
| Foundational CSPM | Free | Security recommendations, MCSB score | ✅ Enable |
| Defender for Servers | ~$15/server/month | VM threat detection | ❌ Not applicable (App Service) |
| Defender for Containers | ~$7/core/month | ACR vulnerability scan, runtime protection | ✅ Enable |
| Defender for Databases | ~$15/server/month | PostgreSQL threat detection, anomalous query detection | ✅ Enable |
| Defender for App Service | ~$15/app/month | App Service threat detection, malicious domain detection | ✅ Enable |
| Defender CSPM (paid) | ~$0.007/resource/hour | Attack path analysis, agentless scanning | ❌ Defer to M19 |

Estimated monthly cost for selected plans:
- Defender for Containers: depends on the final M18.0 workload hosting decision and vCPU allocation for ACR/ACI or replacement container host (review actual billing before enabling paid plan)
- Defender for Databases: ~$15/month (one Postgres server)
- Defender for App Service: applies to the API App Service; additional cost applies only if Worker migrates to App Service
- **Total Defender additions: variable; Foundational CSPM is free, Defender for Databases is the lowest paid baseline**

This is a meaningful cost for a hobby project. The project owner should evaluate whether Defender for Containers and App Service are required for MVP or only Foundational CSPM + Defender for Databases (lower risk, lower cost at ~$15/month added).

---

## Log Analytics Architecture

```
Sources:
  App Service (API)     → Diagnostic settings → Log Analytics Workspace
  ACI (Worker)          → Container logs / diagnostic settings → Log Analytics Workspace
  ACI (Monitoring/Ollama) → Container logs / diagnostic settings → Log Analytics Workspace
  PostgreSQL Flexible   → Diagnostic settings → Log Analytics Workspace
  Key Vault             → Diagnostic settings → Log Analytics Workspace
  NSG flow logs         → Storage Account → Log Analytics Workspace

Log Analytics Workspace: aiassist26-law
  Retention: 30 days (free tier default)
  SKU: PerGB2018

Alerts (Azure Monitor):
  1. KillSwitch DB failure → severity 0 → email + webhook
  2. PostgreSQL admin connection from outside VNet → severity 1 → email
  3. Redis AUTH failures > 10/minute → severity 1 → email
  4. App Service 5xx rate > 5% → severity 2 → email
  5. Container vulnerability: critical CVE in ACR image → severity 1 → email
```

---

## Trading-Critical Alerts

These alerts are specific to the trading server's fail-safe requirements:

### Alert 1: KillSwitch Database Failure (Severity 0 — Financial Safety)

```kql
// Log Analytics query — fire when KillSwitch cannot reach DB
AppExceptions
| where ExceptionType == "Npgsql.NpgsqlException"
| where Properties["SourceContext"] contains "KillSwitchService"
| where TimeGenerated > ago(5m)
| summarize count() by bin(TimeGenerated, 1m)
| where count_ > 3
```

**Why**: KillSwitch failing open (M14.8.3 / KS-1 anti-pattern) means trades execute when they shouldn't. Alert must fire before a second wave of false-positive trades can be placed.

### Alert 2: Abnormal Trade Execution Volume (Severity 1)

```kql
AppTraces
| where Message contains "ExecutionIntent submitted"
| where TimeGenerated > ago(15m)
| summarize count() by bin(TimeGenerated, 15m)
| where count_ > 5   // more than 5 trades in 15 minutes is abnormal
```

---

## Prometheus → Azure Monitor Integration

The existing Prometheus setup (port 9464 on Worker) can be bridged to Azure Monitor via the Azure Monitor managed service for Prometheus (part of Azure Monitor workspace), allowing Grafana to query both local metrics and Azure Monitor from a single data source.

For M18, a simpler approach is acceptable: keep Prometheus + Grafana local, and use Log Analytics for security events only.

---

## Consequences

### Positive
- **CSPM score visible**: baseline posture score measurable; improvement trackable across milestones
- **PostgreSQL threat detection**: Defender for Databases detects SQL injection, anomalous access patterns, brute force — relevant for trading data
- **ACR scan**: Defender for Containers scans ACR images on push — new critical CVE blocks before deployment
- **MCSB compliance**: satisfies LT-1, LT-2, LT-3, PV-1, PV-5
- **Audit retention**: 30 days Log Analytics retention (vs ephemeral App Service log stream)
- **Trading safety**: KillSwitch failure alert provides fail-safe alerting for M14.8.3 anti-pattern

### Negative / Tradeoffs
- **Cost**: ~$101/month for full Defender suite; ~$15/month for minimal (Databases only)
- **Alert fatigue**: Must tune alert thresholds carefully — too sensitive causes noise, too loose misses real threats
- **Log Analytics ingestion cost**: PostgreSQL pgaudit logs can be verbose; filter before ingesting to control cost

### Neutral
- Foundational CSPM is free and has no downside — enable unconditionally
- Diagnostic settings take ~5 minutes to activate after configuration

---

## Implementation Notes

### Terraform Resources

```hcl
resource "azurerm_log_analytics_workspace" "law" {
  name                = "${var.prefix}-law"
  location            = var.location
  resource_group_name = azurerm_resource_group.rg.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
}

resource "azurerm_security_center_subscription_pricing" "containers" {
  tier          = "Standard"
  resource_type = "Containers"
}

resource "azurerm_security_center_subscription_pricing" "databases" {
  tier          = "Standard"
  resource_type = "OpenSourceRelationalDatabases"  # covers PostgreSQL, MySQL, MariaDB Flexible Server
}

resource "azurerm_monitor_diagnostic_setting" "api" {
  name                       = "api-diagnostics"
  target_resource_id         = azurerm_linux_web_app.api.id
  log_analytics_workspace_id = azurerm_log_analytics_workspace.law.id

  enabled_log { category = "AppServiceHTTPLogs" }
  enabled_log { category = "AppServiceConsoleLogs" }
  enabled_log { category = "AppServiceAppLogs" }
}
```

---

## Alternatives Considered

### A1: Elastic Stack (ELK) for log aggregation
- Open source, self-hosted, more customizable
- Requires additional infrastructure (Elasticsearch cluster)
- **Rejected**: increases operational burden; Log Analytics is sufficient and natively integrated

### A2: Azure Sentinel (Microsoft Sentinel) as SIEM
- Full SIEM with ML-based threat detection
- Cost: ~$2.46/GB ingested — significantly more expensive
- **Deferred**: overkill for MVP; consider if trading volume grows significantly

### A3: Skip Defender for Cloud entirely, use Grafana alerts only
- Grafana is already in place for metrics
- Cannot scan container images; no CSPM score; no database threat detection
- **Rejected**: MCSB compliance requires CSPM at minimum; Foundational tier is free

---

## References
- MCSB v2 Logging and Threat Detection: https://learn.microsoft.com/en-us/security/benchmark/azure/mcsb-v2-logging-threat-detection
- Defender for Cloud plans: https://learn.microsoft.com/en-us/azure/defender-for-cloud/defender-for-cloud-introduction
- Log Analytics workspace: https://learn.microsoft.com/en-us/azure/azure-monitor/logs/log-analytics-workspace-overview
- KQL reference: https://learn.microsoft.com/en-us/azure/data-explorer/kql-quick-reference
