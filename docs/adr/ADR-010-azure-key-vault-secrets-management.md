# ADR-010 — Azure Key Vault for Secrets Management

| Field | Value |
|-------|-------|
| **Status** | PROPOSED |
| **Milestone** | M18.2 |
| **Date** | 2025-01 |
| **Author** | Architecture Review |
| **Deciders** | Project Owner |
| **MCSB v2 Domains** | DP-6, DP-7 (Data Protection); IM-8 (Identity Management) |
| **Depends on** | ADR-009 (Managed Identity must exist for Key Vault RBAC) |

---

## Context

Three categories of secrets exist in this system:

**Category 1 — Azure-internal (eliminatable via Managed Identity, ADR-009)**:
- PostgreSQL password → replaced by Entra ID token auth
- Redis password → replaced by Entra ID auth
- ACR admin password → replaced by AcrPull role assignment

**Category 2 — External API keys (cannot eliminate, must protect)**:
- `openai_api_key` — OpenAI.com API key
- `kraken_prod_api_key` / `kraken_prod_api_secret` — live trading credentials
- `kraken_demo_api_key` / `kraken_demo_api_secret` — demo environment
- `tradingview_webhook_secret` — HMAC verification for incoming webhooks
- `ngrok_authtoken` — tunnel for local webhook testing

**Category 3 — Infrastructure secrets**:
- `postgres_password` — still needed for initial DB setup / migrations even after Entra ID auth
- `grafana_admin_password` — monitoring dashboard credential

Currently all Category 2 and 3 secrets live as:
- Plaintext values in `terraform.tfvars` (gitignored, unencrypted on disk)
- Plaintext values in `parameters.local.json` (gitignored, unencrypted on disk)
- Environment variables injected at runtime (visible in `docker inspect`, process environ, App Service config blade)

This violates MCSB v2 DP-6 ("Store secrets in secrets management systems") and DP-7 ("Use certificates from centralized key management").

---

## Decision

**Provision an Azure Key Vault and use it as the single source of truth for all Category 2 and Category 3 secrets. Reference secrets in Azure App Service configuration via Key Vault references — no application code changes required.**

App Service Key Vault references work by injecting the secret value at container start time as an environment variable, but the value is fetched from Key Vault using the App Service's Managed Identity (ADR-009), not stored in the App Service configuration blade. This means:
- The secret value is not in Terraform state if secrets are populated out-of-band or imported with state protection explicitly documented
- The secret value is never visible in the Azure portal App Service configuration view
- Rotation only requires updating the Key Vault secret — no redeployment needed

---

## Key Vault Configuration

### Resource Specifications

```
Name:            aiassist26-kv
SKU:             Standard
Soft-delete:     enabled (90 days)
Purge protection: enabled
Permission model: Azure RBAC (not access policies)
Network:         private endpoint on data-subnet (see ADR-011)
Public network:  disabled after private endpoint active
```

### Secret Inventory

| Secret Name in Key Vault | Current Location | Category |
|--------------------------|------------------|----------|
| `openai-api-key` | terraform.tfvars | 2 |
| `kraken-prod-api-key` | terraform.tfvars | 2 |
| `kraken-prod-api-secret` | terraform.tfvars | 2 |
| `kraken-demo-api-key` | terraform.tfvars | 2 |
| `kraken-demo-api-secret` | terraform.tfvars | 2 |
| `tradingview-webhook-secret` | terraform.tfvars | 2 |
| `ngrok-authtoken` | terraform.tfvars | 3 |
| `postgres-admin-password` | terraform.tfvars | 3 |
| `grafana-admin-password` | terraform.tfvars | 3 |

### App Service Configuration Reference Format

```
# In App Service application settings (via Terraform):
OpenAI__ApiKey = "@Microsoft.KeyVault(SecretUri=https://aiassist26-kv.vault.azure.net/secrets/openai-api-key/)"
KrakenFutures__ProdApiKey = "@Microsoft.KeyVault(SecretUri=https://aiassist26-kv.vault.azure.net/secrets/kraken-prod-api-key/)"
```

The App Service resolves these references at startup — the application receives the plaintext value as a normal environment variable. No SDK changes in the application code.

---

## Consequences

### Positive
- **No runtime secrets in App Service settings**: Terraform creates App Service config using Key Vault reference URIs, not the secret values themselves
- **Terraform state can remain secret-free**: Only if Key Vault secret values are populated out-of-band or imported with explicit encrypted-state controls. Do not manage secret values with `azurerm_key_vault_secret.value` unless the state storage risk is accepted and documented.
- **Centralized rotation**: Updating a secret in Key Vault takes effect on next App Service restart (or within 24 hours via automatic refresh)
- **Access audit log**: Every Key Vault secret access is logged in Azure Monitor — full audit trail for Kraken API key usage
- **MCSB compliance**: Satisfies DP-6, DP-7, IM-8; SFI Pillar 1 (protect secrets)
- **Soft-delete + purge protection**: Prevents accidental or malicious secret deletion
- **Cost**: Key Vault Standard SKU — ~$0.03/10,000 secret operations; negligible for this workload

### Negative / Tradeoffs
- **Key Vault secret must be populated before App Service starts**: Terraform apply order matters — Key Vault and secrets must be provisioned before App Service settings are set. Use `depends_on` in Terraform.
- **Local development unaffected**: docker-compose continues to use environment variables. Developers must not use production Key Vault values locally — use demo credentials or local test values only.
- **Key Vault private endpoint (ADR-011) must be provisioned first for full isolation**: Without private endpoint, Key Vault is still accessible over public internet (but with RBAC protection). Acceptable during initial rollout.

### Neutral
- **Secret bootstrapping**: The initial secret values must be populated in Key Vault via a one-time manual operation or owner-run script that does not persist values in repo files. Terraform-managed secret values are allowed only if encrypted remote state, restricted state access, and the residual state risk are explicitly accepted.

---

## Implementation Notes

### Terraform Resource Pattern

```hcl
resource "azurerm_key_vault" "kv" {
  name                      = "${var.prefix}-kv"
  location                  = var.location
  resource_group_name       = azurerm_resource_group.rg.name
  sku_name                  = "standard"
  tenant_id                 = data.azurerm_client_config.current.tenant_id
  
  enable_rbac_authorization  = true
  soft_delete_retention_days = 90
  purge_protection_enabled   = true
  
  network_acls {
    default_action = "Deny"
    bypass         = "AzureServices"
  }
}

# Grant API Managed Identity read access
resource "azurerm_role_assignment" "api_kv_reader" {
  scope                = azurerm_key_vault.kv.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_linux_web_app.api.identity[0].principal_id
}
```

### Secret Population Rule

Populate secret values outside Terraform by default, for example with Azure CLI, Azure Portal, or a one-time owner-run script that does not persist values in repo files:

```bash
az keyvault secret set --vault-name aiassist26-kv --name openai-api-key --value "<rotated-value>"
```

If Terraform is ever used to manage `azurerm_key_vault_secret.value`, the plan must explicitly document that Terraform state contains secret material and must require encrypted remote state, restricted state access, and no plaintext `.tfvars` values.

---

## Alternatives Considered

### A1: GitHub Secrets / Azure DevOps Variable Groups
- Suitable for CI/CD pipeline secrets
- Not suitable for runtime application secrets — no runtime resolution
- **Rejected**: doesn't solve the App Service runtime problem

### A2: Azure App Configuration with Key Vault references
- Adds indirection: App Configuration references Key Vault
- Useful for feature flags and non-sensitive config
- **Deferred**: overkill for this milestone; can be added in M19 if dynamic config becomes valuable

### A3: Sealed Secrets (Kubernetes pattern)
- Only applicable if moving to AKS (not current architecture)
- **Rejected**: not applicable to App Service deployment

### A4: Environment-level secret injection via deployment pipeline
- Secrets pulled from pipeline vault at deploy time, injected as App Service settings
- Simpler but less auditable — secrets visible in App Service config
- **Rejected**: Key Vault references provide better audit trail with no additional complexity

---

## References
- MCSB v2 Data Protection: https://learn.microsoft.com/en-us/security/benchmark/azure/mcsb-v2-data-protection
- App Service Key Vault references: https://learn.microsoft.com/en-us/azure/app-service/app-service-key-vault-references
- Key Vault RBAC: https://learn.microsoft.com/en-us/azure/key-vault/general/rbac-guide
- Terraform azurerm_key_vault: https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/key_vault
