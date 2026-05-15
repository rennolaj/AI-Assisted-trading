# ADR-011 — Network Security: VNet Integration and Private Endpoints

| Field | Value |
|-------|-------|
| **Status** | PROPOSED |
| **Milestone** | M18.3 |
| **Date** | 2025-01 |
| **Author** | Architecture Review |
| **Deciders** | Project Owner |
| **MCSB v2 Domains** | NS-1, NS-2, NS-3, NS-7 (Network Security) |
| **Depends on** | ADR-009 (Managed Identity), ADR-010 (Key Vault) |

---

## Context

The current repo does not contain Terraform `.tf` files or Bicep `.bicep` templates. The available local state backup indicates the deployed/intended Azure baseline is:

- **Azure App Service (F1 free tier)** — API
- **Azure Container Instances** — Worker, monitoring, and Ollama
- **Azure Database for PostgreSQL Flexible Server** — public endpoint with password auth
- **Azure Cache for Redis (Basic SKU)** — public endpoint with connection string

With the F1 (Free) App Service SKU, VNet integration is **not available** — it requires at least the B1 (Basic) tier. This is a hard blocker for network isolation until M18.0 reconstructs the IaC source of truth.

Current network exposure:
- PostgreSQL Flexible Server: publicly reachable on port 5432 with firewall rules
- Redis: publicly reachable on port 6380 (TLS) with access key
- App Service: publicly reachable on HTTPS (correct — this is the webhook receiver)
- Key Vault (future): publicly reachable by default

This violates MCSB v2 NS-1 ("Establish network segmentation boundaries") and NS-7 ("Simplify network security configuration"). Redis and PostgreSQL should not be reachable from the public internet.

---

## Decision

**After M18.0 reconstructs IaC, upgrade/select workload hosting that supports private egress, create a VNet with approved workload subnet(s) and private endpoint subnet(s), and disable public network access on Redis, PostgreSQL, and Key Vault once private endpoints are active.**

For the API, this likely means App Service F1 → minimum B2. For the Worker, either retain ACI with VNet/subnet attachment where supported or migrate to App Service/Container Apps and document that decision before implementation. This cost and hosting trade-off is acceptable to evaluate because live trading with Kraken production API keys is in scope.

---

## Network Architecture

```
VNet: aiassist26-vnet (10.0.0.0/16)
├─ Subnet: app-subnet (10.0.1.0/24)
│   └─ App Service VNet Integration (API egress)
│   └─ NSG: allow outbound to data-subnet only for 5432, 6380, 443
├─ Subnet: container-subnet (10.0.3.0/24)
│   └─ ACI Worker/monitoring/Ollama egress if ACI is retained
│   └─ NSG: allow outbound to data-subnet only for approved ports
└─ Subnet: data-subnet (10.0.2.0/24)
    └─ Private Endpoint: aiassist26-postgres.postgres.database.azure.com
    └─ Private Endpoint: aiassist26-redis.redis.cache.windows.net
    └─ Private Endpoint: aiassist26-kv.vault.azure.net
    └─ NSG: deny all inbound except from approved workload subnet(s)
```

### Traffic Flows (post-implementation)

| Source | Destination | Path | Allowed |
|--------|-------------|------|---------|
| Internet | App Service (8080/443) | Public IP | ✅ (webhook receiver) |
| App Service | PostgreSQL (5432) | Private endpoint in VNet | ✅ |
| App Service | Redis (6380) | Private endpoint in VNet | ✅ |
| App Service | Key Vault (443) | Private endpoint in VNet | ✅ |
| Worker host | PostgreSQL/Redis/Key Vault | Private endpoint in VNet | ✅ |
| Internet | PostgreSQL (5432) | Public IP | ❌ (disabled) |
| Internet | Redis (6380) | Public IP | ❌ (disabled) |
| Internet | Key Vault (443) | Public IP | ❌ (disabled) |

---

## Consequences

### Positive
- **Redis and PostgreSQL not internet-reachable**: eliminates the largest attack surface in the architecture
- **Key Vault privately accessed**: management plane still public (portal), data plane private
- **NSG defense-in-depth**: even if private endpoint is misconfigured, NSG denies cross-subnet traffic
- **MCSB compliance**: satisfies NS-1, NS-2, NS-3, NS-7
- **Enables ADR-010 Key Vault private endpoint**: Key Vault network ACL `default_action = "Deny"` requires private endpoint to be accessible from App Service

### Negative / Tradeoffs
- **Cost increase**: API App Service F1 → B2 adds cost; retaining private ACI networking or migrating Worker hosting may add further cost
- **Local development unaffected**: docker-compose continues to use direct connections — no VNet involved locally
- **DNS complexity**: Private endpoints require private DNS zones. Terraform must create:
  - `privatelink.postgres.database.azure.com`
  - `privatelink.redis.cache.windows.net`
  - `privatelink.vaultcore.azure.net`
- **Developer access to Postgres**: Without public endpoint, developers cannot connect to Azure Postgres from local machines for debugging. Requires Azure Bastion or VPN to access data-subnet (out of scope for M18 — document as known constraint).

### Neutral
- App Service on B2 has more capacity than needed for MVP, but no smaller non-Premium SKU supports VNet integration. Worker hosting remains an explicit M18.0/M18.3 design choice.

---

## Implementation Notes

### Terraform Resource Pattern (VNet + Subnets)

```hcl
resource "azurerm_virtual_network" "vnet" {
  name                = "${var.prefix}-vnet"
  resource_group_name = azurerm_resource_group.rg.name
  location            = var.location
  address_space       = ["10.0.0.0/16"]
}

resource "azurerm_subnet" "app" {
  name                 = "app-subnet"
  resource_group_name  = azurerm_resource_group.rg.name
  virtual_network_name = azurerm_virtual_network.vnet.name
  address_prefixes     = ["10.0.1.0/24"]

  delegation {
    name = "app-service-delegation"
    service_delegation {
      name    = "Microsoft.Web/serverFarms"
      actions = ["Microsoft.Network/virtualNetworks/subnets/action"]
    }
  }
}

resource "azurerm_subnet" "data" {
  name                                          = "data-subnet"
  resource_group_name                           = azurerm_resource_group.rg.name
  virtual_network_name                          = azurerm_virtual_network.vnet.name
  address_prefixes                              = ["10.0.2.0/24"]
  private_endpoint_network_policies             = "Disabled"
}
```

### PostgreSQL — Disable Public Access

```hcl
# ✅ CORRECT — disable public network access on the server resource directly
resource "azurerm_postgresql_flexible_server" "pg" {
  # ... other config ...
  public_network_access_enabled = false   # disables public internet access entirely
  # Note: private endpoint (above) must be active before setting this to false
  # or the server will become unreachable
}

# ❌ DO NOT USE the 0.0.0.0 firewall rule — it OPENS Azure services access, not closes public access
# resource "azurerm_postgresql_flexible_server_firewall_rule" "deny_public" {
#   start_ip_address = "0.0.0.0"
#   end_ip_address   = "0.0.0.0"
# }
```

### App Service VNet Integration

```hcl
# ✅ CORRECT — VNet integration is set directly on the web app resource
# azurerm_app_service_virtual_network_swift_connection is DEPRECATED in AzureRM 3.x+
resource "azurerm_linux_web_app" "api" {
  # ... other config ...
  virtual_network_subnet_id = azurerm_subnet.app.id   # replaces swift_connection
}
```

### SKU Upgrade

```hcl
resource "azurerm_service_plan" "plan" {
  name                = "${var.prefix}-plan"
  resource_group_name = azurerm_resource_group.rg.name
  location            = var.location
  os_type             = "Linux"
  sku_name            = "B1"  # Was F1 — B1 is the minimum SKU supporting VNet integration
                               # B2 or higher only if compute capacity is needed
}
```

> **Redis SKU prerequisite**: Private endpoints for Redis require **Standard SKU or higher**. The current `redis_sku = "Basic"` in terraform.tfvars must be upgraded to `"Standard"` before the Redis private endpoint can be provisioned. This also unblocks Entra ID auth for Redis (ADR-009).

---

## Alternatives Considered

### A1: Firewall IP Allowlist (no VNet)
- Restrict Redis/PostgreSQL to App Service outbound IPs via firewall rules
- App Service outbound IPs are not static (multiple IPs, can change)
- **Rejected**: unreliable IP allowlisting with dynamic App Service IPs; private endpoint is the correct pattern

### A2: Azure Application Gateway as WAF for webhook
- Adds WAF (Web Application Firewall) in front of App Service
- Overkill for MVP — App Service built-in DDoS protection is sufficient for webhook receiver
- **Deferred**: consider for M19 if webhook receives significant traffic or attack surface grows

### A3: AKS with Kubernetes NetworkPolicy
- Provides fine-grained pod-level network control
- Requires significantly more infrastructure complexity
- **Rejected**: AKS migration not in scope for M18

### A4: Stay on F1, skip VNet isolation
- Zero cost increase
- Redis and PostgreSQL remain publicly accessible — violates MCSB NS-1
- **Rejected**: unacceptable risk for production live-trading credentials

---

## References
- MCSB v2 Network Security: https://learn.microsoft.com/en-us/security/benchmark/azure/mcsb-v2-network-security
- Azure App Service VNet integration: https://learn.microsoft.com/en-us/azure/app-service/configure-vnet-integration-enable
- Private endpoints for PostgreSQL: https://learn.microsoft.com/en-us/azure/postgresql/flexible-server/concepts-networking-private-link
- Private endpoints for Redis: https://learn.microsoft.com/en-us/azure/azure-cache-for-redis/cache-private-link
