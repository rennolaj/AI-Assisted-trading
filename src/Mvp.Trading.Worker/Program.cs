using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using StackExchange.Redis;
using Mvp.Trading.Integrations.Kraken;
using Mvp.Trading.Contracts;
using Mvp.Trading.Elliott;
using Mvp.Trading.Indicators;

namespace Mvp.Trading.Worker;

/// <summary>
/// Entry point for the alert processing worker.
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Configuration
            .AddJsonFile("config/kraken-futures.json", optional: true, reloadOnChange: true)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        builder.Services.Configure<WorkerOptions>(options =>
        {
            var redisSection = builder.Configuration.GetSection("Redis");
            options.RedisConnectionString = redisSection["ConnectionString"] ?? string.Empty;
            options.AlertQueueKey = redisSection["AlertQueueKey"] ?? "mvp:alerts";
            options.PollIntervalMs = int.TryParse(builder.Configuration["Worker:PollIntervalMs"], out var ms) ? ms : 500;
            options.TradeMonitorIntervalMs = int.TryParse(builder.Configuration["Worker:TradeMonitorIntervalMs"], out var monitorMs)
                ? monitorMs
                : 2000;
        });

        var krakenOptions = builder.Configuration.GetSection("KrakenFutures").Get<KrakenFuturesOptions>() ?? new KrakenFuturesOptions();
        ApplyKrakenEnvironmentOverrides(builder.Configuration, krakenOptions);
        builder.Services.AddSingleton(krakenOptions);
        var symbolMappingOptions = builder.Configuration.GetSection("SymbolMapping").Get<SymbolMappingOptions>() ?? new SymbolMappingOptions();
        builder.Services.AddSingleton(symbolMappingOptions);
        builder.Services.AddSingleton<SymbolMapper>();
        var krakenCacheOptions = builder.Configuration.GetSection("KrakenFutures:Cache").Get<KrakenFuturesCacheOptions>() ?? new KrakenFuturesCacheOptions();
        var krakenRateLimitOptions = builder.Configuration.GetSection("KrakenFutures:RateLimit").Get<KrakenFuturesRateLimitOptions>() ?? new KrakenFuturesRateLimitOptions();
        builder.Services.AddSingleton(krakenCacheOptions);
        builder.Services.AddSingleton(krakenRateLimitOptions);
        builder.Services.AddSingleton<KrakenFuturesRateLimitBudget>();
        builder.Services.AddMemoryCache();
        builder.Services.AddHttpClient<KrakenFuturesMarketDataProvider>();
        builder.Services.AddSingleton<IMarketDataProvider>(sp => sp.GetRequiredService<KrakenFuturesMarketDataProvider>());

        var elliottRunOptions = builder.Configuration.GetSection("Elliott").Get<ElliottRunOptions>() ?? new ElliottRunOptions();
        var elliottBaseTimeframe = ParseTimeframe(elliottRunOptions.BaseTimeframe, Timeframe.M15);
        var parameterOptions = elliottRunOptions.Parameters ?? new ElliottParametersOptions();
        var elliottParameters = new ElliottParameters(
            string.IsNullOrWhiteSpace(parameterOptions.PivotMethod) ? "ZigZag" : parameterOptions.PivotMethod,
            parameterOptions.Depth > 0 ? parameterOptions.Depth : 12,
            parameterOptions.DeviationPct > 0m ? parameterOptions.DeviationPct : 5m,
            parameterOptions.MaxCandidates > 0 ? parameterOptions.MaxCandidates : 10);

        var tickOverrides = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in elliottRunOptions.TickSizeOverrides)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key))
            {
                tickOverrides[pair.Key] = pair.Value;
            }
        }

        var elliottOptions = new ElliottOptions
        {
            TickSizeFallback = elliottRunOptions.TickSizeFallback,
            TickSizeOverrides = tickOverrides
        };

        builder.Services.AddSingleton(elliottOptions);
        builder.Services.AddSingleton(new ElliottRunConfig(elliottBaseTimeframe, elliottParameters));
        builder.Services.AddSingleton<IPivotExtractor, ZigZagPivotExtractor>();
        builder.Services.AddSingleton<ImpulseCandidateBuilder>();
        builder.Services.AddSingleton<CandidateScorer>();
        builder.Services.AddSingleton<InvalidationCalculator>();
        builder.Services.AddSingleton<IElliottEngine, ElliottEngine>();

        var indicatorMode = builder.Configuration["Indicator:Mode"] ?? IndicatorDefaults.ScalpingMode;
        var indicatorConfig = IndicatorDefaults.ForMode(indicatorMode);
        builder.Services.AddSingleton(indicatorConfig);
        builder.Services.AddSingleton<IndicatorEngine>();

        builder.Services.AddSingleton<NpgsqlDataSource>(sp =>
        {
            var connectionString = builder.Configuration.GetSection("Postgres")["ConnectionString"];
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("Postgres connection string is required (Postgres:ConnectionString).");
            }

            return new NpgsqlDataSourceBuilder(connectionString).Build();
        });

        builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkerOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.RedisConnectionString))
            {
                throw new InvalidOperationException("Redis connection string is required (Redis:ConnectionString)." );
            }

            return ConnectionMultiplexer.Connect(options.RedisConnectionString);
        });

        builder.Services.AddSingleton<IAlertProcessingStore, PostgresAlertProcessingStore>();
        builder.Services.AddSingleton<IIndicatorSnapshotStore, PostgresIndicatorSnapshotStore>();
        builder.Services.AddSingleton<IElliottCandidatesStore, PostgresElliottCandidatesStore>();
        builder.Services.AddSingleton<IOpenTradeRepository, PostgresOpenTradeRepository>();
        builder.Services.AddHostedService<AlertWorker>();
        builder.Services.AddHostedService<TradeMonitorWorker>();

        await builder.Build().RunAsync();
    }

    private static void ApplyKrakenEnvironmentOverrides(IConfiguration configuration, KrakenFuturesOptions options)
    {
        var environment = options.Environment?.Trim();
        if (string.IsNullOrWhiteSpace(environment))
        {
            return;
        }

        var normalized = NormalizeEnvironment(environment);
        var envSection = configuration.GetSection($"KrakenFutures:Environments:{normalized}");
        var envOptions = envSection.Get<KrakenFuturesEnvironmentOptions>();
        if (envOptions is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(envOptions.BaseUrl))
        {
            options.BaseUrl = envOptions.BaseUrl;
        }

        if (!string.IsNullOrWhiteSpace(envOptions.AuthBaseUrl))
        {
            options.AuthBaseUrl = envOptions.AuthBaseUrl;
        }

        if (!string.IsNullOrWhiteSpace(envOptions.WebSocketUrl))
        {
            options.WebSocketUrl = envOptions.WebSocketUrl;
        }

        if (!string.IsNullOrWhiteSpace(envOptions.TestSymbol))
        {
            options.TestSymbol = envOptions.TestSymbol;
        }
    }

    private static string NormalizeEnvironment(string environment)
    {
        return environment.Trim().ToLowerInvariant() switch
        {
            "production" => "prod",
            "prod" => "prod",
            "sandbox" => "demo",
            "demo" => "demo",
            _ => environment.Trim().ToLowerInvariant()
        };
    }

    private static Timeframe ParseTimeframe(string? value, Timeframe fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim().ToUpperInvariant();
        return normalized switch
        {
            "1" => Timeframe.M1,
            "5" => Timeframe.M5,
            "15" => Timeframe.M15,
            "30" => Timeframe.M30,
            "60" or "1H" or "H1" => Timeframe.H1,
            "120" or "2H" or "H2" => Timeframe.H2,
            "240" or "4H" or "H4" => Timeframe.H4,
            "720" or "12H" or "H12" => Timeframe.H12,
            "D" or "1D" or "D1" => Timeframe.D1,
            _ when Enum.TryParse<Timeframe>(normalized, true, out var parsed) => parsed,
            _ => fallback
        };
    }
}
