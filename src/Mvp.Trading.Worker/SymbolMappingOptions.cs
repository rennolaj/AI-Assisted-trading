namespace Mvp.Trading.Worker;

/// <summary>
/// Configurable symbol mappings for external alert sources.
/// </summary>
public sealed class SymbolMappingOptions
{
    /// <summary>
    /// Symbol overrides for Kraken Futures.
    /// </summary>
    public Dictionary<string, string> KrakenFutures { get; set; } = new();
}
