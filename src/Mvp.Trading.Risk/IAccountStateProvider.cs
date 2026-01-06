namespace Mvp.Trading.Risk;

/// <summary>
/// Resolves account state for risk sizing.
/// </summary>
public interface IAccountStateProvider
{
    AccountState GetAccountState();
}
