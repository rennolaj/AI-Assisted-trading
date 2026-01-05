using System.Threading;
using System.Threading.Tasks;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Api.Services;

/// <summary>
/// Persists raw webhook payloads and normalized alert events.
/// </summary>
public interface IAlertStore
{
    Task StoreAsync(string rawPayload, AlertEvent alert, CancellationToken ct);
}
