namespace Mvp.Trading.Risk;

/// <summary>
/// Resolves execution settings (mode, slippage caps, heartbeat).
/// </summary>
public interface IExecutionSettingsProvider
{
    ExecutionSettings GetSettings();
}
