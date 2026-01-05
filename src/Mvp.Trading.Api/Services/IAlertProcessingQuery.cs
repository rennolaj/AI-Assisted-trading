using System.Threading;
using System.Threading.Tasks;

namespace Mvp.Trading.Api.Services;

/// <summary>
/// Retrieves processing status for alerts.
/// </summary>
public interface IAlertProcessingQuery
{
    Task<AlertProcessingStatus?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct);
}
