namespace Mvp.Trading.Risk;

/// <summary>
/// Resolves instrument specifications for risk and execution constraints.
/// </summary>
public interface IInstrumentSpecProvider
{
    InstrumentSpec? GetSpec(string symbol);
}
