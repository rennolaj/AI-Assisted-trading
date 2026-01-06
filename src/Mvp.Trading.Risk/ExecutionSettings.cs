namespace Mvp.Trading.Risk;

/// <summary>
/// Execution behavior settings for the MVP.
/// </summary>
public sealed record ExecutionSettings(
    string Mode,
    decimal SlippageCapPct,
    int HeartbeatIntervalSeconds,
    int StaleThresholdSeconds,
    int MaxOrderRetries
);
