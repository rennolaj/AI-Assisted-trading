namespace Mvp.Trading.Execution;

public interface IKillSwitchService
{
    Task<bool> IsActiveAsync(CancellationToken ct = default);
    Task<KillSwitchStatus> GetStatusAsync(CancellationToken ct = default);
    Task ActivateAsync(KillSwitchLevel level, string reason, string activatedBy, CancellationToken ct = default);
    Task DeactivateAsync(string deactivatedBy, string reason, CancellationToken ct = default);
}
