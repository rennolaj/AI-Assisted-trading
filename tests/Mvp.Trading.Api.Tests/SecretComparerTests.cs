using Mvp.Trading.Api.Security;
using Xunit;

namespace Mvp.Trading.Api.Tests;

/// <summary>
/// Guards the constant-time secret comparison used for webhook and
/// kill-switch authentication (SEC-2). The critical invariant: an
/// unconfigured (null/empty/whitespace) secret must never match anything,
/// including another empty value.
/// </summary>
public sealed class SecretComparerTests
{
    [Fact]
    public void FixedTimeEquals_MatchingSecrets_ReturnsTrue()
    {
        Assert.True(SecretComparer.FixedTimeEquals("s3cr3t-value", "s3cr3t-value"));
    }

    [Fact]
    public void FixedTimeEquals_DifferentSecrets_ReturnsFalse()
    {
        Assert.False(SecretComparer.FixedTimeEquals("s3cr3t-value", "other-value"));
    }

    [Fact]
    public void FixedTimeEquals_CaseDiffers_ReturnsFalse()
    {
        Assert.False(SecretComparer.FixedTimeEquals("Secret", "secret"));
    }

    [Fact]
    public void FixedTimeEquals_DifferentLengths_ReturnsFalse()
    {
        Assert.False(SecretComparer.FixedTimeEquals("secret", "secret-longer"));
        Assert.False(SecretComparer.FixedTimeEquals("secret-longer", "secret"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FixedTimeEquals_BlankProvided_ReturnsFalse(string? provided)
    {
        Assert.False(SecretComparer.FixedTimeEquals(provided, "expected"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FixedTimeEquals_BlankExpected_NeverMatches(string? expected)
    {
        Assert.False(SecretComparer.FixedTimeEquals("provided", expected));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    [InlineData(null, "")]
    [InlineData("", "   ")]
    public void FixedTimeEquals_BothBlank_ReturnsFalse(string? provided, string? expected)
    {
        // An unconfigured secret must fail closed: identical blank values
        // are still a mismatch, otherwise a missing config would let an
        // empty request secret through.
        Assert.False(SecretComparer.FixedTimeEquals(provided, expected));
    }

    [Fact]
    public void FixedTimeEquals_WhitespacePadding_ReturnsFalse()
    {
        Assert.False(SecretComparer.FixedTimeEquals("secret ", "secret"));
        Assert.False(SecretComparer.FixedTimeEquals(" secret", "secret"));
    }

    [Fact]
    public void FixedTimeEquals_NonAsciiSecrets_ComparedByUtf8Bytes()
    {
        Assert.True(SecretComparer.FixedTimeEquals("señal-🔑", "señal-🔑"));
        Assert.False(SecretComparer.FixedTimeEquals("señal-🔑", "senal-🔑"));
    }
}
