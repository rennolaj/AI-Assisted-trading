using Mvp.Trading.Contracts;

namespace Mvp.Trading.Execution;

/// <summary>
/// Executes trade plans using the configured execution mode.
/// </summary>
public interface IExecutionService
{
    Task<Result<ExecutionReceipt>> ExecuteAsync(ExecutionRequest request, CancellationToken ct);
}
