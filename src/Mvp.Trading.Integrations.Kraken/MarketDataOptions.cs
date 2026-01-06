namespace Mvp.Trading.Integrations.Kraken;

/// <summary>
/// Configuration for selecting the market data provider.
/// </summary>
public sealed class MarketDataOptions
{
    /// <summary>
    /// Provider mode ("kraken" or "fixtures").
    /// </summary>
    public string Mode { get; init; } = "kraken";

    /// <summary>
    /// Relative or absolute path to fixture files.
    /// </summary>
    public string FixturesPath { get; init; } = "fixtures/kraken-futures";

    /// <summary>
    /// Whether to extend fixtures to satisfy lookback sizes.
    /// </summary>
    public bool ExtendFixtures { get; init; } = true;

    public bool UseFixtures =>
        string.Equals(Mode, "fixtures", System.StringComparison.OrdinalIgnoreCase);
}
