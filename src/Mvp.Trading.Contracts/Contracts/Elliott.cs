using System.Collections.Generic;

namespace Mvp.Trading.Contracts;

/// <summary>
/// Configuration for Elliott candidate generation.
/// </summary>
public sealed record ElliottParameters(
    string PivotMethod,
    int Depth,
    decimal DeviationPct,
    int MaxCandidates
);

/// <summary>
/// Collection of Elliott candidates for a base timeframe.
/// </summary>
public sealed record ElliottCandidates(
    Timeframe BaseTimeframe,
    IReadOnlyList<ElliottCandidate> Candidates
);

/// <summary>
/// Candidate Elliott count with rule violations and invalidation levels.
/// </summary>
public sealed record ElliottCandidate(
    string CandidateId,
    string PatternType,
    string WaveLabel,
    decimal Score,
    decimal Confidence,
    IReadOnlyList<RuleViolation> RuleViolations,
    InvalidationLevels Invalidation
);

/// <summary>
/// Rule violation captured during candidate evaluation.
/// </summary>
public sealed record RuleViolation(string Rule, string Severity, string Details);

/// <summary>
/// Price levels that invalidate the candidate for long or short scenarios.
/// </summary>
public sealed record InvalidationLevels(decimal? LongInvalidationPrice, decimal? ShortInvalidationPrice);
