using System;
using System.Collections.Generic;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Worker;

/// <summary>
/// Resolved Elliott profile selection rules and parameters.
/// </summary>
public sealed record ElliottProfileSelection(
    string DefaultProfile,
    string? FallbackProfile,
    IReadOnlyDictionary<string, string> RiskCategoryMap,
    IReadOnlyDictionary<string, ElliottParameters> Profiles)
{
    public string ResolveProfileName(string? riskCategory)
    {
        if (!string.IsNullOrWhiteSpace(riskCategory) &&
            RiskCategoryMap.TryGetValue(riskCategory, out var mapped) &&
            !string.IsNullOrWhiteSpace(mapped))
        {
            return mapped;
        }

        return DefaultProfile;
    }

    public bool TryGetProfile(string profileName, out ElliottParameters parameters)
    {
        if (Profiles.TryGetValue(profileName, out var value) && value is not null)
        {
            parameters = value;
            return true;
        }

        parameters = default!;
        return false;
    }

    public bool TryGetFallback(string? currentProfile, out string profileName, out ElliottParameters parameters)
    {
        profileName = string.Empty;
        parameters = default!;

        if (string.IsNullOrWhiteSpace(FallbackProfile))
        {
            return false;
        }

        if (string.Equals(FallbackProfile, currentProfile, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Profiles.TryGetValue(FallbackProfile, out var fallback))
        {
            return false;
        }

        profileName = FallbackProfile;
        parameters = fallback;
        return true;
    }
}
