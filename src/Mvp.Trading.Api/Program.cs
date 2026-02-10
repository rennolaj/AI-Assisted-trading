using System.Reflection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Mvp.Trading.Api;
using Mvp.Trading.Api.Mcp;
using Mvp.Trading.Api.Models;
using Mvp.Trading.Api.Services;
using Mvp.Trading.Contracts;
using Mvp.Trading.Contracts.Telemetry;
using Mvp.Trading.Execution;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<TradingViewOptions>(builder.Configuration.GetSection("TradingView"));
builder.Services.Configure<PostgresOptions>(builder.Configuration.GetSection("Postgres"));
builder.Services.Configure<RedisOptions>(builder.Configuration.GetSection("Redis"));
builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection("OpenAI"));
builder.Services.Configure<LocalLlmOptions>(builder.Configuration.GetSection("LocalLlm"));
builder.Services.Configure<McpProviderOptions>(builder.Configuration.GetSection("McpProvider"));
builder.Services.AddHttpClient<IOpenAiResponsesClient, OpenAiResponsesClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<OpenAiOptions>>().Value;
    var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "https://api.openai.com/v1/" : options.BaseUrl;
    if (!baseUrl.EndsWith('/'))
    {
        baseUrl += "/";
    }

    client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
});
builder.Services.AddHttpClient<ILocalLlmResponsesClient, LocalLlmResponsesClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<LocalLlmOptions>>().Value;
    var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "http://localhost:11434/v1/" : options.BaseUrl;
    if (!baseUrl.EndsWith('/'))
    {
        baseUrl += "/";
    }

    client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
});
builder.Services.AddSingleton<IJsonSchemaValidator, JsonSchemaValidator>();
builder.Services.AddSingleton<OpenAiMcpGateway>();
builder.Services.AddSingleton<LocalLlmMcpGateway>();
builder.Services.AddSingleton<IMcpGateway, McpGatewayRouter>();
builder.Services.AddSingleton<IMcpConfigStore, FileMcpConfigStore>();
builder.Services.AddSingleton<IPolicyStore, FilePolicyStore>();
builder.Services.AddSingleton<IPromptTemplateStore, FilePromptTemplateStore>();
builder.Services.AddSingleton<IAlertQueue, RedisAlertQueue>();
builder.Services.AddSingleton<IAlertStore, PostgresAlertStore>();
builder.Services.AddSingleton<IIdempotencyStore, PostgresIdempotencyStore>();
builder.Services.AddSingleton<IAlertProcessingQuery, PostgresAlertProcessingQuery>();
builder.Services.AddSingleton<IOpenTradeCommand, PostgresOpenTradeCommand>();
builder.Services.AddSingleton<IIndicatorSnapshotQuery, PostgresIndicatorSnapshotQuery>();
builder.Services.AddSingleton<IElliottCandidatesQuery, PostgresElliottCandidatesQuery>();
builder.Services.AddSingleton<NpgsqlDataSource>(sp =>
{
    var options = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
    if (string.IsNullOrWhiteSpace(options.ConnectionString))
    {
        throw new InvalidOperationException("Postgres connection string is required (Postgres:ConnectionString).");
    }

    return new NpgsqlDataSourceBuilder(options.ConnectionString).Build();
});
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var options = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
    if (string.IsNullOrWhiteSpace(options.ConnectionString))
    {
        throw new InvalidOperationException("Redis connection string is required (Redis:ConnectionString).");
    }

    return ConnectionMultiplexer.Connect(options.ConnectionString);
});

// Kill Switch configuration and service
builder.Services.Configure<KillSwitchApiOptions>(builder.Configuration.GetSection("KillSwitch"));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IKillSwitchService>(sp =>
{
    var postgresOpts = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
    var cache = sp.GetRequiredService<IMemoryCache>();
    var tradingProvider = sp.GetRequiredService<ITradingProvider>();
    var logger = sp.GetRequiredService<ILogger<KillSwitchService>>();
    return new KillSwitchService(postgresOpts.ConnectionString, cache, tradingProvider, logger);
});

// OpenTelemetry metrics
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("mvp-trading-api"))
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("Mvp.Trading");
        metrics.AddAspNetCoreInstrumentation();
        metrics.AddHttpClientInstrumentation();
        metrics.AddPrometheusExporter();
    });

// Metrics service
builder.Services.AddSingleton<IMetricsService, OpenTelemetryMetricsService>();

// Health checks
builder.Services.AddHealthChecks()
    .AddNpgSql(sp => sp.GetRequiredService<IOptions<PostgresOptions>>().Value.ConnectionString!, name: "postgres")
    .AddRedis(sp => sp.GetRequiredService<IOptions<RedisOptions>>().Value.ConnectionString!, name: "redis");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    options.IncludeXmlComments(xmlPath);
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "MVP Trading API v1");
    options.DocumentTitle = "MVP Trading API";
});

app.MapPost("/webhooks/tradingview/{secret}", async (
        string secret,
        HttpRequest request,
        IAlertQueue queue,
        IAlertStore store,
        IIdempotencyStore idempotency,
        IOptions<TradingViewOptions> options,
        ILoggerFactory loggerFactory,
        CancellationToken ct) =>
    {
        var logger = loggerFactory.CreateLogger("WebhookIngress");
        var expectedSecret = options.Value.WebhookSecret;
        if (string.IsNullOrWhiteSpace(expectedSecret) || !string.Equals(secret, expectedSecret, StringComparison.Ordinal))
        {
            return Results.Unauthorized();
        }

        request.EnableBuffering();
        // Log header-level information to help debug real-world webhook deliveries (Content-Type, User-Agent, etc.)
            try
            {
                var headerMap = request.Headers.ToDictionary(h => h.Key, h => string.Join(',', h.Value.ToArray() ?? System.Array.Empty<string>()));
                logger.LogInformation("Incoming webhook headers: {Headers}", headerMap);
            }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enumerate request headers for logging.");
        }

        string rawPayload;
        using (var reader = new StreamReader(request.Body, leaveOpen: true))
        {
            rawPayload = await reader.ReadToEndAsync(ct);
            request.Body.Position = 0;
        }

        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return Results.BadRequest(new { error = "Empty payload." });
        }


        TradingViewWebhookPayload? payload = null;

        // Enforce JSON-only payloads for this server. Parse and fail fast on invalid JSON.
        var trimmedBody = rawPayload.Trim();
        try
        {
            // Normalize into a consistent shape even if producers vary in casing/structure.
            var normalized = TradingViewNormalizer.Normalize(trimmedBody);
            payload = new TradingViewWebhookPayload(
                normalized.IdempotencyKey,
                normalized.Ticker,
                normalized.Exchange,
                normalized.Interval,
                normalized.Close,
                normalized.Volume,
                normalized.DirectionHint,
                normalized.SymbolHint,
                normalized.Reason
            );
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Expected JSON payload but normalization failed.");
            return Results.BadRequest(new { error = ex.Message });
        }

        if (payload is null)
        {
            return Results.BadRequest(new { error = "Missing payload data." });
        }

        if (string.IsNullOrWhiteSpace(payload.IdempotencyKey))
        {
            return Results.BadRequest(new { error = "IdempotencyKey is required." });
        }

        if (!idempotency.TryAdd(payload.IdempotencyKey))
        {
            logger.LogInformation("Duplicate alert ignored for idempotency key {IdempotencyKey}.", payload.IdempotencyKey);
            return Results.Ok(new { status = "duplicate_ignored" });
        }

        var alert = new AlertEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "tradingview",
            payload.IdempotencyKey,
            new TradingViewFields(payload.Ticker, payload.Exchange, payload.Interval, payload.Close, payload.Volume),
            new IntentFields(payload.DirectionHint, payload.SymbolHint, payload.Reason),
            rawPayload
        );

        await store.StoreAsync(rawPayload, alert, ct);
        await queue.EnqueueAsync(alert, ct);

        return Results.Accepted(value: new { status = "enqueued" });
    })
    .WithName("TradingViewWebhook")
    .WithTags("Webhook")
    .WithSummary("Accept TradingView webhook alerts and enqueue them for processing.")
    .WithDescription("Validates the shared secret, normalizes the payload, enforces idempotency, and enqueues the alert.");

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("HealthCheck")
    .WithTags("System")
    .WithSummary("Basic health check")
    .WithDescription("Confirms the API process is running.");

app.MapHealthChecks("/health/dependencies")
    .WithName("DependencyHealthCheck")
    .WithTags("System")
    .WithSummary("Dependency health check")
    .WithDescription("Verifies Postgres and Redis connectivity using ASP.NET Core health checks.");

app.MapHealthChecks("/health/ready")
    .WithName("ReadinessCheck")
    .WithTags("System")
    .WithSummary("Readiness probe")
    .WithDescription("Kubernetes readiness probe - checks if service can accept traffic.");

app.MapHealthChecks("/health/live")
    .WithName("LivenessCheck")
    .WithTags("System")
    .WithSummary("Liveness probe")
    .WithDescription("Kubernetes liveness probe - checks if service is alive and should not be restarted.");

app.MapPrometheusScrapingEndpoint()
    .WithTags("Metrics");

app.MapGet("/alerts/status/{idempotencyKey}", async (
        string idempotencyKey,
        IAlertProcessingQuery query,
        CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(new { error = "IdempotencyKey is required." });
        }

        var status = await query.GetByIdempotencyKeyAsync(idempotencyKey, ct);
        return status is null ? Results.NotFound() : Results.Ok(status);
    })
    .WithName("AlertProcessingStatus")
    .WithTags("Alerts")
    .WithSummary("Get processing status for an alert.")
    .WithDescription("Returns the latest processing status for the given idempotency key.");

app.MapGet("/alerts/{alertId:guid}/indicator-snapshot", async (
        Guid alertId,
        IIndicatorSnapshotQuery query,
        CancellationToken ct) =>
    {
        var json = await query.GetJsonByAlertIdAsync(alertId, ct);
        return json is null ? Results.NotFound() : Results.Text(json, "application/json");
    })
    .WithName("IndicatorSnapshot")
    .WithTags("Indicators")
    .WithSummary("Get indicator snapshot for an alert.")
    .WithDescription("Returns the stored indicator snapshot JSON for the given alert id.");

app.MapGet("/alerts/{alertId:guid}/elliott-candidates", async (
        Guid alertId,
        IElliottCandidatesQuery query,
        CancellationToken ct) =>
    {
        var json = await query.GetJsonByAlertIdAsync(alertId, ct);
        return json is null ? Results.NotFound() : Results.Text(json, "application/json");
    })
    .WithName("ElliottCandidates")
    .WithTags("Elliott")
    .WithSummary("Get Elliott candidates for an alert.")
    .WithDescription("Returns the stored Elliott candidate JSON for the given alert id.");

app.MapPost("/trades/open", async (
        OpenTradeRequest request,
        IOpenTradeCommand command,
        CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(request.ExchangeId) ||
            string.IsNullOrWhiteSpace(request.Symbol) ||
            string.IsNullOrWhiteSpace(request.Side))
        {
            return Results.BadRequest(new { error = "ExchangeId, Symbol, and Side are required." });
        }

        var tradeId = await command.CreateOpenTradeAsync(request, ct);
        return Results.Created($"/trades/open/{tradeId}", new { tradeId });
    })
    .WithName("CreateOpenTrade")
    .WithTags("Trades")
    .WithSummary("Seed an open trade for monitoring.")
    .WithDescription("Creates an open trade record that the monitor worker will validate for invalidation.");

app.Run();
