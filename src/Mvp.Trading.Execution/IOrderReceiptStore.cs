using Mvp.Trading.Contracts;

namespace Mvp.Trading.Execution;

/// <summary>
/// Persists order receipts for executions.
/// </summary>
public interface IOrderReceiptStore
{
    Task SaveAsync(Guid executionId, string orderKind, OrderReceipt receipt, CancellationToken ct);
    
    /// <summary>
    /// Get all order receipts for a specific execution.
    /// </summary>
    Task<IReadOnlyList<OrderReceipt>> GetByExecutionIdAsync(Guid executionId, CancellationToken ct = default);
}
