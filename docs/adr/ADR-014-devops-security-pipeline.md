# ADR-014 — DevOps Security Pipeline: Secret Scanning, Vulnerability Gates, Supply Chain

| Field | Value |
|-------|-------|
| **Status** | PROPOSED |
| **Milestone** | M18.6 |
| **Date** | 2025-01 |
| **Author** | Architecture Review |
| **Deciders** | Project Owner |
| **MCSB v2 Domains** | DS-1, DS-2, DS-3, DS-4, DS-6 (DevOps Security) |

---

## Context

The repository has no CI/CD pipeline files visible (no `.github/workflows/` or Azure DevOps YAML detected). Building and testing is done locally via `./scripts/build.sh` and `./scripts/test.sh`. Deployment is manual.

Current DevOps security gaps:
- **No secret scanning**: `.env*.local` and `terraform.tfvars` are gitignored, but a developer mistake could accidentally commit secrets
- **No NuGet vulnerability gate**: `dotnet list package --vulnerable` not part of any build gate (M14.8.7 — tracked but not implemented)
- **No container image scanning**: no Trivy or Defender for Containers scan before push to ACR
- **Unpinned base images**: Dockerfiles use `mcr.microsoft.com/dotnet/sdk:10.0` and `mcr.microsoft.com/dotnet/aspnet:10.0` — version-pinned but not SHA-pinned
- **docker-compose uses `:latest`** for postgres, redis, ngrok, prometheus, grafana (M14.8.6 — tracked)
- **No Software Bill of Materials (SBOM)**: no `dotnet sbom` generation in build pipeline
- **No dependency review**: no Dependabot or Renovate for automated NuGet/Docker update PRs

This violates MCSB v2 DS-1 ("Use a centrally managed and trusted platform"), DS-2 ("Ensure software supply chain security"), DS-3 ("Secure DevOps infrastructure"), and DS-6 ("Enforce security of workload throughout DevOps lifecycle").

---

## Decision

**Create a GitHub Actions CI pipeline with security gates: secret scanning, NuGet vulnerability check, container image scan (Trivy), and SBOM generation. Pin all Docker image tags to exact versions. Enable Dependabot for NuGet and Docker.**

---

## Pipeline Design

### Trigger Matrix

```yaml
on:
  push:
    branches: [main, feature/**]
  pull_request:
    branches: [main]
  schedule:
    - cron: '0 6 * * 1'  # weekly vulnerability scan on Monday 06:00 UTC
```

### Stage 1: Secret Scanning (every push/PR)

```yaml
- name: Secret scan
  # Pin to a reviewed version tag or commit SHA; do not use mutable @main.
  uses: trufflesecurity/trufflehog@<pinned-version-or-sha>
  with:
    path: ./
    base: ${{ github.event.repository.default_branch }}
    head: HEAD
    extra_args: --debug --only-verified
```

Fail condition: any verified secret detected. Unverified findings (false positive risk) are warnings only.

Alternative: GitHub Advanced Security with push protection (requires GitHub Team/Enterprise or public repository).

### Stage 2: Build and Test Gate (every push/PR)

```yaml
- name: Restore
  run: ./scripts/restore.sh

- name: Build
  run: ./scripts/build.sh

- name: Test
  run: ./scripts/test.sh

- name: NuGet vulnerability check
  run: |
    dotnet list package --vulnerable --include-transitive --format json > vuln.json
    # Fail if any Critical or High severity found
    python3 -c "
    import json, sys
    with open('vuln.json') as f:
        d = json.load(f)
    critical = [p for proj in d.get('projects',[]) 
                for fw in proj.get('frameworks',[])
                for p in fw.get('topLevelPackages',[])+fw.get('transitivePackages',[])
                if p.get('severity') in ('Critical','High')]
    if critical:
        print('FAIL: Critical/High vulnerabilities found:')
        for p in critical: print(f'  {p[\"id\"]} {p[\"resolvedVersion\"]} - {p[\"severity\"]}')
        sys.exit(1)
    print('PASS: No critical/high vulnerabilities found')
    "
```

### Stage 3: Container Build and Scan (main branch and PRs)

```yaml
- name: Build API container
  run: docker build -t api:${{ github.sha }} -f Dockerfile .

- name: Build Worker container
  run: docker build -t worker:${{ github.sha }} -f Dockerfile.worker .

- name: Trivy scan — API
  # Pin to a reviewed version tag or commit SHA; do not use mutable @master.
  uses: aquasecurity/trivy-action@<pinned-version-or-sha>
  with:
    image-ref: api:${{ github.sha }}
    format: sarif
    output: trivy-api.sarif
    severity: CRITICAL,HIGH
    exit-code: 1  # Fail on critical/high CVEs

- name: Trivy scan — Worker
  uses: aquasecurity/trivy-action@<pinned-version-or-sha>
  with:
    image-ref: worker:${{ github.sha }}
    format: sarif
    output: trivy-worker.sarif
    severity: CRITICAL,HIGH
    exit-code: 1

- name: Upload API Trivy SARIF to GitHub Security
  uses: github/codeql-action/upload-sarif@v3
  with:
    sarif_file: trivy-api.sarif

- name: Upload Worker Trivy SARIF to GitHub Security
  uses: github/codeql-action/upload-sarif@v3
  with:
    sarif_file: trivy-worker.sarif
```

### Stage 4: SBOM Generation (main branch only)

```yaml
- name: Generate SBOM
  run: dotnet tool install --global Microsoft.Sbom.DotNetTool && sbom-tool generate -b . -bc . -pn AI-Assisted-Trading -pv ${{ github.sha }} -ps RennolaJ -nsb https://sbom.example.com
  
- name: Upload SBOM artifact
  uses: actions/upload-artifact@v4
  with:
    name: sbom-${{ github.sha }}
    path: _manifest/
```

### Stage 5: ACR Push (main branch only, after all security gates pass)

```yaml
- name: Login to ACR
  uses: azure/docker-login@v1
  with:
    login-server: ${{ vars.ACR_LOGIN_SERVER }}
    username: ${{ secrets.ACR_USERNAME }}    # Service principal for CI (not admin)
    password: ${{ secrets.ACR_PASSWORD }}

- name: Push to ACR
  run: |
    docker tag api:${{ github.sha }} ${{ vars.ACR_LOGIN_SERVER }}/api:${{ github.sha }}
    docker tag api:${{ github.sha }} ${{ vars.ACR_LOGIN_SERVER }}/api:latest
    docker push ${{ vars.ACR_LOGIN_SERVER }}/api:${{ github.sha }}
    docker push ${{ vars.ACR_LOGIN_SERVER }}/api:latest
```

**Note**: ACR push uses a Service Principal with `AcrPush` role only — not admin credentials. GitHub repository secrets store the service principal credentials. The App Service pull from ACR uses Managed Identity (ADR-009) — separate credential.

---

## Docker Image Pinning

### docker-compose.yml — Pin all image versions

```yaml
# Current (bad):
image: postgres:16
image: redis:7
image: ngrok/ngrok:latest
image: prom/prometheus:latest
image: grafana/grafana:latest

# After (pinned to specific versions):
image: postgres:16.4-alpine3.20
image: redis:7.4.0-alpine3.20
image: ngrok/ngrok:3.19.0
image: prom/prometheus:v2.55.1
image: grafana/grafana:11.3.1
```

### Dockerfiles — Pin to version (SHA pinning deferred)

```dockerfile
# Current:
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# After (minor version pinned — SHA pinning is ideal but requires Dependabot to maintain):
FROM mcr.microsoft.com/dotnet/sdk:10.0.101 AS build
FROM mcr.microsoft.com/dotnet/aspnet:10.0.1 AS runtime
```

SHA pinning (`FROM image@sha256:...`) provides maximum supply chain integrity but requires tooling (Dependabot) to keep up to date. Enable after Dependabot is configured.

---

## Dependabot Configuration

```yaml
# .github/dependabot.yml
version: 2
updates:
  - package-ecosystem: nuget
    directory: "/"
    schedule:
      interval: weekly
    groups:
      microsoft-extensions:
        patterns: ["Microsoft.Extensions.*", "Microsoft.AspNetCore.*"]
      azure-sdk:
        patterns: ["Azure.*"]

  - package-ecosystem: docker
    directories:
      - "/"          # Dockerfile, Dockerfile.worker
      - "/"          # docker-compose.yml (parsed as Docker)
    schedule:
      interval: weekly

  - package-ecosystem: github-actions
    directory: "/.github/workflows"
    schedule:
      interval: weekly
```

---

## Workload Identity Federation for CI (Future Enhancement)

Instead of storing ACR service principal credentials as GitHub Secrets, use Workload Identity Federation:

```hcl
# Terraform: create federated identity credential
resource "azurerm_user_assigned_identity" "ci" {
  name                = "${var.prefix}-ci-identity"
  resource_group_name = azurerm_resource_group.rg.name
  location            = var.location
}

resource "azurerm_federated_identity_credential" "github" {
  name                = "github-actions"
  resource_group_name = azurerm_resource_group.rg.name
  parent_id           = azurerm_user_assigned_identity.ci.id
  audience            = ["api://AzureADTokenExchange"]
  issuer              = "https://token.actions.githubusercontent.com"   # ✅ GitHub OIDC issuer (official)
  subject             = "repo:rennolaj/AI-Assisted-trading:ref:refs/heads/main"
}
```

This eliminates all credentials from GitHub Secrets for Azure operations — the pipeline exchanges a short-lived GitHub OIDC token for an Azure access token at runtime. Implement in M18.6 or M19.

---

## Consequences

### Positive
- **Secret leak prevention**: Trufflehog catches accidentally committed credentials before they reach GitHub
- **Vulnerability gate**: NuGet and container CVE gates block unsafe deployments
- **Supply chain integrity**: SBOM provides full dependency inventory; pinned images prevent unexpected updates
- **MCSB compliance**: satisfies DS-1, DS-2, DS-3, DS-4, DS-6
- **Automated updates**: Dependabot keeps NuGet and Docker images current without manual tracking

### Negative / Tradeoffs
- **CI build time**: Trivy scan adds ~2-3 minutes; SBOM generation adds ~1 minute
- **False positive management**: Trufflehog may flag test fixture files or example configs — requires suppression configuration
- **GitHub Actions minutes**: Self-hosted runners preferred to avoid minutes limits on large projects
- **IaC not yet in repo**: Pipeline cannot deploy IaC until Terraform/Bicep source files are added to repo

### Neutral
- Dependabot NuGet PRs require manual review — set to weekly to avoid PR flood

---

## Alternatives Considered

### A1: Azure DevOps pipelines instead of GitHub Actions
- Native Azure integration, built-in secret scanning
- Requires separate Azure DevOps organization setup
- **Rejected**: GitHub is already the repository host; GitHub Actions is simpler and sufficient

### A2: Snyk for vulnerability scanning
- Comprehensive vulnerability database, good developer UX
- $99/month for paid tier
- **Rejected**: Trivy (free, OSS) and `dotnet list package --vulnerable` (built-in) cover the requirement

### A3: Container image signing (cosign/Notary v2)
- Provides cryptographic proof of image provenance
- Required for MCSB DS-5 (container image signing)
- **Deferred to M19**: requires additional key management infrastructure

---

## References
- MCSB v2 DevOps Security: https://learn.microsoft.com/en-us/security/benchmark/azure/mcsb-v2-devops-security
- GitHub Actions security hardening: https://docs.github.com/en/actions/security-for-github-actions/security-guides/security-hardening-for-github-actions
- Trivy container scanning: https://trivy.dev/latest/docs/target/container_image/
- Trufflehog secret scanning: https://github.com/trufflesecurity/trufflehog
- dotnet SBOM tool: https://github.com/microsoft/sbom-tool
- Workload Identity Federation: https://learn.microsoft.com/en-us/azure/active-directory/develop/workload-identity-federation
