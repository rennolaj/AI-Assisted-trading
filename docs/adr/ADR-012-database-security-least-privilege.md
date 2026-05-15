# ADR-012 — Database Security: Least Privilege and Encryption Hardening

| Field | Value |
|-------|-------|
| **Status** | PROPOSED |
| **Milestone** | M18.4 |
| **Date** | 2025-01 |
| **Author** | Architecture Review |
| **Deciders** | Project Owner |
| **MCSB v2 Domains** | PA-1, PA-7 (Privileged Access); DP-1, DP-4 (Data Protection) |
| **Depends on** | ADR-009 (Managed Identity for DB auth), ADR-011 (VNet/private endpoint for DB access) |

---

## Context

Current database access pattern:
- Application connects as `pgadmin` (the PostgreSQL admin account) with password `TestDeploy2026!` (from terraform.tfvars)
- Admin account has full DDL rights: can DROP tables, CREATE roles, modify schema, etc.
- PostgreSQL Flexible Server default configuration: TLS not enforced by application-level setting (TLS is on by default in Azure, but `require_ssl` should be explicit)
- No database activity logging (`pgaudit` extension not enabled)
- No least-privilege DB user for the application

This violates MCSB v2 PA-1 ("Separate and limit highly privileged/administrative users") and PA-7 ("Follow just enough administration principle").

In a live trading context, if the application is compromised via a code injection attack, an admin DB user allows the attacker to:
1. DROP the `open_trades` table (financial loss)
2. CREATE a new admin-level role
3. Extract all historical trade data and API keys stored in plaintext columns

---

## Decision

**Create an application-specific PostgreSQL role with minimum required privileges (DML only: SELECT, INSERT, UPDATE, DELETE on specific tables). The application connection string will use this role. The `pgadmin` admin account will only be used for schema migrations and is never accessible from application containers.**

Additionally, enable `pgaudit` for SQL activity logging, enforce TLS at the server level, and verify Transparent Data Encryption (TDE) is active.

---

## Database Privilege Model

### Application Role (`appuser`)

```sql
-- Create application role (run once during DB initialization)
CREATE ROLE appuser WITH LOGIN PASSWORD 'managed-by-entra-or-kv';

-- Grant DML on all application tables (expand as schema grows)
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE
    alerts,
    open_trades,
    execution_intents,
    order_receipts,
    reconciliation_records,
    kill_switch_status,
    trade_reviews          -- future: ADR-006 post-trade loop
TO appuser;

-- Grant usage on sequences (for INSERT with serial columns)
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO appuser;

-- Explicitly revoke DDL rights
REVOKE CREATE ON SCHEMA public FROM appuser;
REVOKE ALL ON SCHEMA public FROM PUBLIC;

-- Deny admin functions
REVOKE pg_execute_server_program FROM appuser;
```

### Migration Role (`migrator`)

Used only by migration tooling (Flyway/DbUp/EF migrations), never by application containers:

```sql
CREATE ROLE migrator WITH LOGIN;
GRANT CONNECT ON DATABASE "ai-trading-db" TO migrator;
GRANT ALL ON SCHEMA public TO migrator;  -- DDL rights for schema changes only
```

### Admin Role (`pgadmin`)

Admin role kept for emergency DBA access only. Never used in connection strings. Accessed only via Azure Bastion or CLI after authentication via Entra ID.

---

## pgaudit Configuration

```sql
-- Enable pgaudit extension (requires server restart or Azure portal parameter)
CREATE EXTENSION IF NOT EXISTS pgaudit;

-- Audit all DDL, DML, and role changes for the application user
ALTER ROLE appuser SET pgaudit.log = 'read,write,ddl';
ALTER ROLE migrator SET pgaudit.log = 'ddl,role';

-- Server parameter (set in Azure PostgreSQL Flexible Server settings):
-- shared_preload_libraries = 'pgaudit'
-- pgaudit.log = 'write,ddl'
-- pgaudit.log_catalog = off   (reduces noise)
```

Audit logs are shipped to Log Analytics workspace (M18.5).

---

## TLS Enforcement

```hcl
# In Terraform PostgreSQL resource (or equivalent Bicep):
resource "azurerm_postgresql_flexible_server" "pg" {
  # ...

  authentication {
    active_directory_auth_enabled = true   # Enables Entra ID auth (ADR-009)
    password_auth_enabled         = true   # Kept for migrator role; disable when migrator uses Entra ID
    tenant_id                     = data.azurerm_client_config.current.tenant_id
  }
}

resource "azurerm_postgresql_flexible_server_configuration" "require_ssl" {
  server_id = azurerm_postgresql_flexible_server.pg.id
  name      = "require_secure_transport"
  value     = "on"
}
```

Application connection string must include `Ssl Mode=Require` in Npgsql:
```csharp
// Ensure connection string has SSL mode set
// "Host=...;Port=5432;Database=ai-trading-db;Username=appuser;Ssl Mode=Require;"
```

---

## Transparent Data Encryption (TDE)

Azure Database for PostgreSQL Flexible Server enables TDE by default using platform-managed keys. No additional configuration is required. To verify:
- Azure portal → PostgreSQL Flexible Server → Security → Encryption shows "Service-managed key" ✅

For Customer-Managed Key (CMK) encryption, Azure Key Vault integration with Managed Identity would be required. This adds operational complexity (key rotation risk) and is not required for MVP level compliance. Document as a future consideration.

---

## Consequences

### Positive
- **Attack surface reduction**: Compromised app container cannot DROP tables or CREATE roles
- **Audit trail**: pgaudit logs every read/write/DDL for forensic analysis
- **MCSB compliance**: satisfies PA-1, PA-7, DP-1, DP-4
- **Defense-in-depth**: Even if SQL injection bypasses application logic, `appuser` cannot reach tables not in the GRANT list

### Negative / Tradeoffs
- **Schema migration complexity**: Migrations must run as `migrator`, not `appuser`. Docker build pipeline must handle two connection strings: one for schema management, one for runtime.
- **Initial setup burden**: `appuser` and `migrator` roles must be created before first application deployment. This requires a one-time admin operation (via `init.sql` or migration bootstrap script).
- **Local dev change**: `scripts/db/init.sql` must be updated to create `appuser` with appropriate grants for local development

### Neutral
- TDE is already active by default — this is a verification and documentation task, not a new configuration

---

## Implementation Notes

### Update `scripts/db/init.sql`

```sql
-- Add to existing init.sql after table creation:
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'appuser') THEN
        CREATE ROLE appuser WITH LOGIN PASSWORD 'appuser';
    END IF;
END
$$;

GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO appuser;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO appuser;
```

### Connection String Separation

Two environment variables in docker-compose and App Service config:
- `Postgres__ConnectionString` — uses `appuser`, DML only (application runtime)
- `Postgres__MigrationConnectionString` — uses `migrator` or `pgadmin`, DDL allowed (migration runs only)

The migration connection string is only present in CI/CD pipeline environment — not in production App Service config.

---

## Alternatives Considered

### A1: Row-Level Security (RLS)
- Provides per-row access control within a single role
- Powerful for multi-tenant isolation — not needed here (single-tenant system)
- **Deferred**: revisit if multi-user or multi-symbol isolation becomes relevant

### A2: Customer-Managed Key for TDE
- Provides full control over encryption key lifecycle
- Requires Key Vault integration at the storage layer (different from application Key Vault)
- Operational risk: if CMK becomes unavailable, database is permanently inaccessible
- **Deferred**: not required for MVP compliance; review for M19

### A3: Database firewall IP allowlist as primary protection
- Allow only App Service outbound IPs to connect to PostgreSQL
- Combined with private endpoint (ADR-011), IP allowlist adds no additional security
- **Superseded by ADR-011**: VNet private endpoint provides network isolation; privilege separation provides additional layer

---

## References
- MCSB v2 Privileged Access: https://learn.microsoft.com/en-us/security/benchmark/azure/mcsb-v2-privileged-access
- pgaudit documentation: https://www.pgaudit.org/
- PostgreSQL Flexible Server TDE: https://learn.microsoft.com/en-us/azure/postgresql/flexible-server/concepts-data-encryption
- Npgsql SSL: https://www.npgsql.org/doc/security.html#encryption-ssltls
