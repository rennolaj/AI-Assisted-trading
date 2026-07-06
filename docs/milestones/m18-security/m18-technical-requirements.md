# M18 Technical Requirements — Azure Security Posture

> Last updated: 2026-05-15  
> Framework: C# 14 / .NET 10 · SDK 10.0.101  
> Reference ADRs: ADR-009 through ADR-018  
> Reference skill: `docs/development/csharp-dotnet10-skill.md`

---

## How to Read This Document

Each section maps one ADR to:
- **Current state** — what the code does today (with exact file/line references)
- **Target state** — what it must do after M18
- **Exact code** — the C# 14/.NET 10 pattern to implement
- **NuGet packages** — what to add/upgrade

---

## 0. Pre-Implementation Blockers (Do Before Writing Code)

| Action | Why | Who |
|--------|-----|-----|
| Rotate TradingView webhook secret in TradingView | Old secret in git history (ADR-018) | Owner |
| Rotate OpenAI API key at platform.openai.com | Plaintext in `terraform.tfvars` on disk | Owner |
| Set OpenAI monthly hard limit to $10 | No spend cap currently | Owner |
| Run git-filter-repo to clean history | Webhook secret in 2 commits | Owner + ADR-018 |
| Enable GitHub Secret Push Protection | Prevent future accidents | Owner |

## 0A. Current IaC Reality (Hard Blocker for Azure Resource Stories)

The `infra/` directory currently does not contain Terraform `.tf` files or Bicep `.bicep` templates. It contains local artifacts only:

- `infra/terraform/terraform.tfstate`
- `infra/terraform/terraform.tfstate.backup`
- `infra/terraform/tfplan`
- `infra/terraform/terraform.tfvars`
- `infra/azure/parameters.local.json`

These files are useful for discovery, but they are not safe authoritative source for implementation. State, plan, variable, and local parameter files must remain local and must not be committed as the IaC definition.

The observed `terraform.tfstate.backup` baseline is:

- API: Azure App Service on F1, public network enabled, system identity present, ACR pull not using managed identity
- Worker, monitoring, and Ollama: Azure Container Instances
- PostgreSQL Flexible Server: public network enabled, password auth enabled, Entra auth disabled, no private subnet or private DNS
- Redis: Basic SKU, public network enabled, TLS enabled, no subnet
- ACR: Basic SKU, public network enabled, admin user enabled
- GitHub Actions: existing `.github/workflows/ci.yml` performs restore/build/test only

M18.0 must reconstruct a real IaC source of truth before M18.1-M18.5 can be implemented. The implementation must choose Terraform or Bicep, import/reconcile the existing Azure resources, move Terraform state to a protected remote backend if Terraform is selected, and validate a clean `terraform plan` or Bicep `what-if` with no unintended destroy/recreate.

---

## 1. Options Validation (All ADRs — cross-cutting)

### Current State

Every Options class is missing `[Required]` attributes and validation:

```csharp
// ❌ CURRENT — src/Mvp.Trading.Api/Services/PostgresOptions.cs
public sealed class PostgresOptions
{
    public string ConnectionString { get; init; } = string.Empty;
    // No [Required] — startup succeeds even if Postgres:ConnectionString is missing
}

// ❌ CURRENT — src/Mvp.Trading.Api/Program.cs (line 18-23)
builder.Services.Configure<TradingViewOptions>(builder.Configuration.GetSection("TradingView"));
builder.Services.Configure<PostgresOptions>(builder.Configuration.GetSection("Postgres"));
builder.Services.Configure<RedisOptions>(builder.Configuration.GetSection("Redis"));
builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection("OpenAI"));
// No .ValidateDataAnnotations().ValidateOnStart() — misconfigured apps start and fail at runtime
```

### Target State

```csharp
// ✅ TARGET — all Options classes get [Required] where the value is mandatory
using System.ComponentModel.DataAnnotations;

namespace Mvp.Trading.Api.Services;

public sealed class PostgresOptions
{
    [Required(AllowEmptyStrings = false)]
    public string ConnectionString { get; init; } = string.Empty;
}

public sealed class RedisOptions
{
    [Required(AllowEmptyStrings = false)]
    public string ConnectionString { get; init; } = string.Empty;

    public string AlertQueueKey { get; init; } = "mvp:alerts";
}

public sealed class TradingViewOptions
{
    [Required(AllowEmptyStrings = false)]
    public string WebhookSecret { get; init; } = string.Empty;
}
```

```csharp
// ✅ TARGET — Program.cs: all Configure<T> replaced with AddOptions<T> chain
builder.Services.AddOptions<TradingViewOptions>()
    .BindConfiguration("TradingView")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<PostgresOptions>()
    .BindConfiguration("Postgres")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<RedisOptions>()
    .BindConfiguration("Redis")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<OpenAiOptions>()
    .BindConfiguration("OpenAI")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<McpProviderOptions>()
    .BindConfiguration("McpProvider")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

**Files to change:**
- `src/Mvp.Trading.Api/Services/PostgresOptions.cs` — add `[Required]`
- `src/Mvp.Trading.Api/Services/RedisOptions.cs` — add `[Required]` on ConnectionString
- `src/Mvp.Trading.Api/Services/TradingViewOptions.cs` — add `[Required]`
- `src/Mvp.Trading.Api/Mcp/OpenAiOptions.cs` — add `[Required]` on ApiKey
- `src/Mvp.Trading.Api/Program.cs` — replace all `Configure<T>` with `AddOptions<T>` chain
- `src/Mvp.Trading.Worker/Program.cs` — same pattern for all worker options
- `src/Mvp.Trading.Execution/Mvp.Trading.Execution.csproj` — bump `Microsoft.Extensions.Caching.Memory` 9.0.0 → 10.0.0

---

## 2. Secret Comparison Hardening (ADR-015 / M14.8.4 / SEC-2)

### Current State

```csharp
// ❌ CURRENT — src/Mvp.Trading.Api/Program.cs line 141
// Timing-attack vulnerable: early exit reveals string prefix matches
if (string.IsNullOrWhiteSpace(expectedSecret) || 
    !string.Equals(secret, expectedSecret, StringComparison.Ordinal))
{
    return Results.Unauthorized();
}
```

### Target State

```csharp
// ✅ TARGET — constant-time comparison, timing-attack safe
using System.Security.Cryptography;
using System.Text;

if (string.IsNullOrWhiteSpace(expectedSecret))
    return TypedResults.Unauthorized();

var providedBytes = Encoding.UTF8.GetBytes(secret ?? string.Empty);
var expectedBytes = Encoding.UTF8.GetBytes(expectedSecret);

if (!CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
    return TypedResults.Unauthorized();
```

Note: also switches from `Results.Unauthorized()` → `TypedResults.Unauthorized()` per C# skill rule.

**Files to change:**
- `src/Mvp.Trading.Api/Program.cs` lines 140–144

---

## 3. KillSwitch Fail-Closed (M14.8.3 / KS-1)

### Current State

```csharp
// ❌ CURRENT — src/Mvp.Trading.Execution/KillSwitchService.cs lines 60-61
// If DB row missing → fail-open: returns inactive = trades can execute
_logger.LogWarning("Kill switch state not found in database, assuming inactive");
return new KillSwitchStatus(false, KillSwitchLevel.PAUSE_ALL, null, null);
```

### Target State

```csharp
// ✅ TARGET — fail-closed: no DB row = treat as emergency stop
_logger.LogError(
    "Kill switch state not found in database — defaulting to EMERGENCY_STOP (fail-closed)");
return new KillSwitchStatus(true, KillSwitchLevel.EMERGENCY_STOP, 
    "DB state missing — fail-closed", activatedAt: null);
```

Also inject `TimeProvider` instead of accepting a raw connection string:

```csharp
// ✅ TARGET — constructor uses IOptions<PostgresOptions> + TimeProvider
public sealed class KillSwitchService(
    IOptions<PostgresOptions> options,
    IMemoryCache cache,
    ITradingProvider tradingProvider,
    TimeProvider time,
    ILogger<KillSwitchService> logger) : IKillSwitchService
{
    private readonly string _connectionString = options.Value.ConnectionString;
    private readonly DateTimeOffset _startTime = time.GetUtcNow();
```

Register TimeProvider in Program.cs:
```csharp
builder.Services.AddSingleton(TimeProvider.System);
```

**Files to change:**
- `src/Mvp.Trading.Execution/KillSwitchService.cs` — fail-closed + TimeProvider injection
- `src/Mvp.Trading.Api/Program.cs` — register `TimeProvider.System`; update KillSwitchService factory

---

## 4. TimeProvider Injection (PERF-1 / M14.6.1 — 16 files)

### Current State (16 files with direct clock access)

```csharp
// ❌ CURRENT — untestable, appears in 16 source files including:
// Program.cs line 215, AlertWorker.cs, ExecutionService.cs, KrakenFuturesRateLimitBudget.cs, etc.
DateTimeOffset.UtcNow
DateTime.UtcNow
```

### Target State

```csharp
// ✅ TARGET — inject TimeProvider, call time.GetUtcNow()
public sealed class AlertWorker(
    // ...existing params...
    TimeProvider time) : BackgroundService
{
    private void RecordAlertTime() => _ = time.GetUtcNow();
}
```

**DI Registration (both Program.cs files):**
```csharp
builder.Services.AddSingleton(TimeProvider.System);
```

**Test usage (FakeTimeProvider):**
```csharp
// NuGet: Microsoft.Extensions.TimeProvider.Testing
var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
var service = new KillSwitchService(..., fakeTime, ...);
fakeTime.Advance(TimeSpan.FromSeconds(10));
```

**Files to change (all 16):**
- `src/Mvp.Trading.Api/Program.cs`
- `src/Mvp.Trading.Api/Mcp/LocalLlmMcpGateway.cs`
- `src/Mvp.Trading.Api/Mcp/OpenAiMcpGateway.cs`
- `src/Mvp.Trading.Api/Services/PostgresIdempotencyStore.cs`
- `src/Mvp.Trading.Api/Services/PostgresOpenTradeCommand.cs`
- `src/Mvp.Trading.Execution/ExecutionService.cs`
- `src/Mvp.Trading.Execution/KillSwitchService.cs`
- `src/Mvp.Trading.Execution/PostgresExecutionHeartbeatStore.cs`
- `src/Mvp.Trading.Execution/PostgresOrderReceiptStore.cs`
- `src/Mvp.Trading.Execution/PostgresReconciliationStore.cs`
- `src/Mvp.Trading.Integrations.Kraken/KrakenFuturesMarketDataProvider.cs`
- `src/Mvp.Trading.Integrations.Kraken/KrakenFuturesRateLimitBudget.cs`
- `src/Mvp.Trading.Integrations.Kraken/KrakenFuturesTradingProvider.cs`
- `src/Mvp.Trading.Worker/AlertWorker.cs`
- `src/Mvp.Trading.Worker/PostgresAlertProcessingStore.cs`
- `src/Mvp.Trading.Worker/PostgresOpenTradeRepository.cs`

**NuGet to add (test projects only):**
```xml
<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.0.0" />
```

---

## 5. Guid.CreateVersion7 for DB Entity IDs (GUID-1 / M14.6.7)

### Current State

```csharp
// ❌ CURRENT — random GUIDs cause index fragmentation on Postgres BTREE index
// Program.cs line 214
var alert = new AlertEvent(Guid.NewGuid(), ...);

// PostgresOpenTradeCommand.cs
var tradeId = Guid.NewGuid();

// PostgresOrderReceiptStore.cs
var receiptId = Guid.NewGuid();

// AlertWorker.cs
var intentId = Guid.NewGuid();
```

### Target State

```csharp
// ✅ TARGET — time-sortable, monotonically increasing, better index locality
var alert = new AlertEvent(Guid.CreateVersion7(), ...);
var tradeId = Guid.CreateVersion7();
var receiptId = Guid.CreateVersion7();
```

`Guid.CreateVersion7()` is a .NET 9+/10 built-in — no NuGet package needed.

**Files to change (4 files):**
- `src/Mvp.Trading.Api/Program.cs`
- `src/Mvp.Trading.Api/Services/PostgresOpenTradeCommand.cs`
- `src/Mvp.Trading.Execution/PostgresOrderReceiptStore.cs`
- `src/Mvp.Trading.Worker/AlertWorker.cs`

---

## 6. HttpClient Resilience (RES-1 / M14.8.1)

### Current State

```csharp
// ❌ CURRENT — bare HttpClient, no retry/circuit breaker/timeout
// src/Mvp.Trading.Api/Program.cs lines 24-44
builder.Services.AddHttpClient<IOpenAiResponsesClient, OpenAiResponsesClient>((sp, client) => {
    // ... base address setup only
});

builder.Services.AddHttpClient<ILocalLlmResponsesClient, LocalLlmResponsesClient>((sp, client) => {
    // ... base address setup only
});

// src/Mvp.Trading.Worker/Program.cs line 62
builder.Services.AddHttpClient<KrakenFuturesMarketDataProvider>();
```

### Target State

```csharp
// ✅ TARGET — standard resilience handler on every AddHttpClient registration
builder.Services.AddHttpClient<IOpenAiResponsesClient, OpenAiResponsesClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<OpenAiOptions>>().Value;
    var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) 
        ? "https://api.openai.com/v1/" 
        : options.BaseUrl;
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
})
.AddResilienceHandler("openai", pipeline =>
{
    pipeline.AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromMilliseconds(500),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        // Do not retry 401 (bad key) or 400 (bad request)
        ShouldHandle = args => ValueTask.FromResult(
            args.Outcome.Exception is not null ||
            (int)(args.Outcome.Result?.StatusCode ?? 0) >= 500)
    });
    pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5,
        SamplingDuration = TimeSpan.FromSeconds(30),
        MinimumThroughput = 5,
        BreakDuration = TimeSpan.FromSeconds(30)
    });
    pipeline.AddTimeout(TimeSpan.FromSeconds(30)); // LLM calls can be slow
});

builder.Services.AddHttpClient<ILocalLlmResponsesClient, LocalLlmResponsesClient>((sp, client) =>
{
    // ... same base address setup
})
.AddResilienceHandler("local-llm", pipeline =>
{
    pipeline.AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 2 });
    pipeline.AddTimeout(TimeSpan.FromSeconds(60)); // Local models can be very slow
});

// Worker: Kraken HTTP client
builder.Services.AddHttpClient<KrakenFuturesMarketDataProvider>()
    .AddStandardResilienceHandler(); // standard: retry + CB + timeout (10s default)

builder.Services.AddHttpClient<KrakenFuturesTradingProvider>()
    .AddStandardResilienceHandler();
```

**NuGet to add (Api + Worker projects):**
```xml
<PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="10.0.0" />
```

**Files to change:**
- `src/Mvp.Trading.Api/Mvp.Trading.Api.csproj` — add Http.Resilience
- `src/Mvp.Trading.Worker/Mvp.Trading.Worker.csproj` — add Http.Resilience
- `src/Mvp.Trading.Api/Program.cs` — chain resilience on both LLM clients
- `src/Mvp.Trading.Worker/Program.cs` — chain resilience on Kraken clients

---

## 7. Managed Identity & Azure Identity (ADR-009)

### Azure Prerequisites

Before code can use passwordless PostgreSQL authentication in Azure:
- Enable Microsoft Entra authentication on Azure Database for PostgreSQL Flexible Server.
- Configure a Microsoft Entra administrator for the server.
- Create database principals for the API managed identity and the Worker managed identity/container identity from the `postgres` database:

```sql
select * from pgaadauth_create_principal('<api-managed-identity-name>', false, false);
select * from pgaadauth_create_principal('<worker-managed-identity-name>', false, false);
```

Grant table permissions to those managed identity principals, not to a password-backed runtime user. A local password-backed `appuser` may exist for docker-compose development only.

### New NuGet Packages

```xml
<!-- Add to Api + Worker csproj for Azure production deployment -->
<PackageReference Include="Azure.Identity" Version="1.13.2" />
<!-- NOTE: Npgsql.Azure does NOT exist. UseAzureADAuthentication() is built into Npgsql 8 core. -->
<!-- For Redis Entra ID auth (requires Standard SKU or higher — NOT Basic SKU) -->
<PackageReference Include="Microsoft.Azure.StackExchangeRedis" Version="1.5.0" />
```

### NpgsqlDataSource with DefaultAzureCredential

```csharp
// ✅ TARGET — src/Mvp.Trading.Api/Program.cs
// Replace the current NpgsqlDataSource singleton factory:
builder.Services.AddSingleton<NpgsqlDataSource>(sp =>
{
    var options = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
    var builder = new NpgsqlDataSourceBuilder(options.ConnectionString);

    // When running in Azure with Managed Identity, use Entra ID token auth.
    // Detected by presence of AZURE_CLIENT_ID env var OR non-Development environment.
    var env = sp.GetRequiredService<IHostEnvironment>();
    if (!env.IsDevelopment() || 
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")))
    {
        builder.UseAzureADAuthentication(new DefaultAzureCredential());
    }

    return builder.Build();
});
```

### Redis with DefaultAzureCredential

```csharp
// ✅ TARGET — conditional Azure identity auth for Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var options = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
    var env = sp.GetRequiredService<IHostEnvironment>();

    if (!env.IsDevelopment())
    {
        // Azure Cache for Redis with Entra ID — uses IHostedService initializer pattern
        // to avoid blocking .GetAwaiter().GetResult() in a sync DI factory (deadlock risk in ASP.NET Core)
        // See RedisInitializerService (IHostedService) below for the recommended async init pattern
        throw new InvalidOperationException(
            "Redis Entra ID auth must be initialized via RedisInitializerService (IHostedService). " +
            "Register IConnectionMultiplexer as a lazy/deferred singleton instead.");
    }

    // Local development: use connection string with password as before
    return ConnectionMultiplexer.Connect(options.ConnectionString);
});

// ✅ Recommended pattern: async IHostedService initializer for Redis Entra ID auth
// This avoids .GetAwaiter().GetResult() deadlock risk in ASP.NET Core DI factory
public sealed class RedisInitializerService(
    IOptions<RedisOptions> options,
    IServiceProvider serviceProvider,
    ILogger<RedisInitializerService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var hostname = options.Value.ConnectionString.Split(':')[0];
        var configOptions = ConfigurationOptions.Parse(hostname + ":6380");
        configOptions.Ssl = true;
        configOptions.AbortConnect = false;
        await configOptions.ConfigureForAzureWithTokenCredentialAsync(new DefaultAzureCredential());
        var multiplexer = await ConnectionMultiplexer.ConnectAsync(configOptions);
        // Replace the placeholder singleton with the actual authenticated connection
        // (use IConnectionMultiplexer wrapper pattern or lazy initialization)
        logger.LogInformation("Redis authenticated via Entra ID Managed Identity");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

---

## 8. Azure Key Vault Integration (ADR-010)

### App Service Key Vault References (IaC Only)

No application code changes are needed for Key Vault references. App Service resolves the `@Microsoft.KeyVault(...)` values at startup before injecting them as environment variables. The application continues to read `IOptions<OpenAiOptions>().Value.ApiKey` — it just receives the resolved value from Key Vault instead of a hardcoded env var.

### Optional: Key Vault SDK for Programmatic Access (not required for M18)

If the application ever needs to read secrets dynamically at runtime (not at startup):

```xml
<PackageReference Include="Azure.Security.KeyVault.Secrets" Version="4.7.0" />
```

```csharp
// Example — not required for M18 (App Service references handle this)
var kvClient = new SecretClient(
    new Uri("https://aiassist26-kv.vault.azure.net/"),
    new DefaultAzureCredential());
var secret = await kvClient.GetSecretAsync("openai-api-key", cancellationToken: ct);
```

---

## 9. Database Least Privilege (ADR-012)

### Azure Runtime Connection String Change

In Azure, the application connection string must identify the managed identity database principal and must not include a password:

```
# Before (terraform.tfvars / App Service env var):
Postgres__ConnectionString=Host=...;Username=pgadmin;Password=TestDeploy2026!

# After:
Postgres__ConnectionString=Host=...;Database=...;Username=<api-or-worker-managed-identity-name>;Ssl Mode=Require
# (Password removed — Entra ID token auth handles authentication, ADR-009)
```

### Database Role Grants

For Azure, grant least-privilege DML permissions to the Entra-backed principals created by `pgaadauth_create_principal(...)`:

```sql
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE
    alerts,
    open_trades,
    execution_intents,
    order_receipts,
    reconciliation_records,
    kill_switch_status,
    idempotency_keys
TO "<managed-identity-principal-name>";

-- Sequence access for INSERT operations
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO "<managed-identity-principal-name>";

-- Revoke DDL capability
REVOKE CREATE ON SCHEMA public FROM "<managed-identity-principal-name>";
```

For local docker-compose only, `scripts/db/init.sql` may create an `appuser` with a local development password. That role must not be used by Azure-hosted containers.

### NpgsqlCommand — Enforce SSL

```csharp
// ✅ Ensure connection strings include SSL enforcement
// Add to NpgsqlDataSourceBuilder in Program.cs:
var connBuilder = new NpgsqlConnectionStringBuilder(options.ConnectionString)
{
    SslMode = SslMode.Require  // enforces TLS even if not in connection string
};
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connBuilder.ToString());
```

---

## 10. Container Hardening (ADR-015)

### Dockerfile — Add USER + HEALTHCHECK

```dockerfile
# ✅ TARGET — Dockerfile (API)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create non-root user (M14.8.5 fix)
RUN groupadd -g 1000 appgroup && \
    useradd -u 1000 -g appgroup -m -s /sbin/nologin appuser

COPY --from=build --chown=appuser:appgroup /app/publish ./

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

USER appuser

# Health check requires /health/live endpoint (already exists in Program.cs)
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "Mvp.Trading.Api.dll"]
```

```dockerfile
# ✅ TARGET — Dockerfile.worker
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN groupadd -g 1000 appgroup && \
    useradd -u 1000 -g appgroup -m -s /sbin/nologin appuser

COPY --from=build --chown=appuser:appgroup /app/publish ./

EXPOSE 9464

USER appuser

# Worker exposes Prometheus metrics — check the metrics endpoint
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:9464/metrics | head -1 || exit 1

ENTRYPOINT ["dotnet", "Mvp.Trading.Worker.dll"]
```

### docker-compose.yml — Pin All Image Tags

```yaml
# ✅ TARGET — no :latest tags
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

## 11. TypedResults Migration (C# Skill — ASP.NET Core 10)

### Current State

```csharp
// ❌ CURRENT — Program.cs uses Results.* throughout
return Results.Unauthorized();
return Results.BadRequest(new { error = "Empty payload." });
return Results.Accepted(value: new { status = "enqueued" });
return Results.Ok(new { status = "ok" });
```

### Target State

```csharp
// ✅ TARGET — TypedResults.* for all endpoint handlers
return TypedResults.Unauthorized();
return TypedResults.BadRequest(new { error = "Empty payload." });
return TypedResults.Accepted(new { status = "enqueued" });
return TypedResults.Ok(new { status = "ok" });
```

**Why**: `TypedResults` generates correct OpenAPI schema automatically and is AOT-safe. `Results` is dynamic and cannot infer schema at compile time. Required for `builder.Services.AddOpenApi()` migration (Swashbuckle → built-in).

---

## 12. GitHub Actions Pipeline (ADR-014)

### New file: `.github/workflows/ci.yml` (`.github/workflows/` directory does not yet exist — create from scratch)

> ⚠️ There is no existing CI workflow in this repo. The file and directory must be created new.

### Target workflow content

```yaml
name: CI Security Pipeline

on:
  push:
    branches: [main, "feature/**"]
  pull_request:
    branches: [main]
  schedule:
    - cron: '0 6 * * 1'  # Weekly vulnerability scan

env:
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE: true
  DOTNET_CLI_TELEMETRY_OPTOUT: true
  DOTNET_NOLOGO: true

jobs:
  secret-scan:
    name: Secret Scanning
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with: { fetch-depth: 0 }
      # Pin to a reviewed version tag or commit SHA; do not use mutable @main.
      - uses: trufflesecurity/trufflehog@<pinned-version-or-sha>
        with:
          path: ./
          base: ${{ github.event.repository.default_branch }}
          head: HEAD
          extra_args: --only-verified

  build-test:
    name: Build, Test & Vulnerability Check
    runs-on: ubuntu-latest
    needs: secret-scan
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version-file: global.json

      - name: Restore
        run: ./scripts/restore.sh

      - name: Build
        run: ./scripts/build.sh

      - name: Test
        run: ./scripts/test.sh

      - name: NuGet vulnerability check
        run: |
          dotnet list package --vulnerable --include-transitive --format json > vuln.json
          cat vuln.json
          # Fail on Critical or High
          python3 -c "
          import json, sys
          d = json.load(open('vuln.json'))
          bad = [p for proj in d.get('projects',[])
                   for fw in proj.get('frameworks',[])
                   for p in fw.get('topLevelPackages',[])+fw.get('transitivePackages',[])
                   if p.get('severity') in ('Critical','High')]
          if bad:
              print('FAIL - Critical/High vulnerabilities:')
              [print(f'  {p[\"id\"]} {p[\"resolvedVersion\"]} ({p[\"severity\"]})') for p in bad]
              sys.exit(1)
          print('PASS - No critical/high vulnerabilities')
          "

  container-scan:
    name: Container Build & CVE Scan
    runs-on: ubuntu-latest
    needs: build-test
    steps:
      - uses: actions/checkout@v4

      - name: Build API image
        run: docker build -t api:${{ github.sha }} -f Dockerfile .

      - name: Build Worker image
        run: docker build -t worker:${{ github.sha }} -f Dockerfile.worker .

      - name: Trivy scan — API
        # Pin to a reviewed version tag or commit SHA; do not use mutable @master.
        uses: aquasecurity/trivy-action@<pinned-version-or-sha>
        with:
          image-ref: api:${{ github.sha }}
          format: sarif
          output: trivy-api.sarif
          severity: CRITICAL,HIGH
          exit-code: '1'

      - name: Trivy scan — Worker
        uses: aquasecurity/trivy-action@<pinned-version-or-sha>
        with:
          image-ref: worker:${{ github.sha }}
          format: sarif
          output: trivy-worker.sarif
          severity: CRITICAL,HIGH
          exit-code: '1'

      - name: Upload API SARIF results
        if: always()
        uses: github/codeql-action/upload-sarif@v3
        with:
          sarif_file: trivy-api.sarif

      - name: Upload Worker SARIF results
        if: always()
        uses: github/codeql-action/upload-sarif@v3
        with:
          sarif_file: trivy-worker.sarif
```

### New file: `.github/dependabot.yml`

```yaml
version: 2
updates:
  - package-ecosystem: nuget
    directory: "/"
    schedule:
      interval: weekly
    groups:
      microsoft:
        patterns: ["Microsoft.*", "Azure.*", "System.*"]
      opentelemetry:
        patterns: ["OpenTelemetry*"]

  - package-ecosystem: docker
    directory: "/"
    schedule:
      interval: weekly

  - package-ecosystem: github-actions
    directory: "/.github/workflows"
    schedule:
      interval: weekly
```

---

## 13. LLM Audit Logging (ADR-016)

### New Record + Service (Contracts project)

```csharp
// ✅ TARGET — src/Mvp.Trading.Contracts/Contracts/LlmAudit.cs
namespace Mvp.Trading.Contracts;

public sealed record LlmAuditRecord(
    Guid CallId,
    string Provider,        // "openai" | "local"
    string Model,
    string UseCase,         // "adjudication" | "confluence" | "stop-loss"
    string Symbol,
    int PromptTokens,
    int CompletionTokens,
    string Outcome,
    TimeSpan Latency,
    bool IsAdvisory,
    DateTimeOffset Timestamp
);

public interface ILlmAuditLogger
{
    void Record(LlmAuditRecord entry);
}
```

```csharp
// ✅ TARGET — implementation in Api project (logs to ILogger → Log Analytics)
internal sealed class StructuredLlmAuditLogger(ILogger<StructuredLlmAuditLogger> logger)
    : ILlmAuditLogger
{
    public void Record(LlmAuditRecord entry) =>
        logger.LogInformation(
            "LLM call | {CallId} | {Provider} | {Model} | {UseCase} | {Symbol} | " +
            "tokens={PromptTokens}+{CompletionTokens} | latency={LatencyMs}ms | " +
            "outcome={Outcome} | advisory={IsAdvisory}",
            entry.CallId, entry.Provider, entry.Model, entry.UseCase, entry.Symbol,
            entry.PromptTokens, entry.CompletionTokens,
            (int)entry.Latency.TotalMilliseconds,
            entry.Outcome, entry.IsAdvisory);
}
```

### Token Budget in Gateway

```csharp
// ✅ Add to gateways — Microsoft.Extensions.AI ChatOptions (post ADR-003 migration)
private const int MaxOutputTokens = 500;

// In MEA ChatOptions after ADR-003:
var chatOptions = new ChatOptions
{
    MaxOutputTokens = MaxOutputTokens
};

// In current hand-rolled OpenAiResponsesRequest (pre-ADR-003):
// Set max_tokens field in the request body — implementation-specific to current gateway
```

---

## 14. Health Check Endpoint (Prerequisite for ADR-015 HEALTHCHECK)

The API already has `/health/live` and `/health/dependencies` — verified in `Program.cs` lines 245–255. **No code change needed.** Confirm the liveness endpoint returns 200 when the process is healthy:

```csharp
// ✅ ALREADY EXISTS — Program.cs line 245
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false  // liveness: always true if process is up
});
```

The `HEALTHCHECK` in the Dockerfile uses `/health/live` — this already works.

---

## 15. Package Version Alignment

### Execution project (9.0.0 → 10.0.0)

```xml
<!-- src/Mvp.Trading.Execution/Mvp.Trading.Execution.csproj -->
<!-- ❌ CURRENT -->
<PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="9.0.0" />

<!-- ✅ TARGET — align to SDK version -->
<PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="10.0.0" />
```

### Api project — swap Swashbuckle for native OpenAPI (optional, M18+)

```xml
<!-- Optional — reduces dependencies, aligns with .NET 10 native OpenAPI -->
<!-- Remove: -->
<PackageReference Include="Swashbuckle.AspNetCore" Version="6.6.2" />
<!-- Add: (built into .NET 10 SDK — no package needed for basic usage) -->
```

---

## Full NuGet Package Change Summary

| Project | Package | Action | Version |
|---------|---------|--------|---------|
| Api | `Microsoft.Extensions.Http.Resilience` | **Add** | 10.0.0 |
| Api | `Azure.Identity` | **Add** | 1.13.2 |
| Api | `Microsoft.Azure.StackExchangeRedis` | **Add** | 1.5.0 |
| Worker | `Microsoft.Extensions.Http.Resilience` | **Add** | 10.0.0 |
| Worker | `Azure.Identity` | **Add** | 1.13.2 |
| Worker | `Microsoft.Azure.StackExchangeRedis` | **Add** | 1.5.0 |
| Execution | `Microsoft.Extensions.Caching.Memory` | **Upgrade** | 9.0.0 → 10.0.0 |
| Tests | `Microsoft.Extensions.TimeProvider.Testing` | **Add** | 10.0.0 |

> **Note**: `Npgsql.Azure` does NOT exist as a separate package. `UseAzureADAuthentication()` is a built-in extension method in the core `Npgsql` package (8.x) — no additional NuGet reference needed.

---

## File Change Summary

| File | Changes |
|------|---------|
| `src/Mvp.Trading.Api/Program.cs` | AddOptions chain, TypedResults, FixedTimeEquals, TimeProvider.System, Guid.CreateVersion7, HttpClient resilience, Azure identity |
| `src/Mvp.Trading.Worker/Program.cs` | AddOptions chain, TimeProvider.System, HttpClient resilience, Azure identity |
| `src/Mvp.Trading.Execution/KillSwitchService.cs` | Fail-closed, TimeProvider injection |
| `src/Mvp.Trading.Api/Services/PostgresOptions.cs` | [Required] attribute |
| `src/Mvp.Trading.Api/Services/RedisOptions.cs` | [Required] attribute |
| `src/Mvp.Trading.Api/Services/TradingViewOptions.cs` | [Required] attribute |
| `src/Mvp.Trading.Api/Mcp/OpenAiOptions.cs` | [Required] on ApiKey |
| `src/Mvp.Trading.Api/Services/PostgresOpenTradeCommand.cs` | Guid.CreateVersion7, TimeProvider |
| `src/Mvp.Trading.Execution/PostgresOrderReceiptStore.cs` | Guid.CreateVersion7, TimeProvider |
| `src/Mvp.Trading.Worker/AlertWorker.cs` | Guid.CreateVersion7, TimeProvider |
| All 16 files with DateTimeOffset.UtcNow | TimeProvider injection |
| `scripts/db/init.sql` | Local-dev-only appuser grants if needed; Azure grants target managed identity database principals |
| `Dockerfile` | USER appuser, HEALTHCHECK |
| `Dockerfile.worker` | USER appuser, HEALTHCHECK |
| `docker-compose.yml` | Pin all image tags |
| `.github/workflows/ci.yml` | Extend existing restore/build/test workflow with secret scan, NuGet CVE gate, container scans, and SARIF uploads |
| `.github/dependabot.yml` | **New** — dependency automation |
| `src/Mvp.Trading.Contracts/Contracts/LlmAudit.cs` | **New** — audit record type |
| All csproj files above | Package adds/upgrades |
| `infra/` IaC source files | **New** — Terraform or Bicep source of truth created by M18.0 before Azure resource changes |

---

## Implementation Order (Respects M14 and M18 Dependencies)

```
Sprint 0 — IaC source reconstruction (hard blocker for Azure resource changes):
  0. Choose Terraform or Bicep as the single authoritative IaC path
  0.1 Reconstruct source for existing App Service API, ACI Worker/monitoring/Ollama, ACR, PostgreSQL, Redis, role assignments, diagnostics, and networking placeholders
  0.2 Import/reconcile live resources and validate clean plan/what-if with no unintended destroy/recreate
  0.3 Remove real secrets from local variable/parameter workflows and keep state/plan/secret files out of git

Sprint 1 — No-brainers (zero risk, no Azure infra needed):
  1. Options [Required] + AddOptions chain (all Program.cs files)
  2. TypedResults migration (Program.cs)
  3. CryptographicOperations.FixedTimeEquals (Program.cs line 141)
  4. KillSwitch fail-closed (KillSwitchService.cs line 61)
  5. Guid.CreateVersion7 (4 files)
  6. Pin docker-compose image tags
  7. Dockerfile USER + HEALTHCHECK
  8. .github/dependabot.yml
  9. Package version alignment (Execution: 9.0.0 → 10.0.0)

Sprint 2 — TimeProvider injection (16 files, systematic):
  10. Add TimeProvider.System to DI in both Program.cs
  11. Inject TimeProvider into each service constructor
  12. Replace all DateTime.UtcNow / DateTimeOffset.UtcNow calls

Sprint 3 — Resilience (after TimeProvider done):
  13. Add Microsoft.Extensions.Http.Resilience to Api + Worker csproj
  14. Chain .AddResilienceHandler on all HttpClient registrations

Sprint 4 — Azure Identity (requires M18.0 IaC source plus Azure infra from ADR-009/010/011):
  15. Add Azure.Identity, Microsoft.Azure.StackExchangeRedis (Npgsql built-in — no extra package)
  16. Conditional DefaultAzureCredential in NpgsqlDataSource factory
  17. Conditional Entra ID auth in Redis factory

Sprint 5 — CI Pipeline + Audit:
  18. .github/workflows/ci.yml (secret scan + build + CVE scan)
  19. LlmAuditRecord type + StructuredLlmAuditLogger
  20. Token budget in LLM gateways
```
