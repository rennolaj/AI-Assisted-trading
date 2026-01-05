using System;
using System.Threading;
using System.Threading.Tasks;
using Mvp.Trading.Api.Models;

namespace Mvp.Trading.Api.Services;

/// <summary>
/// Writes open trade records for monitoring.
/// </summary>
public interface IOpenTradeCommand
{
    Task<Guid> CreateOpenTradeAsync(OpenTradeRequest request, CancellationToken ct);
}
