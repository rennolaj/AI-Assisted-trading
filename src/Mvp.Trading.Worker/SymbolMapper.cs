namespace Mvp.Trading.Worker;

/// <summary>
/// Resolves external symbols into integration-specific identifiers.
/// </summary>
public sealed class SymbolMapper
{
    private readonly IReadOnlyDictionary<string, string> _krakenOverrides;

    /// <summary>
    /// Initializes the mapper with configured symbol overrides.
    /// </summary>
    public SymbolMapper(SymbolMappingOptions options)
    {
        _krakenOverrides = options.KrakenFutures is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(options.KrakenFutures, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the best matching symbol for the given exchange.
    /// </summary>
    public string Resolve(string? exchange, string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return string.Empty;
        }

        var normalized = Normalize(symbol);

        if (IsKrakenExchange(exchange))
        {
            if (_krakenOverrides.TryGetValue(normalized, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
            {
                return Normalize(mapped);
            }
        }

        return normalized;
    }

    private static bool IsKrakenExchange(string? exchange)
    {
        return string.IsNullOrWhiteSpace(exchange)
            || exchange.Contains("kraken", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToUpperInvariant();
    }
}
