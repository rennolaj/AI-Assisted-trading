using Microsoft.Extensions.Options;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// Routes MCP calls to OpenAI, local LLM, or auto fallback based on configuration.
/// </summary>
public sealed class McpGatewayRouter : IMcpGateway
{
    private readonly OpenAiMcpGateway _openAiGateway;
    private readonly LocalLlmMcpGateway _localGateway;
    private readonly McpProviderOptions _options;
    private readonly ILogger<McpGatewayRouter> _logger;

    public McpGatewayRouter(
        OpenAiMcpGateway openAiGateway,
        LocalLlmMcpGateway localGateway,
        IOptions<McpProviderOptions> options,
        ILogger<McpGatewayRouter> logger)
    {
        _openAiGateway = openAiGateway;
        _localGateway = localGateway;
        _options = options.Value;
        _logger = logger;
    }

    public Task<Result<McpAdjudicationResult>> AdjudicateElliottAsync(ElliottAdjudicationInput input, CancellationToken ct)
    {
        return ExecuteAsync(gateway => gateway.AdjudicateElliottAsync(input, ct), ct);
    }

    public Task<Result<StopLossSuggestion>> ExplainStopLossAsync(StopLossExplainInput input, CancellationToken ct)
    {
        return ExecuteAsync(gateway => gateway.ExplainStopLossAsync(input, ct), ct);
    }

    private async Task<Result<T>> ExecuteAsync<T>(Func<IMcpGateway, Task<Result<T>>> action, CancellationToken ct)
    {
        var provider = NormalizeProvider(_options.Provider);
        _logger.LogInformation("MCP provider set to {Provider} (fallbackOnOpenAi429={Fallback}).", provider, _options.FallbackOnOpenAi429);
        return provider switch
        {
            "local" => await action(_localGateway),
            "openai" => await action(_openAiGateway),
            "auto" => await ExecuteWithFallbackAsync(action, ct),
            _ => await ExecuteWithDefaultAsync(action, provider)
        };
    }

    private async Task<Result<T>> ExecuteWithFallbackAsync<T>(Func<IMcpGateway, Task<Result<T>>> action, CancellationToken ct)
    {
        var primary = await action(_openAiGateway);
        if (primary.Ok || primary.Error is null)
        {
            return primary;
        }

        if (!ShouldFallback(primary.Error))
        {
            return primary;
        }

        _logger.LogWarning("OpenAI returned {Code}; falling back to local LLM.", primary.Error.Code);
        var fallback = await action(_localGateway);
        if (fallback.Ok)
        {
            return fallback;
        }

        _logger.LogWarning(
            "Local LLM fallback failed: {Code} {Message}",
            fallback.Error?.Code ?? "UNKNOWN",
            fallback.Error?.Message ?? "Unknown error");

        return primary;
    }

    private async Task<Result<T>> ExecuteWithDefaultAsync<T>(Func<IMcpGateway, Task<Result<T>>> action, string provider)
    {
        _logger.LogWarning("Unknown MCP provider '{Provider}', defaulting to OpenAI.", provider);
        return await action(_openAiGateway);
    }

    private bool ShouldFallback(Error error)
    {
        if (!_options.FallbackOnOpenAi429)
        {
            return false;
        }

        if (!string.Equals(error.Code, "OPENAI_REQUEST_FAILED", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (error.Meta is not null &&
            error.Meta.TryGetValue("statusCode", out var statusCode) &&
            string.Equals(statusCode, "429", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return error.Message?.Contains("quota", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string NormalizeProvider(string? provider)
    {
        return string.IsNullOrWhiteSpace(provider) ? "openai" : provider.Trim().ToLowerInvariant();
    }
}
