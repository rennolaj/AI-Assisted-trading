# C# 14 / .NET 10 Agent Skill Reference

> **Purpose**: Authoritative reference for any agent working on this codebase.
> All agents must use this document when making C# or .NET decisions.
> Keep this file updated as the project evolves.

---

## Project Baseline

| Property | Value |
|----------|-------|
| SDK | `10.0.101` (pinned via `global.json`, `rollForward: latestFeature`) |
| Target Framework | `net10.0` (all 8 projects via `Directory.Build.props`) |
| Language Version | **C# 14** (default for `net10.0` — no `LangVersion` override anywhere) |
| Nullable | `enable` (global) |
| ImplicitUsings | `enable` (global) |
| TreatWarningsAsErrors | **NOT SET** (backlog item M14.1.J) |

All 8 projects inherit from `Directory.Build.props`. Individual `.csproj` files only override assembly name, root namespace, references, and packages.

---

## C# 14 Language Features

### 1. `field` keyword — semi-auto properties
Eliminates backing field for properties with simple custom logic.
```csharp
// ✅ C# 14 preferred
public string Symbol
{
    get;
    set => field = value.ToUpperInvariant();
}

// ❌ Old pattern
private string _symbol = "";
public string Symbol
{
    get => _symbol;
    set => _symbol = value.ToUpperInvariant();
}
```
**Rule**: Use `field` whenever a property has a simple setter transformation and no other logic referencing the backing field directly.
**Avoid**: When the backing field is accessed by name elsewhere in the class.

---

### 2. `nameof` with unbound generics
```csharp
nameof(Result<>)        // "Result"
nameof(IReadOnlyList<>) // "IReadOnlyList"
```
**Rule**: Use in exception messages, logging, and attribute arguments when referencing generic types by name.

---

### 3. Null-conditional assignment
```csharp
// ✅ C# 14
trade?.StopLoss = newStop;

// ❌ Old pattern
if (trade != null)
    trade.StopLoss = newStop;
```
**Rule**: Use for optional object property updates. Do NOT use when the null case requires logging or fallback logic — use explicit null check in that case.

---

### 4. Implicit span conversions
Arrays implicitly convert to `ReadOnlySpan<T>` / `Span<T>` — no explicit cast needed.
```csharp
decimal[] prices = [1.0m, 2.0m, 3.0m];
// ✅ No cast needed — array implicitly becomes ReadOnlySpan<decimal>
var rsi = IndicatorMath.ComputeRsi(prices, period: 14);
```
**Rule**: When writing methods that accept `ReadOnlySpan<T>`, callers can pass arrays directly. Prefer `ReadOnlySpan<T>` over `IReadOnlyList<T>` in computation-heavy library methods (Elliott, Indicators, Risk).

---

### 5. Extension members (new syntax)
Extends extension methods to support properties, indexers, and static members.
```csharp
// ✅ C# 14 extension property
extension(IReadOnlyList<Candle> candles)
{
    public bool IsEmpty => candles.Count == 0;
    public Candle Latest => candles[^1];
    public decimal TypicalPrice => (Latest.High + Latest.Low + Latest.Close) / 3m;
}
```
**Rule**: Prefer extension members over helper static methods for domain-meaningful operations on existing types. Keep extension classes in the project that owns the domain type.

---

### 6. `partial` constructors and events
Extends the partial member pattern (C# 13 added partial properties).
```csharp
partial class AlertWorker
{
    partial void OnAlertReceived(AlertEvent alert);
    partial void OnAlertProcessed(AlertEvent alert, Result<TradePlan> result);
}
```
**Rule**: Use for cross-cutting hooks in large partial classes after refactoring. Do not use as a substitute for proper service decomposition.

---

### 7. `params ReadOnlySpan<T>` (improved from C# 13)
`params` now works with `ReadOnlySpan<T>` — zero allocation at call site.
```csharp
// ✅ No array allocation
void LogSymbols(params ReadOnlySpan<string> symbols) { ... }
LogSymbols("BTC", "ETH", "SOL");
```
**Rule**: Prefer `params ReadOnlySpan<T>` over `params T[]` for new methods where callers supply a variable number of value-typed or string arguments.

---

## C# 13 Language Features (available since .NET 9 — also fully available in .NET 10)

### 8. `params` collections (any collection type)
```csharp
void ProcessCandidates(params IEnumerable<ElliottCandidate> candidates) { ... }
void ProcessCandidates(params List<ElliottCandidate> candidates) { ... }
```

### 9. `System.Threading.Lock` — new lock type
```csharp
// ✅ C# 13 preferred
private readonly Lock _lock = new();
lock (_lock) { ... }

// ❌ Old pattern
private readonly object _lock = new();
lock (_lock) { ... }
```
**Rule**: Use `System.Threading.Lock` for all new lock objects. Do NOT use `object` as a lock target in new code.

### 10. `\e` escape sequence
```csharp
// ✅ C# 13
const char Esc = '\e';

// ❌ Old
const char Esc = '\u001b';
```

### 11. `allows ref struct` generic constraint
```csharp
static T Process<T>(T value) where T : allows ref struct { ... }
```
**Rule**: Use when writing generic utilities that must accept `ref struct` types like `Span<T>`, `ReadOnlySpan<T>`.

### 12. `[OverloadResolutionPriority]`
```csharp
[OverloadResolutionPriority(1)]
public void Process(ReadOnlySpan<decimal> values) { ... }
public void Process(IReadOnlyList<decimal> values) { ... } // lower priority
```
**Rule**: Use when providing both a high-performance `Span<T>` overload and a compatibility `IReadOnlyList<T>` overload, to prefer the span variant automatically.

### 13. `ref` and `unsafe` in iterators and async methods
```csharp
async Task ProcessAsync()
{
    unsafe
    {
        // now allowed in async methods
    }
}
```

### 14. `partial` properties and indexers
```csharp
partial class RiskPolicy
{
    public partial decimal MaxLossPercent { get; set; }
}
```

---

## C# 12 Language Features (available since .NET 8 — fully available in .NET 10)

### 15. Primary constructors
```csharp
// ✅ C# 12 preferred for simple DI injection
public sealed class IndicatorEngine(
    IMarketDataProvider marketData,
    ILogger<IndicatorEngine> logger) : IIndicatorEngine
{
    // No explicit field declarations needed for simple pass-through
}
```
**Rule**: Use primary constructors when the constructor body ONLY assigns parameters to fields. Do NOT use if the constructor has validation logic, null checks, or computed initialization.

### 16. Collection expressions
```csharp
// ✅ C# 12
int[] values = [1, 2, 3];
List<string> symbols = ["BTC", "ETH"];
ReadOnlySpan<int> span = [1, 2, 3];
var combined = [..first, ..second]; // spread

// ❌ Old
int[] values = new[] { 1, 2, 3 };
List<string> symbols = new List<string> { "BTC", "ETH" };
```
**Rule**: Always use collection expressions for array/list/span literals. Use spread `[..a, ..b]` for concatenation instead of `Concat`.

### 17. Inline arrays
```csharp
[System.Runtime.CompilerServices.InlineArray(8)]
struct EightCandles { private Candle _element0; }
```
**Rule**: Use for fixed-size buffer scenarios in performance-critical hot paths (e.g., indicator lookback windows).

---

## .NET 10 Runtime APIs — Key Additions

### LINQ
```csharp
// CountBy — replaces GroupBy().ToDictionary()
var alertsBySymbol = alerts.CountBy(a => a.Symbol);

// AggregateBy — keyed aggregation
var totalSize = trades.AggregateBy(t => t.Symbol, 0m, (acc, t) => acc + t.Size);

// Index() — replaces Select((x, i) => ...)
foreach (var (i, candle) in candles.Index())
    Console.WriteLine($"[{i}] {candle.Close}");
```

### Task.WhenEach
```csharp
// Process tasks as they complete (like await foreach on parallel tasks)
await foreach (var result in Task.WhenEach(timeframeTasks))
    ProcessResult(result);
```
**Rule**: Use in `IndicatorEngine` multi-timeframe processing instead of `Task.WhenAll` + loop, when results can be processed independently as they arrive.

### TimeProvider (stabilized .NET 8+, widely adopted in .NET 10)
```csharp
// ✅ Testable
public sealed class KillSwitchService(TimeProvider time)
{
    public DateTimeOffset Now => time.GetUtcNow();
}

// ❌ Untestable
public DateTimeOffset Now => DateTimeOffset.UtcNow;
```
**Rule**: NEVER use `DateTime.UtcNow` or `DateTimeOffset.UtcNow` directly. Always inject `TimeProvider` and call `timeProvider.GetUtcNow()`. Register `TimeProvider.System` in DI. Use `FakeTimeProvider` (from `Microsoft.Extensions.TimeProvider.Testing`) in tests.

### Base64Url
```csharp
// ✅ .NET 10 — no manual replace needed
string encoded = Base64Url.EncodeToString(bytes);
byte[] decoded = Base64Url.DecodeFromChars(encoded);

// ❌ Old workaround
Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
```

### Guid.CreateVersion7()
```csharp
// ✅ Time-sortable UUID — ideal for DB primary keys
var id = Guid.CreateVersion7(); // monotonically increasing, globally unique
```
**Rule**: Use `Guid.CreateVersion7()` for new entity IDs inserted into Postgres. Time-sortable GUIDs eliminate index fragmentation.

### SearchValues<T>
```csharp
private static readonly SearchValues<char> _invalidChars = SearchValues.Create("/<>\\|");

// ✅ High-performance multi-char search
bool hasInvalid = symbol.AsSpan().IndexOfAny(_invalidChars) >= 0;
```
**Rule**: Use `SearchValues<char>` or `SearchValues<string>` for any multi-value membership checks in hot paths.

### CryptographicOperations (fixed-time comparison)
```csharp
// ✅ Timing-attack safe
bool valid = CryptographicOperations.FixedTimeEquals(
    Encoding.UTF8.GetBytes(provided),
    Encoding.UTF8.GetBytes(expected));

// ❌ Vulnerable to timing attacks
bool valid = provided == expected;
```
**Rule**: ALWAYS use `CryptographicOperations.FixedTimeEquals` for secret/token/API-key comparison. Never use `==` or `string.Equals` for security-sensitive comparisons.

### System.Threading.Lock
```csharp
// ✅ .NET 9+/.NET 10
private readonly Lock _syncRoot = new();
lock (_syncRoot) { ... }
```
**Rule**: All new lock objects must use `System.Threading.Lock`, not `object`.

---

## .NET 10 ASP.NET Core

### Native OpenAPI (replaces Swashbuckle)
```csharp
// ✅ .NET 10 — no Swashbuckle needed
builder.Services.AddOpenApi();
app.MapOpenApi();
```
**Rule**: New endpoints use `Microsoft.AspNetCore.OpenApi` (built-in). Evaluate migrating from Swashbuckle when time permits.

### TypedResults (preferred over Results)
```csharp
// ✅ Preferred — generates correct OpenAPI schema, AOT safe
app.MapPost("/webhook", () => TypedResults.Accepted());
app.MapGet("/health", () => TypedResults.Ok(new HealthStatus()));

// ❌ Dynamic — no compile-time schema inference
app.MapPost("/webhook", () => Results.Accepted());
```
**Rule**: Always use `TypedResults.*` in Minimal API endpoint handlers, not `Results.*`.

### Route groups
```csharp
var api = app.MapGroup("/api").RequireAuthorization();
var trades = api.MapGroup("/trades");
trades.MapGet("/open", GetOpenTrades);
trades.MapPost("/close", CloseTrade);
```
**Rule**: Group related endpoints with `MapGroup` to share middleware, auth, and prefix configuration.

---

## Microsoft.Extensions.Http.Resilience (.NET 8+ / .NET 10)

```csharp
// ✅ Standard resilience pipeline
builder.Services.AddHttpClient<KrakenFuturesTradingProvider>()
    .AddStandardResilienceHandler(); // retry + circuit breaker + timeout

// ✅ Custom pipeline
builder.Services.AddHttpClient<OpenAiMcpGateway>()
    .AddResilienceHandler("openai", pipeline =>
    {
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(500),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true
        });
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            MinimumThroughput = 5,
            BreakDuration = TimeSpan.FromSeconds(30)
        });
        pipeline.AddTimeout(TimeSpan.FromSeconds(10));
    });
```
**Rule**: Every `AddHttpClient<T>()` registration MUST chain `.AddStandardResilienceHandler()` or a custom `.AddResilienceHandler(...)`. No bare `AddHttpClient` without resilience.

---

## IOptions<T> Pattern

| Variant | Lifetime | Use when |
|---------|----------|----------|
| `IOptions<T>` | Singleton snapshot | Value never changes at runtime |
| `IOptionsMonitor<T>` | Singleton, live reload | Config changes without restart (e.g., kill switch policy) |
| `IOptionsSnapshot<T>` | Scoped (per-request) | Per-request config (do NOT inject into singletons) |

**Rule**: All `services.Configure<T>()` bindings MUST chain `.ValidateDataAnnotations().ValidateOnStart()`.
```csharp
services.Configure<PostgresOptions>(config.GetSection(ConfigKeys.Postgres))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

---

## Async / CancellationToken Rules

1. **Every `async Task` / `async Task<T>` method must accept `CancellationToken ct`** — no exceptions outside event handlers and `Main`.
2. **Every downstream async call must forward `ct`** — `await SomethingAsync(ct)`, never `await SomethingAsync()`.
3. **Never use `CancellationToken.None`** inside a method that has a `ct` parameter.
4. **Never use `async void`** outside event handlers.
5. **Never use `.Result` or `.Wait()`** — always `await`.
6. **Postgres pattern** — always use `OpenConnectionAsync(ct)`, never `CreateCommand()` without CT:
    ```csharp
    // ✅ Correct
    await using var conn = await _dataSource.OpenConnectionAsync(ct);
    await using var cmd = new NpgsqlCommand(sql, conn);
    await cmd.ExecuteNonQueryAsync(ct);

    // ❌ Wrong — CreateCommand has no CT support
    await using var cmd = _dataSource.CreateCommand(sql);
    await cmd.ExecuteNonQueryAsync(ct);
    ```
7. **Library projects** (Elliott, Indicators, Risk, Integrations.Kraken) must use `.ConfigureAwait(false)` on all awaits.

---

## Error Handling Rules

1. **All service boundaries return `Result<T>`** — never throw across domain boundaries.
2. **`OperationCanceledException` is always re-thrown** — never swallowed.
3. **Fail-closed for all safety checks**: kill switch unavailable → assume ACTIVE (block trades); Redis unavailable → return 503; DB write fails → do not mark as processed.
4. **No empty catch blocks** — every catch must log at minimum `_logger.LogError(ex, ...)`.
5. **`CryptographicOperations.FixedTimeEquals`** for all secret comparisons.

---

## Logging Rules

1. **Structured properties only** — `_logger.LogInformation("Processing {Symbol}", symbol)` not `$"Processing {symbol}"`.
2. **`[LoggerMessage]` partial methods** for any log call in a loop or hot path.
3. **Never log secrets, API keys, passwords, full connection strings, or raw headers**.
4. **`LogDebug` / `LogTrace`** for per-tick/per-alert detail; `LogInformation` for business events; `LogWarning` for recoverable issues; `LogError` for failures.

---

## Security Rules

1. **No secrets in source files** — all secrets via environment variables only.
2. **`.env.*.local` files must never be committed** — enforced by `.gitignore` + pre-commit hook.
3. **`CryptographicOperations.FixedTimeEquals`** for webhook and kill switch secret validation.
4. **Non-root Docker containers** — `USER appuser` in all Dockerfiles.
5. **Pinned Docker image tags** — never `:latest`.
6. **`dotnet list package --vulnerable`** must pass clean in CI.

---

## Performance Rules

1. **`ReadOnlySpan<T>` / `Span<T>`** for computation-heavy methods (IndicatorMath, ZigZagPivotExtractor).
2. **`static readonly` `JsonSerializerOptions`** — never `new JsonSerializerOptions()` per call.
3. **Pre-size collections** when count is known: `new List<T>(capacity)`.
4. **`TryGetValue`** for all dictionary lookups — never `ContainsKey + []`.
5. **No `.Skip(n).ToList()`** on already-materialized lists — use slice `[n..]`.
6. **`SearchValues<T>`** for multi-value membership checks in hot paths.
7. **`Guid.CreateVersion7()`** for new DB entity IDs.

---

## Project Structure Rules

1. **Dependency direction**: `Contracts` ← `Elliott`, `Indicators`, `Risk`, `Execution`, `Integrations.Kraken` ← `Worker`, `Api`
2. **No circular dependencies** — verify with dependency graph on any new project reference.
3. **Implementation types are `internal`** — only contracts and interfaces are `public`.
4. **`InternalsVisibleTo`** in `.csproj` for test project access.
5. **Max class size**: 300 lines; **max method size**: 50 lines; **max constructor dependencies**: 7.
6. **All `HttpClient` registrations** must include `.AddStandardResilienceHandler()`.
7. **All `services.Configure<T>()`** must include `.ValidateDataAnnotations().ValidateOnStart()`.
