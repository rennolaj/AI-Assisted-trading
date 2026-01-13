using System;
using System.Globalization;
using System.Text.Json;

namespace Mvp.Trading.Api
{
    public static class TradingViewNormalizer
    {
        public sealed record NormalizedPayload(
            string IdempotencyKey,
            string Ticker,
            string Exchange,
            string Interval,
            decimal? Close,
            decimal? Volume,
            string DirectionHint,
            string SymbolHint,
            string Reason
        );

        public static NormalizedPayload Normalize(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                throw new InvalidOperationException("Empty payload.");

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(body);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Invalid JSON payload.", ex);
            }

            var root = doc.RootElement;

            static string? GetStringCaseInsensitive(JsonElement el, params string[] names)
            {
                foreach (var n in names)
                {
                    if (el.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String)
                        return v.GetString();
                }
                foreach (var prop in el.EnumerateObject())
                {
                    foreach (var n in names)
                        if (string.Equals(prop.Name, n, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                            return prop.Value.GetString();
                }
                return null;
            }

            static bool TryParseDecimalLoosely(string? s, out decimal result)
            {
                result = 0;
                if (string.IsNullOrWhiteSpace(s)) return false;
                var cleaned = s.Replace(",", "");
                return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
            }

            static bool TryGetNumberCaseInsensitive(JsonElement el, out decimal value, params string[] names)
            {
                foreach (var n in names)
                {
                    if (el.TryGetProperty(n, out var v))
                    {
                        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out value)) return true;
                        if (v.ValueKind == JsonValueKind.String && TryParseDecimalLoosely(v.GetString(), out value)) return true;
                    }
                }
                foreach (var prop in el.EnumerateObject())
                {
                    foreach (var n in names)
                        if (string.Equals(prop.Name, n, StringComparison.OrdinalIgnoreCase))
                        {
                            var v = prop.Value;
                            if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out value)) return true;
                            if (v.ValueKind == JsonValueKind.String && TryParseDecimalLoosely(v.GetString(), out value)) return true;
                        }
                }

                if (el.TryGetProperty("ohlc", out var ohlc) || el.TryGetProperty("OHLC", out ohlc))
                {
                    if (ohlc.ValueKind == JsonValueKind.Object)
                    {
                        if (ohlc.TryGetProperty("close", out var close) || ohlc.TryGetProperty("Close", out close))
                        {
                            if (close.ValueKind == JsonValueKind.Number && close.TryGetDecimal(out value)) return true;
                            if (close.ValueKind == JsonValueKind.String && TryParseDecimalLoosely(close.GetString(), out value)) return true;
                        }
                    }
                }

                if (el.TryGetProperty("indicator", out var ind) && ind.ValueKind == JsonValueKind.Object)
                {
                    if (ind.TryGetProperty("values", out var vals) && vals.ValueKind == JsonValueKind.Object)
                    {
                        if (vals.TryGetProperty("main", out var main) || vals.TryGetProperty("Main", out main))
                        {
                            if (main.ValueKind == JsonValueKind.Number && main.TryGetDecimal(out value)) return true;
                            if (main.ValueKind == JsonValueKind.String && TryParseDecimalLoosely(main.GetString(), out value)) return true;
                        }
                    }
                }

                value = 0;
                return false;
            }

            var idKey = GetStringCaseInsensitive(root, "idempotencyKey", "IdempotencyKey", "idempotency_key", "Id");
            if (string.IsNullOrWhiteSpace(idKey))
                throw new InvalidOperationException("IdempotencyKey is required in JSON payload.");

            var ticker = GetStringCaseInsensitive(root, "ticker", "Ticker", "symbol", "Symbol", "symbolHint", "SymbolHint") ?? "<unknown>";
            var exchange = GetStringCaseInsensitive(root, "exchange", "Exchange") ?? "unknown";
            var interval = GetStringCaseInsensitive(root, "interval", "Interval", "timeframe") ?? "unknown";

            decimal? close = null;
            if (TryGetNumberCaseInsensitive(root, out var c, "close", "Close")) close = c;

            decimal? volume = null;
            if (TryGetNumberCaseInsensitive(root, out var v, "volume", "Volume")) volume = v;

            var directionHint = GetStringCaseInsensitive(root, "directionHint", "DirectionHint") ?? string.Empty;
            var symbolHint = GetStringCaseInsensitive(root, "symbolHint", "SymbolHint") ?? ticker;
            var reason = GetStringCaseInsensitive(root, "reason", "Reason") ?? string.Empty;

            return new NormalizedPayload(idKey, ticker, exchange, interval, close, volume, directionHint, symbolHint, reason);
        }
    }
}
