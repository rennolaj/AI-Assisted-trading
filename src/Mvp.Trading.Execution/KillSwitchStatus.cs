namespace Mvp.Trading.Execution;

public sealed record KillSwitchStatus(
    bool Active,
    KillSwitchLevel Level,
    string? Reason,
    DateTime? ActivatedAt
);
