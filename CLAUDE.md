# Claude Multi-Agent Operating Contract

> **Auto-read by GitHub Copilot CLI (Claude) when working in this repository.**
> Defines the multi-agent workflow, C# 14 / .NET 10 standards, and all known
> anti-patterns discovered during the M14 technical review.
> Any agent working on this repo must read this file first.

---

## Project Identity

| Property | Value |
|----------|-------|
| Name | AI-Assisted Trading Server |
| Repo | `rennolaj/AI-Assisted-trading` |
| Path | `/Users/jrennola/Hobby/AI-Assisted` |
| SDK | `10.0.101` (pinned via `global.json`) |
| Framework | `net10.0` — all 8 projects via `Directory.Build.props` |
| Language | **C# 14** (default for net10.0; no `LangVersion` override) |
| Nullable | `enable` (global) |
| ImplicitUsings | `enable` (global) |

### Source Projects
```
src/Mvp.Trading.Api             — ASP.NET Core 10 Minimal API, webhook ingestion
src/Mvp.Trading.Contracts       — Shared records/interfaces (81 sealed records)
src/Mvp.Trading.Elliott         — Elliott Wave analysis engine
src/Mvp.Trading.Execution       — Order execution, KillSwitch, persistence stores
src/Mvp.Trading.Indicators      — IndicatorMath, IndicatorEngine
src/Mvp.Trading.Integrations.Kraken — Kraken futures HTTP client, rate limiting
src/Mvp.Trading.Risk            — Risk evaluation
src/Mvp.Trading.Worker          — BackgroundService workers (AlertWorker, ReconciliationWorker)
tests/                          — xUnit test projects
```

---

## C# and .NET Standards

**ALWAYS read `docs/development/csharp-dotnet10-skill.md` before making any C# or .NET decisions.**
It is the authoritative reference for this codebase covering:
- All C# 12/13/14 language features with rules and code examples
- .NET 10 runtime APIs (TimeProvider, Guid.CreateVersion7, Base64Url, SearchValues, Task.WhenEach, LINQ)
- ASP.NET Core 10 patterns (TypedResults, Minimal API)
- IOptions validation patterns
- CancellationToken rules
- Error handling rules
- Logging rules
- Security rules
- Performance rules
- Project structure rules

---

## M14 Anti-Patterns — DO NOT Introduce or Worsen

These patterns were found during the M14 technical review and are tracked in `docs/backlog/backlog.md`.
Never introduce new instances of these anti-patterns when writing code for this repository.

### 🔴 CRITICAL — Financial / Security Risk

**CT-1: CancellationToken not forwarded through delegate**
```csharp
// ❌ FOUND: ExecutionService.cs ~line 366
Func<Task<Result<OrderAck>>> action = ...;
await action();  // token never passed → retry loop cannot be cancelled

// ✅ CORRECT
Func<CancellationToken, Task<Result<OrderAck>>> action = ...;
await action(ct);
```

**CT-2/3: CT ignored in Redis and queue operations**
```csharp
// ❌ FOUND: RedisAlertQueue.cs, AlertWorker.cs
await _db.ListRightPushAsync(key, value);         // no ct
await _db.ListLeftPopAsync(key);                  // no ct

// ✅ CORRECT — always pass ct to StackExchange.Redis async calls
await _db.ListRightPushAsync(key, value, flags: CommandFlags.None);
// or use the overload accepting CancellationToken where available
```

**KS-1: Fail-open on DB unreachable (KillSwitch)**
```csharp
// ❌ FOUND: KillSwitchService.cs ~lines 42-81
// If DB unreachable AND cache miss → returns inactive kill switch → trades execute
catch (Exception)
{
    return KillSwitchStatus.Inactive;  // WRONG: fails-open = financial loss
}

// ✅ CORRECT: fail-closed = safe
catch (Exception ex)
{
    _logger.LogError(ex, "KillSwitch DB failure — defaulting to EMERGENCY_STOP");
    return new KillSwitchStatus(active: true, level: KillSwitchLevel.EmergencyStop);
}
```

**RES-1: No resilience policies on HTTP clients**
```csharp
// ❌ FOUND: zero Polly policies on OpenAI, LocalLLM, Kraken clients
builder.Services.AddHttpClient<IKrakenClient, KrakenClient>();  // bare client

// ✅ CORRECT
builder.Services.AddHttpClient<IKrakenClient, KrakenClient>()
    .AddStandardResilienceHandler();  // Microsoft.Extensions.Http.Resilience
```

**SEC-1: Credentials committed to Git**
- `.env.prod.local`, `.env.demo.local`, `.env.smoke.fixtures` contained real keys
- NEVER commit `.env*.local` files; ensure `.gitignore` covers them
- Use environment variable injection or secret manager

**SEC-2: String equality for secret comparison**
```csharp
// ❌ FOUND: Program.cs ~line 141, KillSwitchController.cs ~lines 37, 57
if (secret == expectedSecret)  // timing attack vulnerable

// ✅ CORRECT
if (CryptographicOperations.FixedTimeEquals(
    Encoding.UTF8.GetBytes(secret),
    Encoding.UTF8.GetBytes(expectedSecret)))
```

### 🟠 HIGH — Must Fix Before Shipping

**CT-4: `CreateCommand` without CancellationToken (14+ Postgres files)**
```csharp
// ❌ FOUND: PostgresOpenTradeRepository, PostgresAlertStore, etc.
await using var cmd = _dataSource.CreateCommand(sql);  // no CT

// ✅ CORRECT — existing pattern in PostgresReconciliationStore
await using var conn = await _dataSource.OpenConnectionAsync(ct);
await using var cmd = conn.CreateCommand();
cmd.CommandText = sql;
```

**CT-5: CT not passed through MCP router**
```csharp
// ❌ FOUND: McpGatewayRouter.ExecuteWithDefaultAsync ~line 79
// Private method receives no CancellationToken → LLM calls uncancellable
private async Task<T> ExecuteWithDefaultAsync(Func<Task<T>> action)

// ✅ CORRECT
private async Task<T> ExecuteWithDefaultAsync(Func<CancellationToken, Task<T>> action, CancellationToken ct)
```

**OPT-1: IOptions bindings without ValidateOnStart**
```csharp
// ❌ FOUND: all Configure<T> calls in Program.cs
services.Configure<KrakenOptions>(config.GetSection("Kraken"));

// ✅ CORRECT
services.AddOptions<KrakenOptions>()
    .BindConfiguration("Kraken")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

**OPT-2: Missing [Required] on Options classes**
```csharp
// ❌ FOUND: all *Options / *Settings classes have no validation attributes
public class KrakenOptions { public string ApiKey { get; set; } = ""; }

// ✅ CORRECT
public class KrakenOptions
{
    [Required] public string ApiKey { get; set; } = "";
    [Required] public string ApiSecret { get; set; } = "";
}
```

**ARCH-1: All types public instead of internal**
```csharp
// ❌ FOUND: all Option/Store/Query/Engine implementations are public
public class PostgresAlertStore : IAlertStore { }

// ✅ CORRECT — internal unless part of Contracts project
internal sealed class PostgresAlertStore : IAlertStore { }
```

**SEC-3: Containers run as root**
```dockerfile
# ❌ FOUND: Dockerfile, Dockerfile.worker — no USER directive
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# ✅ CORRECT
RUN useradd -m -u 1000 appuser && chown -R appuser:appuser /app
USER appuser
```

### 🟡 MEDIUM — Quality and Performance

**PERF-1: `DateTime.UtcNow` used directly (40+ sites)**
```csharp
// ❌ FOUND: AlertWorker, KrakenRateLimitBudget, KillSwitchService, etc.
var now = DateTime.UtcNow;

// ✅ CORRECT — inject TimeProvider, enables testing
public MyService(TimeProvider time) { _time = time; }
var now = _time.GetUtcNow();

// In DI registration:
services.AddSingleton(TimeProvider.System);
```

**PERF-2: `JsonSerializerOptions` created per-call**
```csharp
// ❌ FOUND: 11+ files
var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { ... });

// ✅ CORRECT
private static readonly JsonSerializerOptions _opts = new() { ... };
var json = JsonSerializer.Serialize(obj, _opts);
```

**PERF-3: Double-materialization on candle arrays**
```csharp
// ❌ FOUND: KrakenFuturesMarketDataProvider ~lines 294, 302, 306, 419
var slice = list.Skip(n).ToList();  // already List<T> — allocates second list

// ✅ CORRECT
var slice = list[n..];  // slice notation — zero allocation
```

**PERF-4: Intermediate ToList() on indicator inputs**
```csharp
// ❌ FOUND: IndicatorEngine ~lines 142-143
var closes = candles.Select(c => c.Close).ToList();   // materializes before Span
var volumes = candles.Select(c => c.Volume).ToList();

// ✅ CORRECT — after M14.6.1 IndicatorMath accepts ReadOnlySpan<decimal>
ReadOnlySpan<decimal> closes = candles.Select(c => c.Close).ToArray();
```

**ERR-1: Silent failure on persistence errors**
```csharp
// ❌ FOUND: PostgresAlertStore.StoreAsync, PostgresExecutionIntentStore.SaveAsync
public async Task StoreAsync(Alert alert) { ... }  // plain Task, throws on failure

// ✅ CORRECT — caller must handle failure explicitly
public async Task<Result<bool>> StoreAsync(Alert alert) { ... }
```

**ERR-2: Fixed retry delay (thundering herd)**
```csharp
// ❌ FOUND: ExecutionService ~line 356
await Task.Delay(200);  // fixed 200ms between all retries

// ✅ CORRECT — exponential backoff with jitter
var delay = Math.Min(1000, 100 * (1 << attempt)) + Random.Shared.Next(0, 100);
await Task.Delay(delay, ct);
```

**LOCK-1: `object` used as lock primitive (C# 13 available)**
```csharp
// ❌ FOUND: KrakenFuturesRateLimitBudget.cs, FixtureMarketDataProvider.cs
private readonly object _gate = new();

// ✅ CORRECT — System.Threading.Lock (C# 13/.NET 9+)
private readonly Lock _gate = new();
```

**CONFIG-1: Magic config section strings**
```csharp
// ❌ FOUND: 15+ places in Program.cs
config.GetSection("Kraken")
config.GetSection("OpenAi")

// ✅ CORRECT
internal static class ConfigurationKeys
{
    public const string Kraken = "Kraken";
    public const string OpenAi = "OpenAi";
}
```

**GUID-1: Guid.NewGuid() for DB-persisted entity IDs**
```csharp
// ❌ FOUND: PostgresOpenTradeCommand, PostgresOrderReceiptStore, AlertWorker, Program.cs
var tradeId = Guid.NewGuid();  // random — no DB ordering benefit

// ✅ CORRECT for DB primary keys (.NET 10)
var tradeId = Guid.CreateVersion7();  // monotonic — improves index locality
// Do NOT change non-persisted correlation IDs
```

**DOCKER-1: Unpinned image tags**
```dockerfile
# ❌ FOUND: docker-compose.yml
image: ngrok/ngrok:latest
image: prom/prometheus:latest

# ✅ CORRECT
image: ngrok/ngrok:3.10.0
image: prom/prometheus:v2.53.0
```

### ✅ Already Compliant — Maintain These Patterns

- Pattern matching: use `is null` / `is not null` / switch expressions — **zero old-style casts**
- Record DTOs: all data transfer objects are `sealed record` — maintain this
- File-scoped namespaces: 149/150 files — **always use file-scoped**
- String interpolation: **never `string.Format()`** — use `$"..."`
- LINQ: **no `Where().Count()`** — use `Count(predicate)` directly
- Result envelope: **always return `Result<T>`** at service boundaries, never throw across layers
- `IReadOnlyList<T>` for public collection return types
- `TryGetValue` pattern for dictionary access — **no `ContainsKey + []`**
- IMemoryCache: always set explicit TTL
- OperationCanceledException: **always re-throw**, never swallow
- Global usings are active — never add redundant `using System;` blocks

---

## Multi-Agent Contract

### Roles

| Role | Responsibility |
|------|---------------|
| `planner` | Deterministic implementation plan, risks, acceptance checks, stop conditions |
| `builder` | Implements feature scope; runs restore/build/test; writes report |
| `reviewer` | Code review with severity; file + line references; findings in JSON |
| `quality` | Quality standards check against M14 anti-patterns; actionable findings |
| `tester` | Runs test suite; validates coverage; writes verdict |
| `integrator` | Validates integration/dataflow contracts; cross-component impact |
| `orchestrator` | Final PASS / CHANGES REQUIRED decision; triggers followup bugs |

### Global Policies

- **NO_PUSH**: never run `git push` — the human decides when to push
- **INFRA_FREEZE**: never modify Terraform or Bicep infrastructure files
- **SCOPE**: keep changes inside the declared feature scope
- **BUILD_GATE**: always verify `./scripts/build.sh` passes before marking done
- **TEST_GATE**: always verify `./scripts/test.sh` passes before marking done
- **SKILL_REF**: always read `docs/development/csharp-dotnet10-skill.md` before writing C# code

Every agent output must end with:
```
Policy check: NO_PUSH=confirmed, INFRA_FREEZE=confirmed, SCOPE=confirmed
Blocked items: <none | list>
```

### Stage Order (Claude-Native)

```
You (Copilot CLI / orchestrator)
  ├─ 1. task[planner]       background → read_agent(wait:true)
  ├─ 2. task[rubber-duck]   sync → validate planner output before builder starts
  ├─ 3. task[builder]       background → read_agent(wait:true)
  ├─ 4a. task[code-review]  background ─┐  (parallel)
  ├─ 4b. task[quality]      background ─┘  read both when notified
  ├─ 5. task[tester]        background → read_agent(wait:true)
  ├─ 6. task[integrator]    background → read_agent(wait:true)
  └─ 7. orchestrator decision → checkpoint + optional create-followup-bugs.sh
```

**Gate rules (Claude-native — do NOT use file polling):**
- After `task[planner]` completes, rubber-duck critiques plan before builder starts
- After `task[builder]` completes, launch code-review + quality in the SAME response (parallel)
- After BOTH code-review AND quality complete, launch tester
- After tester completes, launch integrator
- After integrator completes, make final decision

## Branch Model

**One branch per feature — all agents share it.**

- Feature branch: `feature/<scope>` (branched from `main`)
- **Only the builder makes commits** — all other roles are read-only
- Orchestrator coordinates from `main`
- After the builder commits, it saves a diff artifact that every downstream agent reads instead of switching branches:

```bash
git diff main..HEAD > /tmp/multi-agent-sync/<scope>/outbox/builder.diff
```

This means:
- There is exactly **1 branch** per feature scope in the repository
- Reviewer, quality, and integrator never touch git — they read `outbox/builder.diff`
- The human can follow all work in a single clean branch history

### Handoff Bus

```
/tmp/multi-agent-sync/<scope>/
  context.md              ← feature brief (written before run)
  inbox/<role>.md         ← per-role instructions (written by bootstrap)
  outbox/<role>.md        ← human-readable report (written by each agent)
  outbox/<role>.json      ← machine-readable findings (written by each agent)
  outbox/builder.diff     ← unified diff saved by builder after commit; read by reviewer/quality/integrator
  state/<role>.done       ← completion marker (write when done)
```

Bootstrap the bus before any run:
```bash
./scripts/agents/bootstrap-feature-claude.sh --scope <scope-id> --base main
```

### Agent Output Contract

Every agent MUST write two files when complete:

**`outbox/<role>.md`** — human-readable:
```markdown
## <ROLE> Report — <scope>

### Summary
<what was done>

### Files Changed
- path/to/file.cs — what changed

### Commands Run
- `./scripts/build.sh` — PASS
- `./scripts/test.sh` — PASS (47 tests)

### Findings
| Severity | File | Line | Issue | Action |
|----------|------|------|-------|--------|

Policy check: NO_PUSH=confirmed, INFRA_FREEZE=confirmed, SCOPE=confirmed
Blocked items: none
```

**`outbox/<role>.json`** — machine-readable:
```json
{
  "role": "<role>",
  "scope": "<scope>",
  "status": "success|changes_required|blocked|failed",
  "summary": "one-line summary",
  "blocking": false,
  "findings": [
    {
      "severity": "critical|high|medium|low|info",
      "title": "issue title",
      "file": "src/Mvp.Trading.X/Y.cs",
      "line": 42,
      "action": "required fix description"
    }
  ],
  "commands": [
    { "cmd": "dotnet build", "result": "pass", "details": "" }
  ],
  "artifacts": {
    "outbox_md": "/tmp/multi-agent-sync/<scope>/outbox/<role>.md"
  }
}
```

### Role Instructions (Claude Task Tool Idiom)

#### planner
```
Read: context.md, inbox/planner.md, docs/development/csharp-dotnet10-skill.md, docs/backlog/backlog.md
Produce:
  1. Exact list of files to create/modify with reason
  2. Implementation steps in order
  3. Risks and stop conditions
  4. Acceptance checks (how to verify done)
  5. Estimate effort and flag any M14 anti-patterns risk
Write: outbox/planner.md, outbox/planner.json
Create: state/planner.done
```

#### builder
```
Read: context.md, inbox/builder.md, outbox/planner.md, docs/development/csharp-dotnet10-skill.md
Do:
  1. Use bash tool to run: ./scripts/restore.sh
  2. Implement all changes using edit/create tools
  3. Use bash tool to run: ./scripts/build.sh — must PASS
  4. Use bash tool to run: ./scripts/test.sh — must PASS
  5. Verify no M14 anti-patterns introduced (check CLAUDE.md anti-pattern list)
  6. Commit to feature branch (DO NOT PUSH):
       git add -A && git commit -m "feat(<scope>): <short summary>"
  7. Save diff artifact for reviewer/quality/integrator:
       git diff main..HEAD > /tmp/multi-agent-sync/<scope>/outbox/builder.diff
Write: outbox/builder.md (include build + test output), outbox/builder.json
Create: state/builder.done
```

#### reviewer (use code-review agent type)
```
Read: context.md, inbox/reviewer.md, outbox/builder.diff (primary), outbox/builder.md (context)
Do NOT checkout any branch or make any commits.
Do:
  1. Review outbox/builder.diff — the full unified diff of all builder changes
  2. Check against C# 14 / .NET 10 standards from docs/development/csharp-dotnet10-skill.md
  3. Check against every M14 anti-pattern in CLAUDE.md — flag any new instances
  4. Rate each finding: critical / high / medium / low / info
  5. Only block on critical or high findings
Write: outbox/reviewer.md, outbox/reviewer.json
Create: state/reviewer.done
```

#### quality
```
Read: context.md, inbox/quality.md, outbox/builder.diff (primary), outbox/builder.md (context)
Do NOT checkout any branch or make any commits.
Do:
  1. Check naming (internal vs public, Async suffix, I-prefix interfaces)
  2. Check Result<T> usage at all service boundaries
  3. Check CancellationToken propagation in all new async methods
  4. Check IOptions<T> uses ValidateOnStart
  5. Check no new DateTime.UtcNow introduced (must use TimeProvider)
  6. Check no new JsonSerializerOptions() per-call
  7. Check no raw exception propagation across domain boundaries
Output QUALITY_STATUS: PASS | CHANGES_REQUIRED
Write: outbox/quality.md, outbox/quality.json
Create: state/quality.done
```

#### tester
```
Read: context.md, inbox/tester.md, outbox/reviewer.md, outbox/quality.md
Do:
  1. Run: ./scripts/restore.sh && ./scripts/build.sh && ./scripts/test.sh
  2. Verify changed behavior has test coverage
  3. Verify no regressions
  4. Verify CancellationToken paths tested
  5. Verify failure/error paths tested (not just happy path)
Write: outbox/tester.md (include full test output), outbox/tester.json
Create: state/tester.done
```

#### integrator
```
Read: context.md, inbox/integrator.md, outbox/tester.md
Read: outbox/builder.diff  ← use for dataflow impact assessment (no branch switching)
Do:
  1. Trace the full alert dataflow:
     TradingView webhook → normalization → Redis queue → AlertWorker →
     indicators → Elliott → MCP adjudication → trade plan → execution → persistence
  2. Verify changed components respect dataflow contracts
  3. Check happy path and fail-closed path
  4. Verify no new circular dependencies
  5. Verify interface contracts in Contracts project unchanged (or documented)
Do NOT make any commits.
Write: outbox/integrator.md, outbox/integrator.json
Create: state/integrator.done
```

#### orchestrator (you / Copilot CLI)
```
Read: all outbox/*.md files
Decide:
  - PASS: all stages green, no blocking findings
  - CHANGES REQUIRED: list exact items, assign to next iteration
Do:
  - Write final decision to outbox/orchestrator.md
  - If blocking findings: run scripts/agents/create-followup-bugs.sh --scope <scope>
  - Create state/orchestrator.done
  - Save session checkpoint
```

---

## Backlog Integration

- All feature stories are tracked in `docs/backlog/backlog.md` under the relevant milestone
- M14 sub-areas: M14.1 (Language), M14.2 (Runtime API), M14.3 (DI/Config),
  M14.4 (CancellationToken), M14.5 (Error Handling), M14.6 (Performance),
  M14.7 (Testing), M14.8 (Security), M14.9 (Architecture), M14.10 (Remediation Report)
- When a story is started: update SQL todo status to `in_progress`
- When a story is done: update SQL todo status to `done`
- When blocking findings are found: `create-followup-bugs.sh` appends to backlog automatically

---

## Claude Setup Scripts

```bash
# Bootstrap the sync bus for a feature (no worktrees needed)
./scripts/agents/bootstrap-feature-claude.sh --scope <scope-id> --base main

# Write the feature context
nano /tmp/multi-agent-sync/<scope-id>/context.md

# Run one coordinated Claude pass
./scripts/agents/run-feature-once-claude.sh --scope <scope-id>

# After run: generate followup bugs if needed
./scripts/agents/create-followup-bugs.sh --scope <scope-id>
```

---

## AO Integration (if using Agent Orchestrator with Claude support)

```yaml
# claude-orchestrator.yaml
defaults:
  agent: claude
  runtime: copilot-cli
projects:
  AI-Assisted:
    path: /Users/jrennola/Hobby/AI-Assisted
    defaultBranch: main
```

If AO gains native Claude support, use:
```bash
ao start
./scripts/agents/run-feature-once-claude.sh --scope <scope-id> --agent claude
```
