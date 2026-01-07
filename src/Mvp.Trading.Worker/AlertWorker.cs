using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Mvp.Trading.Api.Mcp;
using Mvp.Trading.Contracts;
using Mvp.Trading.Contracts.Telemetry;
using Mvp.Trading.Elliott;
using Mvp.Trading.Execution;
using Mvp.Trading.Indicators;
using Mvp.Trading.Risk;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Mvp.Trading.Worker;

/// <summary>
/// Background worker that dequeues alerts from Redis and logs the normalized payload.
/// </summary>
public sealed class AlertWorker : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IAlertProcessingStore _processingStore;
    private readonly WorkerOptions _options;
    private readonly ILogger<AlertWorker> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IndicatorEngine _indicatorEngine;
    private readonly IIndicatorSnapshotStore _snapshotStore;
    private readonly SymbolMapper _symbolMapper;
    private readonly IElliottEngine _elliottEngine;
    private readonly IElliottCandidatesStore _elliottStore;
    private readonly ElliottRunConfig _elliottConfig;
    private readonly IMcpGateway _mcpGateway;
    private readonly IPolicyStore _policyStore;
    private readonly IMcpConfigStore _mcpConfigStore;
    private readonly ITradePlanBuilder _tradePlanBuilder;
    private readonly IExecutionService _executionService;
    private readonly McpProviderOptions _mcpOptions;
    private readonly IKillSwitchService _killSwitchService;
    private readonly IMetricsService _metricsService;

    public AlertWorker(
        IConnectionMultiplexer redis,
        IOptions<WorkerOptions> options,
        IAlertProcessingStore processingStore,
        IndicatorEngine indicatorEngine,
        IIndicatorSnapshotStore snapshotStore,
        IElliottEngine elliottEngine,
        IElliottCandidatesStore elliottStore,
        ElliottRunConfig elliottConfig,
        IMcpGateway mcpGateway,
        IPolicyStore policyStore,
        IMcpConfigStore mcpConfigStore,
        ITradePlanBuilder tradePlanBuilder,
        IExecutionService executionService,
        IOptions<McpProviderOptions> mcpOptions,
        IKillSwitchService killSwitchService,
        IMetricsService metricsService,
        SymbolMapper symbolMapper,
        ILogger<AlertWorker> logger)
    {
        _redis = redis;
        _options = options.Value;
        _processingStore = processingStore;
        _indicatorEngine = indicatorEngine;
        _snapshotStore = snapshotStore;
        _elliottEngine = elliottEngine;
        _elliottStore = elliottStore;
        _elliottConfig = elliottConfig;
        _mcpGateway = mcpGateway;
        _policyStore = policyStore;
        _mcpConfigStore = mcpConfigStore;
        _tradePlanBuilder = tradePlanBuilder;
        _executionService = executionService;
        _mcpOptions = mcpOptions.Value;
        _killSwitchService = killSwitchService;
        _metricsService = metricsService;
        _symbolMapper = symbolMapper;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker started. Queue={QueueKey}", _options.AlertQueueKey);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Check kill switch status
                var killSwitchStatus = await _killSwitchService.GetStatusAsync(stoppingToken);
                if (killSwitchStatus.Active && 
                    (killSwitchStatus.Level == KillSwitchLevel.PAUSE_NEW || 
                     killSwitchStatus.Level == KillSwitchLevel.PAUSE_ALL ||
                     killSwitchStatus.Level == KillSwitchLevel.EMERGENCY_STOP))
                {
                    _logger.LogWarning("AlertWorker paused: Kill switch active at level {Level}", killSwitchStatus.Level);
                    await Task.Delay(_options.PollIntervalMs, stoppingToken);
                    continue;
                }

                var payload = await DequeueAsync(stoppingToken);
                if (payload.IsNullOrEmpty)
                {
                    await Task.Delay(_options.PollIntervalMs, stoppingToken);
                    continue;
                }

                try
                {
                    var alert = JsonSerializer.Deserialize<AlertEvent>(payload.ToString(), _jsonOptions);
                    if (alert is null)
                    {
                        _logger.LogWarning("Dequeued payload was null after deserialization.");
                        continue;
                    }

                    // Record alert received
                    _metricsService.RecordAlertReceived(alert.Tv.Exchange, alert.Tv.Ticker);

                    // Start timing alert processing
                    var stopwatch = Stopwatch.StartNew();

                    await _processingStore.UpsertAsync(alert, "processing", null, stoppingToken);

                    try
                    {
                        _logger.LogInformation(
                            "Processing alert {AlertId} ({Symbol}/{Interval})",
                            alert.AlertId,
                            alert.Tv.Ticker,
                            alert.Tv.Interval);

                        var symbol = string.IsNullOrWhiteSpace(alert.Intent.SymbolHint)
                            ? alert.Tv.Ticker
                            : alert.Intent.SymbolHint;
                        symbol = _symbolMapper.Resolve(alert.Tv.Exchange, symbol);
                        var input = new IndicatorInput(
                            alert.AlertId,
                            Guid.NewGuid(),
                            symbol,
                            alert.Intent.DirectionHint,
                            DateTimeOffset.UtcNow);

                        var snapshotResult = await _indicatorEngine.ComputeAsync(input, stoppingToken);
                        if (!snapshotResult.Ok || snapshotResult.Value is null)
                        {
                            var errorMessage = snapshotResult.Error?.Message ?? "Indicator snapshot computation failed.";
                            throw new InvalidOperationException(errorMessage);
                        }

                        await _snapshotStore.UpsertAsync(snapshotResult.Value, stoppingToken);

                        var profileName = _elliottConfig.ProfileSelection.ResolveProfileName(snapshotResult.Value.Risk.Category);
                        var parameters = ResolveElliottParameters(profileName);

                        _logger.LogInformation(
                            "Elliott profile {Profile} selected for risk {RiskCategory}.",
                            profileName,
                            snapshotResult.Value.Risk.Category);

                        var elliottCandidates = await _elliottEngine.GenerateCandidatesAsync(
                            symbol,
                            _elliottConfig.BaseTimeframe,
                            parameters,
                            input.EvaluationTimeUtc,
                            stoppingToken);

                        if (ShouldFallback(elliottCandidates) &&
                            _elliottConfig.ProfileSelection.TryGetFallback(profileName, out var fallbackName, out var fallbackParameters))
                        {
                            _logger.LogInformation("Elliott fallback profile {Profile} activated.", fallbackName);

                            var fallbackCandidates = await _elliottEngine.GenerateCandidatesAsync(
                                symbol,
                                _elliottConfig.BaseTimeframe,
                                fallbackParameters,
                                input.EvaluationTimeUtc,
                                stoppingToken);

                            if (!ShouldFallback(fallbackCandidates) ||
                                fallbackCandidates.Candidates.Count > elliottCandidates.Candidates.Count)
                            {
                                profileName = fallbackName;
                                parameters = fallbackParameters;
                                elliottCandidates = fallbackCandidates;
                            }
                        }

                        await _elliottStore.UpsertAsync(
                            alert.AlertId,
                            DateTimeOffset.UtcNow,
                            input.EvaluationTimeUtc,
                            symbol,
                            _elliottConfig.BaseTimeframe,
                            parameters with { ProfileName = profileName },
                            elliottCandidates,
                            stoppingToken);

                        var adjudicationInput = new ElliottAdjudicationInput(
                            ToSignalSnapshot(snapshotResult.Value),
                            elliottCandidates,
                            _policyStore.GetRiskPolicy());

                        var adjudication = await GetAdjudicationAsync(adjudicationInput, alert, stoppingToken);
                        if (!adjudication.Ok || adjudication.Value is null)
                        {
                            var errorCode = adjudication.Error?.Code ?? "MCP_ADJUDICATION_FAILED";
                            var errorMessage = adjudication.Error?.Message ?? "MCP adjudication failed.";
                            await _processingStore.UpsertAsync(alert, "adjudication_failed", $"{errorCode}: {errorMessage}", stoppingToken);
                            _logger.LogWarning("MCP adjudication failed for alert {AlertId}: {ErrorCode}", alert.AlertId, errorCode);
                            
                            stopwatch.Stop();
                            _metricsService.RecordAlertProcessed("error");
                            _metricsService.RecordAlertProcessingDuration(stopwatch.Elapsed);
                            continue;
                        }

                        if (!IsAllowDecision(adjudication.Value.Decision))
                        {
                            await _processingStore.UpsertAsync(
                                alert,
                                "rejected",
                                $"LLM_DECISION:{adjudication.Value.Decision}",
                                stoppingToken);
                            _logger.LogInformation(
                                "Alert {AlertId} rejected by MCP with decision {Decision}.",
                                alert.AlertId,
                                adjudication.Value.Decision);
                            
                            stopwatch.Stop();
                            _metricsService.RecordAlertProcessed("rejected_llm");
                            _metricsService.RecordAlertProcessingDuration(stopwatch.Elapsed);
                            continue;
                        }

                        var planContext = new TradePlanContext(
                            alert,
                            ToSignalSnapshot(snapshotResult.Value),
                            elliottCandidates,
                            adjudication.Value,
                            _policyStore.GetRiskPolicy(),
                            "1.0",
                            _mcpConfigStore.GetConfig().SchemaVersions.LlmDecision,
                            snapshotResult.Value.EvaluationTimeUtc);

                        var planResult = _tradePlanBuilder.BuildPlan(planContext);
                        if (!planResult.Ok || planResult.Value is null)
                        {
                            var planError = planResult.Error?.Message ?? "Trade plan build failed.";
                            await _processingStore.UpsertAsync(alert, "plan_failed", planError, stoppingToken);
                            _logger.LogWarning("Trade plan failed for alert {AlertId}: {Error}", alert.AlertId, planError);
                            continue;
                        }

                        var execution = await _executionService.ExecuteAsync(
                            new ExecutionRequest(alert.AlertId, planResult.Value),
                            stoppingToken);

                        if (!execution.Ok || execution.Value is null)
                        {
                            var execError = execution.Error?.Message ?? "Execution failed.";
                            await _processingStore.UpsertAsync(alert, "execution_failed", execError, stoppingToken);
                            _logger.LogWarning("Execution failed for alert {AlertId}: {Error}", alert.AlertId, execError);
                            
                            stopwatch.Stop();
                            _metricsService.RecordAlertProcessed("error");
                            _metricsService.RecordAlertProcessingDuration(stopwatch.Elapsed);
                            continue;
                        }

                        await _processingStore.UpsertAsync(alert, "executed", null, stoppingToken);
                        
                        stopwatch.Stop();
                        _metricsService.RecordAlertProcessed("accepted");
                        _metricsService.RecordAlertProcessingDuration(stopwatch.Elapsed);
                    }
                    catch (Exception ex)
                    {
                        await _processingStore.UpsertAsync(alert, "failed", ex.Message, stoppingToken);
                        _logger.LogError(ex, "Processing failed for alert {AlertId}.", alert.AlertId);
                        
                        stopwatch.Stop();
                        _metricsService.RecordAlertProcessed("error");
                        _metricsService.RecordError("AlertWorker", ex.GetType().Name);
                        _metricsService.RecordAlertProcessingDuration(stopwatch.Elapsed);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize alert payload from Redis.");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Alert worker iteration failed.");
                if (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(_options.PollIntervalMs, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
    }

    private async Task<RedisValue> DequeueAsync(CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        return await db.ListLeftPopAsync(_options.AlertQueueKey).ConfigureAwait(false);
    }

    private static bool IsAllowDecision(string decision)
    {
        return decision.StartsWith("ALLOW", StringComparison.OrdinalIgnoreCase);
    }

    private static SignalSnapshot ToSignalSnapshot(IndicatorSnapshot snapshot)
    {
        return new SignalSnapshot(snapshot.Symbol, snapshot.ComputedAtUtc, snapshot.Timeframes);
    }

    private ElliottParameters ResolveElliottParameters(string profileName)
    {
        if (_elliottConfig.ProfileSelection.TryGetProfile(profileName, out var parameters))
        {
            return parameters;
        }

        return _elliottConfig.Parameters;
    }

    private static bool ShouldFallback(ElliottCandidates candidates)
    {
        if (candidates.Candidates.Count == 0)
        {
            return true;
        }

        return candidates.Candidates.All(candidate =>
            candidate.RuleViolations.Any(violation =>
                string.Equals(violation.Rule, ElliottRuleCodes.PivotsInsufficient, StringComparison.Ordinal)));
    }

    private async Task<Result<LlmDecision>> GetAdjudicationAsync(
        ElliottAdjudicationInput adjudicationInput,
        AlertEvent alert,
        CancellationToken ct)
    {
        if (_mcpOptions.ForceAllow)
        {
            var forced = TryBuildForcedDecision(adjudicationInput, alert);
            if (forced is not null)
            {
                _logger.LogWarning("MCP force-allow enabled; bypassing LLM adjudication.");
                return new Result<LlmDecision>(true, forced, null);
            }

            _logger.LogWarning("MCP force-allow enabled but no eligible candidate found; falling back to LLM.");
        }

        return await _mcpGateway.AdjudicateElliottAsync(adjudicationInput, ct);
    }

    private static LlmDecision? TryBuildForcedDecision(ElliottAdjudicationInput adjudicationInput, AlertEvent alert)
    {
        var direction = NormalizeDirection(alert.Intent.DirectionHint);
        var isShort = string.Equals(direction, "SHORT", StringComparison.OrdinalIgnoreCase);

        var candidate = adjudicationInput.Candidates.Candidates
            .OrderByDescending(c => c.Score)
            .FirstOrDefault(c =>
                isShort
                    ? c.Invalidation.ShortInvalidationPrice.HasValue
                    : c.Invalidation.LongInvalidationPrice.HasValue);

        if (candidate is null)
        {
            return null;
        }

        var decision = isShort ? "ALLOWSHORTDEMO" : "ALLOWLONGDEMO";
        return new LlmDecision(
            decision,
            candidate.Confidence,
            candidate.CandidateId,
            "WAVEINVALIDATION",
            "forced demo allow");
    }

    private static string NormalizeDirection(string? directionHint)
    {
        if (string.IsNullOrWhiteSpace(directionHint))
        {
            return "LONG";
        }

        var normalized = directionHint.Trim();
        if (normalized.Contains("SHORT", StringComparison.OrdinalIgnoreCase))
        {
            return "SHORT";
        }

        return "LONG";
    }
}
