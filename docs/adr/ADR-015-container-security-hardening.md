# ADR-015 — Container Security Hardening

| Field | Value |
|-------|-------|
| **Status** | PROPOSED |
| **Milestone** | M18.7 |
| **Date** | 2025-01 |
| **Author** | Architecture Review |
| **Deciders** | Project Owner |
| **MCSB v2 Domains** | ES-1, ES-2 (Endpoint Security); PV-5 (Posture & Vulnerability) |
| **Already tracked** | M14.8.5 (non-root USER), M14.8.6 (pinned image tags) — this ADR consolidates all container hardening |

---

## Context

The current Dockerfiles (`Dockerfile` and `Dockerfile.worker`) have the following security issues:

1. **No `USER` directive** — containers run as root (UID 0). If the application is compromised, the attacker has root access inside the container, making container escape significantly easier.
2. **No `HEALTHCHECK` directive** — orchestrators (App Service, Docker Compose) cannot determine container health; unhealthy containers may receive traffic.
3. **Writable root filesystem** — the container filesystem is fully writable, enabling an attacker to modify application binaries at runtime.
4. **All Linux capabilities inherited** — .NET web servers need almost no Linux capabilities; the default set includes many that should be dropped.
5. **No distroless or minimal base image** — `mcr.microsoft.com/dotnet/aspnet:10.0` includes a full Debian distribution; attack surface includes tools like `bash`, `curl`, `apt`.

These issues are flagged by:
- MCSB v2 ES-1 ("Use endpoint detection and response solution")
- MCSB v2 ES-2 ("Use centrally managed modern antimalware software")
- CIS Docker Benchmark 4.1 (run as non-root), 4.6 (HEALTHCHECK), 4.9 (writable filesystem)
- M14.8.5 anti-pattern (already tracked in backlog)

---

## Decision

**Harden both Dockerfiles by adding a non-root user (`appuser`, UID 1000), HEALTHCHECK instruction, and documented capability drop. Pin all docker-compose image tags to exact versions. Evaluate distroless as a future base image.**

The `USER` and `HEALTHCHECK` changes are the highest-priority items and are directly actionable in M18. Read-only filesystem and capability drops require testing to confirm .NET runtime compatibility.

---

## Hardened Dockerfile Pattern

### API (`Dockerfile`) — After Hardening

```dockerfile
# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ENV CI=true
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1

COPY . .
RUN find scripts -name "*.sh" -type f -exec sed -i 's/\r$//' {} \; 2>/dev/null || true
RUN chmod +x scripts/*.sh 2>/dev/null || true
RUN ./scripts/restore.sh
RUN ./scripts/build.sh
RUN ./scripts/test.sh
RUN ./scripts/dotnet.sh publish src/Mvp.Trading.Api/Mvp.Trading.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create non-root user — satisfies M14.8.5 and CIS Docker 4.1
RUN groupadd -g 1000 appgroup && useradd -u 1000 -g appgroup -m -s /sbin/nologin appuser

# Set ownership of application files to appuser
COPY --from=build --chown=appuser:appgroup /app/publish ./

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

# Drop to non-root before starting the process
USER appuser

# Health check uses /health/live (liveness only — no dependency checks on every probe)
# /health/live is already configured in Program.cs with Predicate = _ => false
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "Mvp.Trading.Api.dll"]
```

### Worker (`Dockerfile.worker`) — After Hardening

```dockerfile
# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ENV CI=true
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1

COPY . .
RUN find scripts -name "*.sh" -type f -exec sed -i 's/\r$//' {} \; 2>/dev/null || true
RUN chmod +x scripts/*.sh 2>/dev/null || true
RUN ./scripts/restore.sh
RUN ./scripts/build.sh
RUN ./scripts/dotnet.sh publish src/Mvp.Trading.Worker/Mvp.Trading.Worker.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create non-root user
RUN groupadd -g 1000 appgroup && useradd -u 1000 -g appgroup -m -s /sbin/nologin appuser

COPY --from=build --chown=appuser:appgroup /app/publish ./

# Worker exposes Prometheus metrics scrape endpoint (not an HTTP API)
EXPOSE 9464

USER appuser

HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:9464/metrics || exit 1

ENTRYPOINT ["dotnet", "Mvp.Trading.Worker.dll"]
```

---

## docker-compose.yml — Pinned Image Tags

```yaml
# Pinned versions — update via Dependabot (ADR-014)
postgres:
  image: postgres:16.4-alpine3.20

redis:
  image: redis:7.4.0-alpine3.20

ngrok:
  image: ngrok/ngrok:3.19.0

prometheus:
  image: prom/prometheus:v2.55.1

grafana:
  image: grafana/grafana:11.3.1
```

---

## Read-Only Filesystem (Evaluation Required)

.NET runtime and ASP.NET Core write to the filesystem in specific scenarios:
- Temporary files in `/tmp` (DataProtection keys by default, NLog, etc.)
- Culture data files on first access (usually cached after first run)
- `DOTNET_CLI_HOME` and NuGet cache (only during build, not runtime)

To enable read-only root filesystem with required temp access:

```yaml
# docker-compose.yml
api:
  read_only: true
  tmpfs:
    - /tmp           # ASP.NET Core temp files
    - /var/tmp       # Additional temp space
```

**Validation required**: Run application with `read_only: true` in staging environment. Known issues: DataProtection key ring writes to filesystem by default — must configure to use Redis or Azure Blob Storage instead.

For Azure App Service, read-only filesystem is not configurable at the container level — this applies to docker-compose and AKS deployments only.

---

## Linux Capability Drops (docker-compose only)

```yaml
api:
  cap_drop:
    - ALL        # Drop all capabilities first
  cap_add:
    - NET_BIND_SERVICE  # Required to bind to port 8080 as non-root
                        # (only needed if binding to port < 1024)
```

**Note**: Port 8080 is > 1024, so `NET_BIND_SERVICE` is NOT required. All capabilities can be dropped.

```yaml
api:
  cap_drop:
    - ALL   # Sufficient for port 8080 binding as non-root
```

---

## ASP.NET Core Health Check Endpoint

The HEALTHCHECK instruction requires a `/health` endpoint. ASP.NET Core provides this via `Microsoft.AspNetCore.Diagnostics.HealthChecks`:

```csharp
// In Program.cs
builder.Services.AddHealthChecks()
    .AddNpgsql(connectionString, name: "postgres", tags: ["db"])
    .AddRedis(redisConnectionString, name: "redis", tags: ["cache"]);

app.MapHealthChecks("/health", new HealthCheckOptions
{
    // ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse  // requires AspNetCore.HealthChecks.UI.Client — not installed
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false  // liveness: always true if process is running
});
```

---

## Distroless Base Image (Future Enhancement)

Microsoft provides a `chiseled` (distroless) variant of the .NET runtime image that eliminates the shell and most system utilities:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
```

Benefits:
- ~60% smaller image size
- No `bash`, `sh`, `curl`, `apt` — removes tools attackers typically use post-compromise
- Trivy scan shows significantly fewer OS-level CVEs

Drawbacks:
- `HEALTHCHECK` with `curl` no longer works — requires HTTP-based health check using `dotnet-monitor` sidecar or custom binary
- Harder to debug (no shell access)

**Deferred to M19**: requires testing with the specific .NET version and confirms no runtime compatibility issues. Consider making it part of Trivy scan improvement initiative.

---

## Consequences

### Positive
- **Non-root containers**: eliminates most container escape vectors (CIS Docker 4.1, M14.8.5)
- **HEALTHCHECK**: App Service and docker-compose correctly detect and restart unhealthy containers
- **Pinned images**: eliminates silent upstream breakage from `:latest` tags (M14.8.6)
- **MCSB compliance**: satisfies ES-1 (endpoint hardening); PV-5 (vulnerability reduction)
- **Smaller blast radius**: even if compromised, attacker has no root, no capabilities, no writable system paths

### Negative / Tradeoffs
- **Dockerfile complexity increases**: three additional `RUN` commands and changed `COPY` flags
- **Health endpoint prerequisite**: requires `/health` endpoint in the API and metrics endpoint confirmed accessible in Worker
- **Image rebuild**: all existing cached layers are invalidated by USER addition

### Neutral
- `appuser` UID 1000 is the conventional non-root UID — compatible with most volume mount setups

---

## Alternatives Considered

### A1: Use `nobody` user instead of creating `appuser`
- `nobody` (UID 65534) exists in the base image
- No home directory — .NET may fail to write temporary state
- **Rejected**: creating explicit `appuser` with UID 1000 is safer and more auditable

### A2: Rootless containers via Podman
- Podman runs containers as the calling user by default
- Not applicable to Azure App Service deployment
- **Rejected**: App Service uses Docker; Podman not relevant here

### A3: Distroless image now
- Reduces attack surface significantly
- Requires custom health check binary or sidecar
- **Deferred to M19**: needs thorough validation; `USER` change is the highest-value immediate action

---

## References
- CIS Docker Benchmark v1.6: https://www.cisecurity.org/benchmark/docker
- MCSB v2 Endpoint Security: https://learn.microsoft.com/en-us/security/benchmark/azure/mcsb-v2-endpoint-security
- .NET chiseled containers: https://devblogs.microsoft.com/dotnet/announcing-dotnet-chiseled-containers/
- ASP.NET Core Health Checks: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks
