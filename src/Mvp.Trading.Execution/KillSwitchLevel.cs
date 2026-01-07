namespace Mvp.Trading.Execution;

public enum KillSwitchLevel
{
    PAUSE_NEW,        // Stop accepting new alerts only
    PAUSE_ALL,        // Pause all workers, keep API running
    EMERGENCY_STOP    // Cancel all open orders + pause everything
}
