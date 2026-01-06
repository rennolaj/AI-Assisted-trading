using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mvp.Trading.Api.Mcp;
using Mvp.Trading.Contracts;
using Xunit;

namespace Mvp.Trading.Api.Tests;

public sealed class OpenAiMcpGatewayTests
{
    [Fact]
    public async Task AdjudicateElliott_ReturnsDecision_WhenSchemaValid()
    {
        var responseJson = "{\"decision\":\"ALLOWLONGW3\",\"confidence\":0.85,\"chosenCandidateId\":\"cand-1\",\"stopLossAnchor\":\"WAVEINVALIDATION\",\"notes\":\"ok\"}";
        var gateway = CreateGateway(new Result<string>(true, responseJson, null));

        var result = await gateway.AdjudicateElliottAsync(BuildAdjudicationInput(), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("ALLOWLONGW3", result.Value?.Decision);
    }

    [Fact]
    public async Task AdjudicateElliott_FailsClosed_OnInvalidJson()
    {
        var gateway = CreateGateway(new Result<string>(true, "not-json", null));

        var result = await gateway.AdjudicateElliottAsync(BuildAdjudicationInput(), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("MCP_OUTPUT_INVALID_JSON", result.Error?.Code);
    }

    [Fact]
    public async Task AdjudicateElliott_FailsClosed_OnSchemaViolation()
    {
        var responseJson = "{\"decision\":\"UNKNOWN\",\"confidence\":1.2,\"chosenCandidateId\":null,\"stopLossAnchor\":\"BAD\",\"notes\":\"nope\"}";
        var gateway = CreateGateway(new Result<string>(true, responseJson, null));

        var result = await gateway.AdjudicateElliottAsync(BuildAdjudicationInput(), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("MCP_SCHEMA_INVALID", result.Error?.Code);
    }

    [Fact]
    public async Task ExplainStopLoss_ReturnsSuggestion_WhenSchemaValid()
    {
        var responseJson = "{\"anchor\":\"SWINGLEVEL\",\"suggestedStopPrice\":41234.5,\"notes\":\"swing low\"}";
        var gateway = CreateGateway(new Result<string>(true, responseJson, null));

        var result = await gateway.ExplainStopLossAsync(BuildStopLossInput(), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("SWINGLEVEL", result.Value?.Anchor);
    }

    [Fact]
    public async Task ExplainStopLoss_FailsClosed_OnSchemaViolation()
    {
        var responseJson = "{\"anchor\":null,\"notes\":\"missing price\"}";
        var gateway = CreateGateway(new Result<string>(true, responseJson, null));

        var result = await gateway.ExplainStopLossAsync(BuildStopLossInput(), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("MCP_SCHEMA_INVALID", result.Error?.Code);
    }

    private static OpenAiMcpGateway CreateGateway(Result<string> response)
    {
        var client = new StubOpenAiResponsesClient(response);
        var configStore = new StubMcpConfigStore();
        var policyStore = new StubPolicyStore();
        var promptStore = new StubPromptTemplateStore();
        var schemaValidator = new JsonSchemaValidator();

        return new OpenAiMcpGateway(client, configStore, policyStore, promptStore, schemaValidator);
    }

    private static ElliottAdjudicationInput BuildAdjudicationInput()
    {
        var snapshot = new SignalSnapshot(
            "BTCUSD.P",
            DateTimeOffset.UtcNow,
            new[]
            {
                new TimeframeSnapshot(
                    Timeframe.M15,
                    new RsiState(52m, "NEUTRAL"),
                    new StochRsiState(40m, 45m, "NEUTRAL"),
                    new MacdState(1m, 0.5m, 0.5m, "BULL"),
                    new VolumeState(1000m, "OK", "SMA20"))
            });

        var candidates = new ElliottCandidates(
            Timeframe.M15,
            new[]
            {
                new ElliottCandidate(
                    "cand-1",
                    "IMPULSE",
                    "W3",
                    70m,
                    0.7m,
                    Array.Empty<RuleViolation>(),
                    new InvalidationLevels(40500m, 39800m))
            });

        var policy = new RiskPolicy(1m, 5m, 5m, 50000m, "LONG,SHORT");

        return new ElliottAdjudicationInput(snapshot, candidates, policy);
    }

    private static StopLossExplainInput BuildStopLossInput()
    {
        var snapshot = new SignalSnapshot(
            "BTCUSD.P",
            DateTimeOffset.UtcNow,
            new[]
            {
                new TimeframeSnapshot(
                    Timeframe.M15,
                    new RsiState(52m, "NEUTRAL"),
                    new StochRsiState(40m, 45m, "NEUTRAL"),
                    new MacdState(1m, 0.5m, 0.5m, "BULL"),
                    new VolumeState(1000m, "OK", "SMA20"))
            });

        var candidate = new ElliottCandidate(
            "cand-1",
            "IMPULSE",
            "W3",
            70m,
            0.7m,
            Array.Empty<RuleViolation>(),
            new InvalidationLevels(40500m, 39800m));

        var policy = new RiskPolicy(1m, 5m, 5m, 50000m, "LONG,SHORT");

        return new StopLossExplainInput("LONG", candidate, snapshot, policy);
    }

    private sealed class StubOpenAiResponsesClient : IOpenAiResponsesClient
    {
        private readonly Result<string> _response;

        public StubOpenAiResponsesClient(Result<string> response)
        {
            _response = response;
        }

        public Task<Result<string>> CreateResponseAsync(OpenAiResponsesRequest request, CancellationToken ct)
        {
            return Task.FromResult(_response);
        }
    }

    private sealed class StubMcpConfigStore : IMcpConfigStore
    {
        private readonly McpConfiguration _config;

        public StubMcpConfigStore()
        {
            var tools = new Dictionary<string, McpToolConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["adjudicateElliott"] = new McpToolConfig("gpt-4.1-mini", 0.2m, 500),
                ["explainStopLoss"] = new McpToolConfig("gpt-4.1-mini", 0.2m, 300)
            };

            _config = new McpConfiguration(
                new McpToolRegistry(tools),
                new McpSchemaVersions("1.0", "1.0"));
        }

        public McpConfiguration GetConfig() => _config;

        public McpToolConfig? GetToolConfig(string toolName)
        {
            return _config.Mcp.Tools.TryGetValue(toolName, out var tool) ? tool : null;
        }
    }

    private sealed class StubPolicyStore : IPolicyStore
    {
        public RiskPolicy GetRiskPolicy()
        {
            return new RiskPolicy(1m, 5m, 5m, 50000m, "LONG,SHORT");
        }
    }

    private sealed class StubPromptTemplateStore : IPromptTemplateStore
    {
        public string RenderAdjudicateElliottPrompt(string input)
        {
            return $"ADJUDICATE\n{input}";
        }

        public string RenderExplainStopLossPrompt(string input)
        {
            return $"STOPLOSS\n{input}";
        }
    }
}
