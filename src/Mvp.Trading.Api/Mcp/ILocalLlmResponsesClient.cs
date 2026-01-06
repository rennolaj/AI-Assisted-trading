using Mvp.Trading.Contracts;

namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// Abstraction for local LLM Responses API calls.
/// </summary>
public interface ILocalLlmResponsesClient
{
    Task<Result<string>> CreateResponseAsync(OpenAiResponsesRequest request, CancellationToken ct);
}
