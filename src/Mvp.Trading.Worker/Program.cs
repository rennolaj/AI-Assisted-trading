using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;
using Mvp.Trading.Api.Mcp;
using Mvp.Trading.Integrations.Kraken;
using Mvp.Trading.Contracts;
using Mvp.Trading.Elliott;
using Mvp.Trading.Execution;
using Mvp.Trading.Indicators;
using Mvp.Trading.Risk;

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
            .AddJsonFile("config/symbol-mapping.json", optional: true, reloadOnChange: true)
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
        var marketDataOptions = builder.Configuration.GetSection("MarketData").Get<MarketDataOptions>() ?? new MarketDataOptions();
        builder.Services.AddSingleton(marketDataOptions);
        builder.Services.AddHttpClient<KrakenFuturesMarketDataProvider>();
        builder.Services.AddSingleton<KrakenFuturesMarketDataProvider>();
        builder.Services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<MarketDataOptions>();
            var fallback = sp.GetRequiredService<KrakenFuturesMarketDataProvider>();
            var logger = sp.GetRequiredService<ILogger<FixtureMarketDataProvider>>();
            return new FixtureMarketDataProvider(fallback, options, logger);
        });
        builder.Services.AddSingleton<IMarketDataProvider>(sp =>
        {
            var options = sp.GetRequiredService<MarketDataOptions>();
            if (options.UseFixtures)
            {
                return sp.GetRequiredService<FixtureMarketDataProvider>();
            }

            return sp.GetRequiredService<KrakenFuturesMarketDataProvider>();
        });
        builder.Services.AddHttpClient<KrakenFuturesTradingProvider>();
        builder.Services.AddSingleton<ITradingProvider>(sp => sp.GetRequiredService<KrakenFuturesTradingProvider>());

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
            LookbackDays = Math.Max(0, elliottRunOptions.LookbackDays),
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
        if (int.TryParse(builder.Configuration["Indicator:LookbackDays"], out var indicatorLookbackDays))
        {
            indicatorConfig = indicatorConfig with { LookbackDays = Math.Max(0, indicatorLookbackDays) };
        }

        if (int.TryParse(builder.Configuration["Indicator:LookbackBars"], out var indicatorLookbackBars))
        {
            indicatorConfig = indicatorConfig with { LookbackBars = Math.Max(1, indicatorLookbackBars) };
        }
        builder.Services.AddSingleton(indicatorConfig);
        builder.Services.AddSingleton<IndicatorEngine>();

        builder.Services.AddSingleton<IAccountStateProvider, FileAccountStateProvider>();
        builder.Services.AddSingleton<IInstrumentSpecProvider, FileInstrumentSpecProvider>();
        builder.Services.AddSingleton<IExecutionSettingsProvider, FileExecutionSettingsProvider>();
        builder.Services.AddSingleton<ITradePlanBuilder, TradePlanBuilder>();
        builder.Services.AddSingleton<ITradePlanStore, PostgresTradePlanStore>();
        builder.Services.AddSingleton<IExecutionIntentStore, PostgresExecutionIntentStore>();
        builder.Services.AddSingleton<IOrderReceiptStore, PostgresOrderReceiptStore>();
        builder.Services.AddSingleton<IExecutionHeartbeatStore, PostgresExecutionHeartbeatStore>();
        builder.Services.AddSingleton<IExecutionService, ExecutionService>();

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
