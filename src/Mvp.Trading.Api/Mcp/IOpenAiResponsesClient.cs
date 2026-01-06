using Mvp.Trading.Contracts;

namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// Abstraction for OpenAI Responses API calls.
/// </summary>
public interface IOpenAiResponsesClient
{
    Task<Result<string>> CreateResponseAsync(OpenAiResponsesRequest request, CancellationToken ct);
}
