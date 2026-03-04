using System.Text;
using Microsoft.Extensions.Primitives;

namespace Mvp.Trading.Api;

public static class WebhookHeaderSanitizer
{
    private const string Redacted = "[REDACTED]";
    private const int MaxHeaderValueLength = 256;

    private static readonly HashSet<string> SensitiveHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie",
        "X-API-Key",
        "Api-Key",
        "X-Auth-Token"
    };

    private static readonly string[] SensitiveHeaderNameFragments =
    {
        "authorization",
        "cookie",
        "token",
        "secret",
        "api-key",
        "apikey",
        "signature"
    };

    public static IReadOnlyDictionary<string, string> SanitizeForLogging(IHeaderDictionary headers)
    {
        var result = new Dictionary<string, string>(headers.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            result[header.Key] = SanitizeHeaderValue(header.Key, header.Value);
        }

        return result;
    }

    private static string SanitizeHeaderValue(string headerName, StringValues values)
    {
        if (IsSensitiveHeader(headerName))
        {
            return Redacted;
        }

        if (values.Count == 0)
        {
            return string.Empty;
        }

        var sanitizedValues = new string[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            sanitizedValues[i] = SanitizeText(values[i]);
        }

        return string.Join(',', sanitizedValues);
    }

    private static bool IsSensitiveHeader(string headerName)
    {
        if (SensitiveHeaderNames.Contains(headerName))
        {
            return true;
        }

        foreach (var fragment in SensitiveHeaderNameFragments)
        {
            if (headerName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string SanitizeText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsControl(character))
            {
                builder.Append(' ');
                continue;
            }

            builder.Append(character);
        }

        var sanitized = builder.ToString().Trim();
        if (sanitized.Length <= MaxHeaderValueLength)
        {
            return sanitized;
        }

        return sanitized[..MaxHeaderValueLength] + "...(truncated)";
    }
}
