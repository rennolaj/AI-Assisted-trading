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
    private readonly ILlmAdjudicationStore _llmAdjudicationStore;
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
        ILlmAdjudicationStore llmAdjudicationStore,
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
        _llmAdjudicationStore = llmAdjudicationStore;
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
                            alert.Intent.DirectionHint,
                            ToSignalSnapshot(snapshotResult.Value),
                            elliottCandidates,
                            _policyStore.GetRiskPolicy());

                        var correlationId = Guid.NewGuid();
                        var adjudication = await GetAdjudicationAsync(adjudicationInput, alert, correlationId, stoppingToken);
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

                        var llmDecision = adjudication.Value.Decision;
                        if (!IsAllowDecision(llmDecision.Decision))
                        {
                            await _processingStore.UpsertAsync(
                                alert,
                                "rejected",
                                $"LLM_DECISION:{llmDecision.Decision}",
                                stoppingToken);
                            _logger.LogInformation(
                                "Alert {AlertId} rejected by MCP with decision {Decision}.",
                                alert.AlertId,
                                llmDecision.Decision);
                            
                            stopwatch.Stop();
                            _metricsService.RecordAlertProcessed("rejected_llm");
                            _metricsService.RecordAlertProcessingDuration(stopwatch.Elapsed);
                            continue;
                        }

                        // Override risk for W3 patterns - RSI extreme not required for impulse waves
                        var adjustedSnapshot = AdjustRiskForW3Pattern(snapshotResult.Value, llmDecision.Decision);

                        var planContext = new TradePlanContext(
                            alert,
                            ToSignalSnapshot(adjustedSnapshot),
                            elliottCandidates,
                            llmDecision,
                            _policyStore.GetRiskPolicy(),
                            "1.0",
                            _mcpConfigStore.GetConfig().SchemaVersions.LlmDecision,
                            adjustedSnapshot.EvaluationTimeUtc);

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
        // StackExchange.Redis async operations do not accept a CancellationToken;
        // honor cancellation around them. Only the read-only length query is
        // WaitAsync(ct)-wrapped — abandoning an in-flight pop could drop an alert.
        ct.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();

        // Update queue depth gauge
        var queueLength = await db.ListLengthAsync(_options.AlertQueueKey).WaitAsync(ct).ConfigureAwait(false);
        _metricsService.SetQueueDepthGauge((int)queueLength);

        ct.ThrowIfCancellationRequested();
        return await db.ListLeftPopAsync(_options.AlertQueueKey).ConfigureAwait(false);
    }

    private static bool IsAllowDecision(string decision)
    {
        return decision.StartsWith("ALLOW", StringComparison.OrdinalIgnoreCase);
    }

    private static IndicatorSnapshot AdjustRiskForW3Pattern(IndicatorSnapshot snapshot, string decision)
    {
        // For W3 patterns (impulse waves), override INVALID risk if only RSI gate failed
        // W3 happens mid-trend, so RSI extremes are not required
        // W5END patterns still require RSI extremes for reversal confirmation
        if ((decision == "ALLOWLONGW3" || decision == "ALLOWSHORTW3") && 
            snapshot.Risk.Category == "INVALID")
        {
            // Use LOW risk profile for W3 patterns when RSI gate failed
            var overrideRisk = new IndicatorRisk(
                "LOW",
                "ALLOW_LITE",
                0.5m, // 50% of normal risk for W3 without RSI confirmation
                TrendRequired: false,
                CounterTrendAllowed: true,
                MinConfirmations: 0);

            return snapshot with { Risk = overrideRisk };
        }

        return snapshot;
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

    private async Task<Result<McpAdjudicationResult>> GetAdjudicationAsync(
        ElliottAdjudicationInput adjudicationInput,
        AlertEvent alert,
        Guid correlationId,
        CancellationToken ct)
    {
        if (_mcpOptions.ForceAllow)
        {
            var forced = TryBuildForcedDecision(adjudicationInput, alert);
            if (forced is not null)
            {
                _logger.LogWarning("MCP force-allow enabled; bypassing LLM adjudication.");
                // Create a synthetic adjudication result for forced decisions
                var forcedResult = new McpAdjudicationResult
                {
                    PromptSent = "[FORCED DECISION - NO LLM CALL]",
                    RawResponse = "[FORCED DECISION - NO LLM CALL]",
                    Decision = forced,
                    Provider = "forced",
                    Model = null,
                    DurationMs = 0
                };
                return new Result<McpAdjudicationResult>(true, forcedResult, null);
            }

            _logger.LogWarning("MCP force-allow enabled but no eligible candidate found; falling back to LLM.");
        }

        // Debug: Log Elliott candidates being sent to LLM
        var candidatesSummary = adjudicationInput.Candidates.Candidates
            .Select(c => new { 
                c.WaveLabel, 
                c.Score, 
                c.Confidence,
                ViolationCount = c.RuleViolations?.Count ?? 0,
                FirstViolation = c.RuleViolations?.FirstOrDefault()?.Rule,
                FirstSeverity = c.RuleViolations?.FirstOrDefault()?.Severity
            })
            .ToList();
        _logger.LogInformation(
            "DEBUG: Sending {CandidateCount} Elliott candidates to LLM for alert {AlertId}. Summary: {@CandidatesSummary}", 
            adjudicationInput.Candidates.Candidates.Count,
            alert.AlertId,
            candidatesSummary);

        var llmResult = await _mcpGateway.AdjudicateElliottAsync(adjudicationInput, ct);
        
        // Debug: Log LLM decision result
        if (llmResult.Ok && llmResult.Value is not null)
        {
            _logger.LogInformation(
                "DEBUG: LLM returned decision={Decision} confidence={Confidence} reasoning={Reasoning} for alert {AlertId}",
                llmResult.Value.Decision?.Decision ?? "NULL",
                llmResult.Value.Decision?.Confidence ?? 0,
                llmResult.Value.Decision?.Notes ?? "NULL",
                alert.AlertId);
        }
        else
        {
            _logger.LogWarning(
                "DEBUG: LLM call failed for alert {AlertId}: {Error}",
                alert.AlertId,
                llmResult.Error?.Message ?? "Unknown error");
        }
        
        // Persist LLM adjudication to database
        if (llmResult.Ok && llmResult.Value is not null)
        {
            var adjudication = new Mvp.Trading.Contracts.Contracts.LlmAdjudication
            {
                AdjudicationId = Guid.NewGuid(),
                AlertId = alert.AlertId,
                CorrelationId = correlationId,
                PromptText = llmResult.Value.PromptSent,
                PromptTokens = llmResult.Value.PromptTokens,
                RawResponse = llmResult.Value.RawResponse,
                CompletionTokens = llmResult.Value.CompletionTokens,
                TotalTokens = llmResult.Value.TotalTokens,
                Decision = llmResult.Value.Decision?.Decision ?? "",
                Reasoning = llmResult.Value.Decision?.Notes ?? string.Empty,
                Confidence = llmResult.Value.Decision?.Confidence,
                LlmProvider = llmResult.Value.Provider,
                LlmModel = llmResult.Value.Model,
                ResponseTimeMs = llmResult.Value.DurationMs,
                AdjudicatedAtUtc = DateTimeOffset.UtcNow,
                ParseError = llmResult.Value.ParseError,
                ValidationErrors = llmResult.Value.ValidationErrors
            };

            try
            {
                await _llmAdjudicationStore.SaveAsync(adjudication, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist LLM adjudication for alert {AlertId}", alert.AlertId);
                // Don't fail the alert processing if persistence fails
            }
        }
        
        return llmResult;
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
