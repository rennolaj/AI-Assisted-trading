using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using StackExchange.Redis;
using Mvp.Trading.Api.Mcp;
using Mvp.Trading.Integrations.Kraken;
using Mvp.Trading.Contracts;
using Mvp.Trading.Contracts.Telemetry;
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
        var baseParameters = BuildElliottParameters(parameterOptions, null);

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

        var profileSelection = BuildProfileSelection(elliottRunOptions, baseParameters);

        builder.Services.AddSingleton(elliottOptions);
        builder.Services.AddSingleton(new ElliottRunConfig(elliottBaseTimeframe, baseParameters, profileSelection));
        builder.Services.AddSingleton<IPivotExtractor, ZigZagPivotExtractor>();
        builder.Services.AddSingleton<ImpulseCandidateBuilder>();
        builder.Services.AddSingleton<CandidateScorer>();
        builder.Services.AddSingleton<InvalidationCalculator>();
        builder.Services.AddSingleton<IElliottEngine, ElliottEngine>();

        var indicatorMode = builder.Configuration["Indicator:Mode"] ?? IndicatorDefaults.ScalpingMode;
        var indicatorConfig = IndicatorDefaults.ForMode(indicatorMode);
        var lookbackDaysByTimeframe = ParseIndicatorLookbackMap(builder.Configuration, "Indicator:LookbackDaysByTimeframe");
        var lookbackBarsByTimeframe = ParseIndicatorLookbackMap(builder.Configuration, "Indicator:LookbackBarsByTimeframe");
        if (int.TryParse(builder.Configuration["Indicator:LookbackDays"], out var indicatorLookbackDays))
        {
            indicatorConfig = indicatorConfig with { LookbackDays = Math.Max(0, indicatorLookbackDays) };
        }

        if (int.TryParse(builder.Configuration["Indicator:LookbackBars"], out var indicatorLookbackBars))
        {
            indicatorConfig = indicatorConfig with { LookbackBars = Math.Max(1, indicatorLookbackBars) };
        }

        indicatorConfig = indicatorConfig with
        {
            LookbackDaysByTimeframe = lookbackDaysByTimeframe,
            LookbackBarsByTimeframe = lookbackBarsByTimeframe
        };
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

        // Reconciliation services
        builder.Services.Configure<ReconciliationOptions>(builder.Configuration.GetSection("Reconciliation"));
        builder.Services.AddSingleton<IReconciliationStore, PostgresReconciliationStore>();
        builder.Services.AddSingleton<IReconciliationService, ReconciliationService>();

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

        // Kill Switch service
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<IKillSwitchService>(sp =>
        {
            var connectionString = builder.Configuration.GetSection("Postgres")["ConnectionString"];
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("Postgres connection string is required for kill switch.");
            }
            
            var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
            var tradingProvider = sp.GetRequiredService<ITradingProvider>();
            var logger = sp.GetRequiredService<ILogger<KillSwitchService>>();
            return new KillSwitchService(connectionString, cache, tradingProvider, logger);
        });

        // OpenTelemetry metrics
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("mvp-trading-worker"))
            .WithMetrics(metrics =>
            {
                metrics.AddMeter("Mvp.Trading");
                metrics.AddPrometheusHttpListener(options => options.UriPrefixes = new[] { "http://localhost:9464/" });
            });

        // Metrics service
        builder.Services.AddSingleton<IMetricsService, OpenTelemetryMetricsService>();

        builder.Services.AddHostedService<AlertWorker>();
        builder.Services.AddHostedService<TradeMonitorWorker>();
        builder.Services.AddHostedService<ReconciliationWorker>();

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

        if (!string.IsNullOrWhiteSpace(envOptions.ChartsBaseUrl))
        {
            options.ChartsBaseUrl = envOptions.ChartsBaseUrl;
        }

        if (!string.IsNullOrWhiteSpace(envOptions.TestSymbol))
        {
            options.TestSymbol = envOptions.TestSymbol;
        }

        if (normalized == "demo")
        {
            if (!string.IsNullOrWhiteSpace(options.DemoApiKey))
            {
                options.ApiKey = options.DemoApiKey;
            }

            if (!string.IsNullOrWhiteSpace(options.DemoApiSecret))
            {
                options.ApiSecret = options.DemoApiSecret;
            }
        }
        else if (normalized == "prod")
        {
            if (!string.IsNullOrWhiteSpace(options.ProdApiKey))
            {
                options.ApiKey = options.ProdApiKey;
            }

            if (!string.IsNullOrWhiteSpace(options.ProdApiSecret))
            {
                options.ApiSecret = options.ProdApiSecret;
            }
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

    private static ElliottParameters BuildElliottParameters(ElliottParametersOptions options, string? profileName)
    {
        return new ElliottParameters(
            string.IsNullOrWhiteSpace(options.PivotMethod) ? "ZigZag" : options.PivotMethod,
            options.Depth > 0 ? options.Depth : 12,
            options.DeviationPct > 0m ? options.DeviationPct : 5m,
            options.MaxCandidates > 0 ? options.MaxCandidates : 10,
            profileName);
    }

    private static ElliottProfileSelection BuildProfileSelection(
        ElliottRunOptions options,
        ElliottParameters baseParameters)
    {
        var selectionOptions = options.ProfileSelection ?? new ElliottProfileSelectionOptions();
        var profiles = new Dictionary<string, ElliottParameters>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in options.Profiles)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            var name = pair.Key.Trim();
            var profileOptions = pair.Value ?? new ElliottParametersOptions();
            profiles[name] = BuildElliottParameters(profileOptions, name);
        }

        var defaultProfile = string.IsNullOrWhiteSpace(selectionOptions.DefaultProfile)
            ? "default"
            : selectionOptions.DefaultProfile.Trim();

        if (!profiles.ContainsKey(defaultProfile))
        {
            profiles[defaultProfile] = baseParameters with { ProfileName = defaultProfile };
        }

        var fallbackProfile = string.IsNullOrWhiteSpace(selectionOptions.FallbackProfile)
            ? null
            : selectionOptions.FallbackProfile.Trim();

        var riskMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in selectionOptions.RiskCategoryMap)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            riskMap[pair.Key.Trim()] = pair.Value.Trim();
        }

        return new ElliottProfileSelection(defaultProfile, fallbackProfile, riskMap, profiles);
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

    private static IReadOnlyDictionary<Timeframe, int> ParseIndicatorLookbackMap(
        IConfiguration configuration,
        string sectionKey)
    {
        var raw = configuration.GetSection(sectionKey).Get<Dictionary<string, int>>()
            ?? new Dictionary<string, int>();
        var result = new Dictionary<Timeframe, int>();

        foreach (var pair in raw)
        {
            if (pair.Value <= 0)
            {
                continue;
            }

            if (!TryParseTimeframe(pair.Key, out var timeframe))
            {
                continue;
            }

            result[timeframe] = pair.Value;
        }

        return result;
    }

    private static bool TryParseTimeframe(string? value, out Timeframe timeframe)
    {
        timeframe = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToUpperInvariant();
        switch (normalized)
        {
            case "1":
            case "M1":
                timeframe = Timeframe.M1;
                return true;
            case "5":
            case "M5":
                timeframe = Timeframe.M5;
                return true;
            case "15":
            case "M15":
                timeframe = Timeframe.M15;
                return true;
            case "30":
            case "M30":
                timeframe = Timeframe.M30;
                return true;
            case "60":
            case "1H":
            case "H1":
                timeframe = Timeframe.H1;
                return true;
            case "120":
            case "2H":
            case "H2":
                timeframe = Timeframe.H2;
                return true;
            case "240":
            case "4H":
            case "H4":
                timeframe = Timeframe.H4;
                return true;
            case "720":
            case "12H":
            case "H12":
                timeframe = Timeframe.H12;
                return true;
            case "D":
            case "1D":
            case "D1":
                timeframe = Timeframe.D1;
                return true;
        }

        if (Enum.TryParse<Timeframe>(normalized, true, out var parsed))
        {
            timeframe = parsed;
            return true;
        }

        return false;
    }
}
