using Mvp.Trading.Execution;

namespace Mvp.Trading.Api.Models;

public sealed record KillSwitchActivationRequest(
    string Secret,
    KillSwitchLevel Level,
    string Reason,
    string? ActivatedBy = null
);

public sealed record KillSwitchDeactivationRequest(
    string Secret,
    string Reason,
    string? DeactivatedBy = null
);
