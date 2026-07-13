using System.Security.Cryptography;
using System.Text;

namespace Mvp.Trading.Api.Security;

/// <summary>
/// Constant-time secret comparison to prevent timing attacks.
/// </summary>
internal static class SecretComparer
{
    /// <summary>
    /// Compares two secrets in constant time. Returns false when either value
    /// is null, empty, or whitespace (an unconfigured secret never matches).
    /// </summary>
    public static bool FixedTimeEquals(string? provided, string? expected)
    {
        if (string.IsNullOrWhiteSpace(provided) || string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(expected));
    }
}
