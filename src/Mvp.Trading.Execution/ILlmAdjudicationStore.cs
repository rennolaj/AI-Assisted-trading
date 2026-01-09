using System;
using System.Threading;
using System.Threading.Tasks;
using Mvp.Trading.Contracts.Contracts;

namespace Mvp.Trading.Execution;

/// <summary>
/// Store for persisting LLM adjudication interactions.
/// Enables debugging, analysis, and audit trails of LLM decisions.
/// </summary>
public interface ILlmAdjudicationStore
{
    /// <summary>
    /// Save a complete LLM adjudication record.
    /// </summary>
    Task SaveAsync(LlmAdjudication adjudication, CancellationToken ct);
    
    /// <summary>
    /// Retrieve the most recent adjudication for a given alert.
    /// </summary>
    Task<LlmAdjudication?> GetByAlertIdAsync(Guid alertId, CancellationToken ct);
}
