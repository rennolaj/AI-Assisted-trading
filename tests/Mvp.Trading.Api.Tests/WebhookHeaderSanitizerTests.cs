using Microsoft.AspNetCore.Http;
using Xunit;

namespace Mvp.Trading.Api.Tests;

public sealed class WebhookHeaderSanitizerTests
{
    [Fact]
    public void SanitizeForLogging_RedactsSensitiveHeaders()
    {
        var headers = new HeaderDictionary
        {
            ["Authorization"] = "Bearer super-secret-token",
            ["Cookie"] = "session=very-secret",
            ["X-API-Key"] = "key-123",
            ["X-Webhook-Secret"] = "tv-secret",
            ["X-Hub-Signature-256"] = "sha256=abcdef"
        };

        var sanitized = WebhookHeaderSanitizer.SanitizeForLogging(headers);

        Assert.Equal("[REDACTED]", sanitized["Authorization"]);
        Assert.Equal("[REDACTED]", sanitized["Cookie"]);
        Assert.Equal("[REDACTED]", sanitized["X-API-Key"]);
        Assert.Equal("[REDACTED]", sanitized["X-Webhook-Secret"]);
        Assert.Equal("[REDACTED]", sanitized["X-Hub-Signature-256"]);
    }

    [Fact]
    public void SanitizeForLogging_SanitizesControlCharactersAndTruncates()
    {
        var longValue = new string('a', 300);
        var headers = new HeaderDictionary
        {
            ["User-Agent"] = "TradingView\r\nInjected: bad",
            ["X-Trace"] = longValue
        };

        var sanitized = WebhookHeaderSanitizer.SanitizeForLogging(headers);

        Assert.DoesNotContain('\r', sanitized["User-Agent"]);
        Assert.DoesNotContain('\n', sanitized["User-Agent"]);
        Assert.Equal("TradingView  Injected: bad", sanitized["User-Agent"]);

        Assert.EndsWith("...(truncated)", sanitized["X-Trace"]);
        Assert.Equal(270, sanitized["X-Trace"].Length);
    }

    [Fact]
    public void SanitizeForLogging_RedactsFragmentMatchesCaseInsensitively()
    {
        var headers = new HeaderDictionary
        {
            ["x-custom-token"] = "top-secret",
            ["X-WEBHOOK-SIGNATURE"] = "abc123"
        };

        var sanitized = WebhookHeaderSanitizer.SanitizeForLogging(headers);

        Assert.Equal("[REDACTED]", sanitized["x-custom-token"]);
        Assert.Equal("[REDACTED]", sanitized["X-WEBHOOK-SIGNATURE"]);
    }

    [Fact]
    public void SanitizeForLogging_HandlesEmptyAndMultiValueNonSensitiveHeaders()
    {
        var headers = new HeaderDictionary
        {
            ["X-Empty"] = string.Empty,
            ["X-Multi"] = new[] { "alpha", "beta\r\nvalue" }
        };

        var sanitized = WebhookHeaderSanitizer.SanitizeForLogging(headers);

        Assert.Equal(string.Empty, sanitized["X-Empty"]);
        Assert.Equal("alpha,beta  value", sanitized["X-Multi"]);
    }
}
