# ADR-009 — Managed Identity and Keyless Authentication for Azure Services

| Field | Value |
|-------|-------|
| **Status** | PROPOSED |
| **Milestone** | M18.1 |
| **Date** | 2025-01 |
| **Author** | Architecture Review |
| **Deciders** | Project Owner |
| **MCSB v2 Domains** | IM-1, IM-2, IM-3 (Identity Management) |

---

## Context

The current Azure deployment artifacts (`terraform.tfvars`, `parameters.local.json`, and local state backup) indicate service credentials are passed as environment variables:

- PostgreSQL: `postgres_admin` + `postgres_password` in env
- Redis: connection string with password in env
- ACR: admin credentials for image pull
- OpenAI: API key in env
- Kraken: API keys in env

This means the application containers run with long-lived static secrets that:
1. Exist as plaintext in local files (gitignored but not encrypted)
2. Cannot be automatically rotated without redeployment
3. Provide no audit trail for which service used which credential when
4. Violate MCSB v2 IM-1 ("Use centralized identity system") and IM-3 ("Managed identities for Azure resource authentication")

Microsoft's guidance in the Secure Future Initiative (SFI) Pillar 1 ("Protect Identities and Secrets") mandates that service-to-service authentication within Azure should use Managed Identities — token-based auth that is automatically rotated by the Azure platform.

### Current State Map

```
App Service (API)   → Postgres:  connection string with password
App Service (API)   → Redis:     connection string with password
App Service (API)   → ACR:       not using managed identity for image pull
ACI (Worker)        → Postgres:  connection string with password
ACI (Worker)        → Kraken API: API key in environment variable
ACI (Worker)        → OpenAI API: API key in environment variable
ACI (Monitoring)    → Grafana/admin secrets in environment variables
ACI (Ollama)        → container runtime configuration in environment variables
```

### Azure Services That Support Managed Identity Auth

| Service | Auth Method | SDK Support |
|---------|-------------|-------------|
| Azure Database for PostgreSQL Flexible Server | Entra ID token (no password) | Npgsql 8 `UseAzureADAuthentication` |
| Azure Cache for Redis | Entra ID with `azure-redis-cache` auth | `Microsoft.Azure.StackExchangeRedis` |
| Azure Container Registry | AcrPull role assignment | Built-in — no admin credentials |
| Azure Key Vault | Key Vault Secrets User role | `Azure.Security.KeyVault.Secrets` |

**Services that CANNOT use Managed Identity** (external APIs requiring key-based auth):
- Kraken Futures API — proprietary HMAC key auth, no identity federation
- OpenAI API — API key only (Azure OpenAI Service supports Managed Identity, but not OpenAI.com)
- TradingView webhook — incoming secret verification, not outgoing auth

---

## Decision

**Enable managed identity on every Azure-hosted workload and migrate all Azure-internal service connections to identity-based authentication.**

The observed baseline is mixed hosting: API on App Service and Worker/monitoring/Ollama on ACI. M18.0 must either keep that topology and configure identities for each Azure-hosted workload that requires Azure access, or deliberately migrate the Worker to another managed container host before implementing this ADR.

Credentials that cannot be eliminated (Kraken, OpenAI.com, TradingView webhook) will be stored in Azure Key Vault and referenced via Key Vault references (see ADR-010). App Service resolves those references into runtime environment variables, so the application still receives plaintext secret values at runtime, but the values are not hardcoded in source, App Service settings, or IaC.

---

## Architecture After This ADR

```
App Service (API)    [Managed Identity: aiassist26-api-identity]
  ├─→ Postgres:      Entra ID token auth (Npgsql AzureADAuthentication)
  ├─→ Redis:         Entra ID auth (Microsoft.Azure.StackExchangeRedis)
  ├─→ Key Vault:     Key Vault Secrets User role → retrieves remaining secrets
  └─→ ACR:           AcrPull role → no admin password

ACI or selected Worker host [Managed Identity: aiassist26-worker-identity]
  ├─→ Postgres:      Entra ID token auth
  ├─→ Redis:         Entra ID auth
  └─→ Key Vault:     Key Vault Secrets User role → retrieves remaining secrets

ACI monitoring/Ollama workloads only receive identities and Key Vault access if they need Azure resources or external secrets at runtime.
```

---

## Consequences

### Positive
- **Zero-rotation secrets**: Entra ID tokens rotate automatically (1-hour lifetime by default)
- **Audit trail**: Every Azure service call is logged with the identity that made it
- **Compliance**: Satisfies MCSB v2 IM-1, IM-2, IM-3, IM-8; SFI Pillar 1
- **Attack surface reduction**: Compromised container cannot leak reusable long-lived passwords
- **ACR admin disabled**: Removes deprecated credential class from the deployment

### Negative / Tradeoffs
- **Local dev complexity**: `DefaultAzureCredential` requires Azure CLI login or environment for local development. docker-compose must use service principal or connection-string fallback for local.
- **Npgsql upgrade risk**: Entra ID auth in Npgsql requires version 8 (currently on 8?) — confirm compatibility
- **Redis Entra auth availability**: Only available on Premium+ SKU for full support; Standard/Basic have limited Entra auth. May require SKU upgrade (additional cost).
- **IaC source required first**: Role assignments and identity configuration must be added to the selected Terraform or Bicep source. Since no `.tf` or `.bicep` source exists yet, M18.0 is a hard blocker.

### Neutral
- Application code changes are minimal: `UseAzureADAuthentication()` in Npgsql DI registration; `DefaultAzureCredential` for Redis client. The connection string format changes (no `Password=...`), but the code pattern stays the same.

---

## Implementation Notes

### PostgreSQL Prerequisites

PostgreSQL passwordless runtime authentication requires Azure setup before application rollout:
- Enable Microsoft Entra authentication on Azure Database for PostgreSQL Flexible Server.
- Configure a Microsoft Entra administrator.
- Create database principals for the API managed identity and the Worker managed identity/container identity from the `postgres` database:

```sql
select * from pgaadauth_create_principal('<api-managed-identity-name>', false, false);
select * from pgaadauth_create_principal('<worker-managed-identity-name>', false, false);
```

Least-privilege table grants are then assigned to those managed identity principals. A password-backed `appuser` role is acceptable only for local docker-compose development.

### C# Code Pattern (Postgres — Npgsql with Entra ID)

```csharp
// In Program.cs / DI registration — use IHostEnvironment for robust detection
// Prefer an explicit config flag over env var presence checks to avoid false positives
// (AZURE_CLIENT_ID may be legitimately set in local dev for other reasons)

// Option A: Explicit configuration flag (recommended)
var useEntraId = builder.Configuration.GetValue<bool>("Azure:UseEntraId");

services.AddNpgsqlDataSource(connectionString, npgsqlBuilder =>
{
    if (useEntraId)  // Set Azure:UseEntraId=true in Azure App Service app settings only
    {
        npgsqlBuilder.UseAzureADAuthentication(new DefaultAzureCredential());
    }
});

// appsettings.json (default, committed):  "Azure": { "UseEntraId": false }
// Azure App Service app settings (not committed): Azure__UseEntraId = true
```

### C# Code Pattern (Redis — Microsoft.Azure.StackExchangeRedis)

```csharp
// NuGet: Microsoft.Azure.StackExchangeRedis (version 1.5.0 — requires Redis Standard SKU or higher)
var configOptions = await ConfigurationOptions.Parse(redisHostname).ConfigureForAzureWithTokenCredentialAsync(
    new DefaultAzureCredential());
var connection = await ConnectionMultiplexer.ConnectAsync(configOptions);
```

### Terraform Resource (identity assignment)

```hcl
# ✅ CORRECT — identity block is INSIDE azurerm_linux_web_app, not a separate resource
resource "azurerm_linux_web_app" "api" {
  # ... other config ...

  identity {
    type = "SystemAssigned"   # platform rotates the token automatically
  }
}

# Reference principal_id from the web app identity block
resource "azurerm_role_assignment" "api_acr_pull" {
  scope                = azurerm_container_registry.acr.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_linux_web_app.api.identity[0].principal_id
}

resource "azurerm_role_assignment" "api_kv_secrets_user" {
  scope                = azurerm_key_vault.kv.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_linux_web_app.api.identity[0].principal_id
}
```

### Local Development Fallback

For local docker-compose, keep the existing `Postgres__ConnectionString` with username/password — this is a development-only credential, not production. Use `DefaultAzureCredential` which falls back to environment variables or Azure CLI auth automatically.

Do not add AZURE_CLIENT_ID to docker-compose — absence of that variable signals local mode and the code uses connection-string auth as today.

---

## Alternatives Considered

### A1: Service Principal with client secret stored in Key Vault
- Service principal provides identity, but secret still needs rotation
- Managed Identity is strictly superior for Azure-internal communication
- **Rejected**: unnecessary complexity when Managed Identity is available

### A2: Azure Workload Identity Federation (for GitHub Actions)
- Would allow CI/CD pipeline to access Azure without secrets
- Out of scope for M18 but recommended for M18.6 (DevOps security)
- **Deferred to ADR-014 (DevOps Security)**

### A3: Keep API keys but rotate via Key Vault references
- Simpler to implement (no code changes for Postgres/Redis)
- Doesn't eliminate the secret — just makes it easier to rotate
- **Rejected for Azure-internal services**: Managed Identity is available and preferred. Accepted for external APIs (Kraken, OpenAI) where no identity federation exists — handled in ADR-010.

---

## References
- MCSB v2 Identity Management: https://learn.microsoft.com/en-us/security/benchmark/azure/mcsb-v2-identity-management
- Npgsql Azure AD authentication: https://www.npgsql.org/doc/security.html#azure-managed-identity
- Microsoft.Azure.StackExchangeRedis: https://github.com/Azure/Microsoft.Azure.StackExchangeRedis
- SFI Pillar 1 — Protect identities and secrets: https://www.microsoft.com/en-us/security/blog/2024/05/03/security-for-the-future/
