using System.Reflection;
using System.Text.Json;
using Mvp.Trading.Api.Models;
using Mvp.Trading.Api.Services;
using Mvp.Trading.Contracts;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<TradingViewOptions>(builder.Configuration.GetSection("TradingView"));
builder.Services.Configure<PostgresOptions>(builder.Configuration.GetSection("Postgres"));
builder.Services.Configure<RedisOptions>(builder.Configuration.GetSection("Redis"));
builder.Services.AddSingleton<IAlertQueue, RedisAlertQueue>();
builder.Services.AddSingleton<IAlertStore, PostgresAlertStore>();
builder.Services.AddSingleton<IIdempotencyStore, PostgresIdempotencyStore>();
builder.Services.AddSingleton<IAlertProcessingQuery, PostgresAlertProcessingQuery>();
builder.Services.AddSingleton<IOpenTradeCommand, PostgresOpenTradeCommand>();
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

        TradingViewWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TradingViewWebhookPayload>(rawPayload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "Invalid JSON." });
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

app.MapGet("/health/dependencies", async (
        NpgsqlDataSource dataSource,
        IConnectionMultiplexer redis,
        CancellationToken ct) =>
    {
        try
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "select 1";
            await cmd.ExecuteScalarAsync(ct);

            await redis.GetDatabase().PingAsync();

            return Results.Ok(new { status = "ok" });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Dependency health check failed");
        }
    })
    .WithName("DependencyHealthCheck")
    .WithTags("System")
    .WithSummary("Dependency health check")
    .WithDescription("Verifies Postgres and Redis connectivity.");

app.Run();
