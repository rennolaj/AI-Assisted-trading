using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mvp.Trading.Api.Mcp;
using Mvp.Trading.Api.Services;
using Mvp.Trading.Contracts;
using Mvp.Trading.Elliott;
using Mvp.Trading.Execution;
using Mvp.Trading.Indicators;
using Mvp.Trading.Risk;
using Mvp.Trading.Worker;
using Npgsql;
using NpgsqlTypes;
using StackExchange.Redis;
using Xunit;

namespace Mvp.Trading.Indicators.Tests;

/// <summary>
/// End-to-end pipeline integration test (opt-in via env vars).
/// </summary>
public sealed class ElliottPipelineIntegrationTests
{
    private const string EnabledEnv = "PIPELINE_INTEGRATION_TESTS";
    private const string PostgresEnv = "POSTGRES_CONNECTION_STRING";
    private const string RedisEnv = "REDIS_CONNECTION_STRING";
    private const string QueueEnv = "ALERT_QUEUE_KEY";

    [Fact]
    public async Task Pipeline_Alert_To_Indicator_And_Elliott_Persists()
    {
        if (!IsEnabled())
        {
            return;
        }

        var postgres = Environment.GetEnvironmentVariable(PostgresEnv);
        var redis = Environment.GetEnvironmentVariable(RedisEnv);
        if (string.IsNullOrWhiteSpace(postgres) || string.IsNullOrWhiteSpace(redis))
        {
            return;
        }

        var queueKey = Environment.GetEnvironmentVariable(QueueEnv) ?? "mvp:alerts";

        await using var dataSource = new NpgsqlDataSourceBuilder(postgres).Build();
        await EnsureSchemaAsync(dataSource);

        using var redisConnection = await ConnectionMultiplexer.ConnectAsync(redis);
        var db = redisConnection.GetDatabase();

        var alertId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid().ToString("N");
        await InsertAlertAsync(dataSource, alertId, idempotencyKey);

        var alert = BuildAlert(alertId, idempotencyKey);
        var payload = JsonSerializer.Serialize(alert);
        await db.ListRightPushAsync(queueKey, payload);

        var fixture = LoadFixture("fixtures/kraken-futures/btcusd_p_m1_varied.json");
        var marketData = new FixtureMarketDataProvider(fixture.Candles);

        var indicatorConfig = BuildIndicatorConfig(fixture.Candles.Count);
        var indicatorEngine = new IndicatorEngine(marketData, indicatorConfig);

        var elliottOptions = new ElliottOptions
        {
            MinBars = 10,
            MaxBars = 200,
            MinPivotCount = 4
        };

        var elliottEngine = new ElliottEngine(
            marketData,
            elliottOptions,
            new ZigZagPivotExtractor(elliottOptions),
            new ImpulseCandidateBuilder(elliottOptions),
            new CandidateScorer(elliottOptions),
            new InvalidationCalculator(elliottOptions));

        var baseParameters = new ElliottParameters("ZigZag", Depth: 1, DeviationPct: 0.05m, MaxCandidates: 10, ProfileName: "default");
        var profileSelection = new ElliottProfileSelection(
            "default",
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, ElliottParameters>(StringComparer.OrdinalIgnoreCase)
            {
                ["default"] = baseParameters
            });

        var elliottConfig = new ElliottRunConfig(Timeframe.M1, baseParameters, profileSelection);

        var workerOptions = Options.Create(new WorkerOptions
        {
            RedisConnectionString = redis,
            AlertQueueKey = queueKey,
            PollIntervalMs = 50,
            TradeMonitorIntervalMs = 1000
        });

        var symbolMapper = new SymbolMapper(new SymbolMappingOptions());

        var processingStore = new PostgresAlertProcessingStore(dataSource);
        var snapshotStore = new PostgresIndicatorSnapshotStore(dataSource);
        var elliottStore = new PostgresElliottCandidatesStore(dataSource);
        var logger = LoggerFactory.Create(builder => { }).CreateLogger<AlertWorker>();

        var mcpGateway = new StubMcpGateway();
        var policyStore = new StubPolicyStore();
        var mcpConfigStore = new StubMcpConfigStore();
        var tradePlanBuilder = new StubTradePlanBuilder();
        var executionService = new StubExecutionService();
        var mcpOptions = Options.Create(new McpProviderOptions());
        var killSwitchService = new StubKillSwitchService();

        var worker = new AlertWorker(
            redisConnection,
            workerOptions,
            processingStore,
            indicatorEngine,
            snapshotStore,
            elliottEngine,
            elliottStore,
            elliottConfig,
            mcpGateway,
            policyStore,
            mcpConfigStore,
            tradePlanBuilder,
            executionService,
            mcpOptions,
            killSwitchService,
            symbolMapper,
            logger);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            var indicatorQuery = new PostgresIndicatorSnapshotQuery(dataSource);
            var elliottQuery = new PostgresElliottCandidatesQuery(dataSource);

            var success = await WaitForAsync(async () =>
            {
                var snapshotJson = await indicatorQuery.GetJsonByAlertIdAsync(alertId, CancellationToken.None);
                var candidatesJson = await elliottQuery.GetJsonByAlertIdAsync(alertId, CancellationToken.None);
                return snapshotJson is not null && candidatesJson is not null;
            }, TimeSpan.FromSeconds(10));

            Assert.True(success, "Pipeline did not persist indicator snapshot and Elliott candidates in time.");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            await CleanupAsync(dataSource, alertId, idempotencyKey);
            await db.ListRemoveAsync(queueKey, payload, 0);
        }
    }

    private static bool IsEnabled()
    {
        return string.Equals(Environment.GetEnvironmentVariable(EnabledEnv), "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Environment.GetEnvironmentVariable(EnabledEnv), "true", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> WaitForAsync(Func<Task<bool>> predicate, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await predicate())
            {
                return true;
            }

            await Task.Delay(200);
        }

        return false;
    }

    private static AlertEvent BuildAlert(Guid alertId, string idempotencyKey)
    {
        return new AlertEvent(
            alertId,
            DateTimeOffset.UtcNow,
            "pipeline-test",
            idempotencyKey,
            new TradingViewFields("BTCUSD.P", "krakenfutures", "1", null, null),
            new IntentFields("LONG", "BTCUSD.P", "pipeline"),
            "{}");
    }

    private static IndicatorConfig BuildIndicatorConfig(int lookbackBars)
    {
        return new IndicatorConfig(
            Mode: "pipeline_test",
            Timeframes: new[] { Timeframe.M1 },
            AnchorTimeframe: Timeframe.M1,
            TrendTimeframe: Timeframe.M1,
            LookbackBars: Math.Max(lookbackBars, 30),
            LookbackDays: 0,
            LookbackBarsByTimeframe: new Dictionary<Timeframe, int>(),
            LookbackDaysByTimeframe: new Dictionary<Timeframe, int>(),
            EvaluationWindowMinutes: 60,
            EvaluationIntervalMinutes: 1,
            SnapshotPrecision: 6,
            Parameters: new IndicatorParameters(14, 14, 12, 26, 9, new VolumeRule("SMA_RATIO", 20, 1.5m)),
            Thresholds: new IndicatorThresholds(30m, 70m, 20m, 80m),
            Weights: new IndicatorWeights(1, 1, 1, 1, 1),
            RiskProfiles: new[]
            {
                new IndicatorRiskProfile("LOW", 0, 100, 0, false, true, 1m, "ALLOW")
            },
            StochRsiKPeriod: 3,
            StochRsiDPeriod: 3);
    }

    private static KrakenFixture LoadFixture(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Fixture not found: {path}");
        }

        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var symbol = root.GetProperty("symbol").GetString() ?? "BTCUSD.P";
        var interval = root.GetProperty("intervalMinutes").GetInt32();
        var candles = new List<Candle>();

        foreach (var item in root.GetProperty("candles").EnumerateArray())
        {
            var values = item.EnumerateArray().ToArray();
            if (values.Length < 6)
            {
                continue;
            }

            var tsSeconds = ReadLong(values[0]);
            var open = ReadDecimal(values[1]);
            var high = ReadDecimal(values[2]);
            var low = ReadDecimal(values[3]);
            var close = ReadDecimal(values[4]);
            var volume = values.Length > 6 ? ReadDecimal(values[6]) : 0m;

            var openTime = DateTimeOffset.FromUnixTimeSeconds(tsSeconds);
            candles.Add(new Candle(openTime, open, high, low, close, volume));
        }

        candles = candles.OrderBy(c => c.OpenTimeUtc).ToList();

        return new KrakenFixture(symbol, interval, candles);
    }

    private static long ReadLong(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetInt64(),
            JsonValueKind.String when long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            _ => 0L
        };
    }

    private static decimal ReadDecimal(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) => value,
            _ => 0m
        };
    }

    private static async Task EnsureSchemaAsync(NpgsqlDataSource dataSource)
    {
        const string sql = @"
create table if not exists idempotency_keys (
    idempotency_key text primary key,
    first_seen_utc timestamptz not null
);
create table if not exists alerts (
    alert_id uuid primary key,
    idempotency_key text not null references idempotency_keys(idempotency_key),
    received_at_utc timestamptz not null,
    source text not null,
    raw_payload text not null,
    alert_json jsonb not null
);
create table if not exists alert_processing (
    alert_id uuid primary key references alerts(alert_id),
    idempotency_key text not null,
    status text not null,
    last_updated_utc timestamptz not null,
    error_message text
);
create table if not exists indicator_snapshots (
    alert_id uuid primary key references alerts(alert_id),
    correlation_id uuid not null,
    computed_at_utc timestamptz not null,
    evaluation_time_utc timestamptz not null,
    symbol text not null,
    mode text not null,
    direction text not null,
    snapshot_json jsonb not null
);
create table if not exists elliott_candidates (
    alert_id uuid primary key references alerts(alert_id),
    computed_at_utc timestamptz not null,
    evaluation_time_utc timestamptz not null,
    symbol text not null,
    base_timeframe text not null,
    parameters_json jsonb not null,
    candidates_json jsonb not null
);";

        await using var cmd = dataSource.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertAlertAsync(NpgsqlDataSource dataSource, Guid alertId, string idempotencyKey)
    {
        const string insertKeys = "insert into idempotency_keys (idempotency_key, first_seen_utc) values (@key, @utc) on conflict do nothing;";
        const string insertAlert = @"
insert into alerts (alert_id, idempotency_key, received_at_utc, source, raw_payload, alert_json)
values (@alert_id, @key, @utc, @source, @raw_payload, @alert_json);";

        await using var cmd = dataSource.CreateCommand(insertKeys + insertAlert);
        cmd.Parameters.AddWithValue("key", idempotencyKey);
        cmd.Parameters.AddWithValue("utc", DateTimeOffset.UtcNow.UtcDateTime);
        cmd.Parameters.AddWithValue("alert_id", alertId);
        cmd.Parameters.AddWithValue("source", "test");
        cmd.Parameters.AddWithValue("raw_payload", "{}");
        var alertJson = JsonSerializer.Serialize(new { alertId });
        cmd.Parameters.Add("alert_json", NpgsqlDbType.Jsonb).Value = alertJson;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task CleanupAsync(NpgsqlDataSource dataSource, Guid alertId, string idempotencyKey)
    {
        const string sql = @"
delete from elliott_candidates where alert_id = @alert_id;
delete from indicator_snapshots where alert_id = @alert_id;
delete from alert_processing where alert_id = @alert_id;
delete from alerts where alert_id = @alert_id;
delete from idempotency_keys where idempotency_key = @key;";

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("alert_id", alertId);
        cmd.Parameters.AddWithValue("key", idempotencyKey);
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed record KrakenFixture(string Symbol, int IntervalMinutes, IReadOnlyList<Candle> Candles);

    private sealed class FixtureMarketDataProvider : IMarketDataProvider
    {
        private readonly IReadOnlyList<Candle> _candles;

        public FixtureMarketDataProvider(IReadOnlyList<Candle> candles)
        {
            _candles = candles;
        }

        public string ExchangeId => "kraken-fixture";

        public Task<Result<IReadOnlyList<Instrument>>> GetInstrumentsAsync(CancellationToken ct)
        {
            return Task.FromResult(new Result<IReadOnlyList<Instrument>>(true, Array.Empty<Instrument>(), null));
        }

        public Task<Result<IReadOnlyList<Ticker>>> GetTickersAsync(CancellationToken ct)
        {
            return Task.FromResult(new Result<IReadOnlyList<Ticker>>(true, Array.Empty<Ticker>(), null));
        }

        public Task<Result<IReadOnlyList<Candle>>> GetOhlcvAsync(
            string symbol,
            Timeframe timeframe,
            int lookbackBars,
            CancellationToken ct)
        {
            var slice = _candles.Count > lookbackBars
                ? _candles.Skip(_candles.Count - lookbackBars).ToList()
                : _candles.ToList();

            return Task.FromResult(new Result<IReadOnlyList<Candle>>(true, slice, null));
        }
    }

    private sealed class StubMcpGateway : IMcpGateway
    {
        public Task<Result<LlmDecision>> AdjudicateElliottAsync(ElliottAdjudicationInput input, CancellationToken ct)
        {
            var candidateId = input.Candidates.Candidates.FirstOrDefault()?.CandidateId;
            var decision = new LlmDecision("ALLOWLONGW3", 0.9m, candidateId, "WAVEINVALIDATION", "stub");
            return Task.FromResult(new Result<LlmDecision>(true, decision, null));
        }

        public Task<Result<StopLossSuggestion>> ExplainStopLossAsync(StopLossExplainInput input, CancellationToken ct)
        {
            var suggestion = new StopLossSuggestion("WAVEINVALIDATION", null, "stub");
            return Task.FromResult(new Result<StopLossSuggestion>(true, suggestion, null));
        }
    }

    private sealed class StubPolicyStore : IPolicyStore
    {
        public RiskPolicy GetRiskPolicy()
        {
            return new RiskPolicy(1m, 5m, 5m, 50000m, "LONG,SHORT");
        }
    }

    private sealed class StubMcpConfigStore : IMcpConfigStore
    {
        private readonly McpConfiguration _config;

        public StubMcpConfigStore()
        {
            _config = new McpConfiguration(
                new McpToolRegistry(new Dictionary<string, McpToolConfig>(StringComparer.OrdinalIgnoreCase)),
                new McpSchemaVersions("1.0", "1.0"));
        }

        public McpConfiguration GetConfig() => _config;

        public McpToolConfig? GetToolConfig(string toolName) => null;
    }

    private sealed class StubTradePlanBuilder : ITradePlanBuilder
    {
        public Result<TradePlan> BuildPlan(TradePlanContext context)
        {
            var targets = new[]
            {
                new TakeProfitTarget(1.1m, 0.5m, "TP1"),
                new TakeProfitTarget(1.2m, 0.3m, "TP2"),
                new TakeProfitTarget(1.3m, 0.2m, "TP3")
            };

            var plan = new TradePlan(
                Guid.NewGuid(),
                context.Snapshot.Symbol,
                "LONG",
                context.Candidates.BaseTimeframe,
                "LIMIT_IOC",
                1m,
                1m,
                1m,
                0.9m,
                0.1m,
                targets,
                "1.0",
                "stub",
                context.Decision.ChosenCandidateId ?? "cand",
                "receipt",
                context.EvaluationTimeUtc);

            return new Result<TradePlan>(true, plan, null);
        }
    }

    private sealed class StubExecutionService : IExecutionService
    {
        public Task<Result<ExecutionReceipt>> ExecuteAsync(ExecutionRequest request, CancellationToken ct)
        {
            var entry = new OrderReceipt("entry", null, "FILLED", request.Plan.Quantity, request.Plan.EntryLimitPrice);
            var receipt = new ExecutionReceipt(
                Guid.NewGuid(),
                request.Plan.PlanId,
                "SIMULATED",
                "SIMULATED_FILLED",
                request.Plan.CreatedAtUtc,
                entry,
                null,
                "stub");
            return Task.FromResult(new Result<ExecutionReceipt>(true, receipt, null));
        }
    }

    private sealed class StubKillSwitchService : IKillSwitchService
    {
        public Task<bool> IsActiveAsync(CancellationToken ct = default)
        {
            return Task.FromResult(false); // Always inactive for tests
        }

        public Task<KillSwitchStatus> GetStatusAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new KillSwitchStatus(false, KillSwitchLevel.PAUSE_ALL, null, null));
        }

        public Task ActivateAsync(KillSwitchLevel level, string reason, string activatedBy, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task DeactivateAsync(string deactivatedBy, string reason, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }
}
