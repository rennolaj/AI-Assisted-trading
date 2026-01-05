using System.Threading;
using System.Threading.Tasks;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Api.Services;

/// <summary>
/// Enqueues normalized alerts for downstream processing.
/// </summary>
public interface IAlertQueue
{
    Task EnqueueAsync(AlertEvent alert, CancellationToken ct);
}
