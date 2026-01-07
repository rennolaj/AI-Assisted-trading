using System;

namespace Mvp.Trading.Integrations.Kraken;

/// <summary>
/// Normalizes canonical symbols to Kraken Futures API symbols.
/// </summary>
internal static class KrakenFuturesSymbolFormatter
{
    public static string Normalize(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return string.Empty;
        }

        var normalized = symbol.Trim().ToUpperInvariant();
        if (normalized.StartsWith("PI_", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("PF_", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        var dotIndex = normalized.LastIndexOf('.', normalized.Length - 1);
        if (dotIndex <= 0 || dotIndex >= normalized.Length - 1)
        {
            return normalized;
        }

        var baseQuote = normalized[..dotIndex];
        var suffix = normalized[(dotIndex + 1)..];
        baseQuote = NormalizeBase(baseQuote);

        return suffix switch
        {
            "P" => $"PI_{baseQuote}",
            "F" => $"PF_{baseQuote}",
            _ => normalized
        };
    }

    private static string NormalizeBase(string baseQuote)
    {
        if (baseQuote.StartsWith("BTC", StringComparison.OrdinalIgnoreCase))
        {
            return "XBT" + baseQuote[3..];
        }

        return baseQuote;
    }
}
