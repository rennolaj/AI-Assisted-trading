using System.Collections.Generic;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Elliott;

/// <summary>
/// Context for an impulse candidate window.
/// </summary>
public sealed record ImpulseCandidateContext(
    ImpulseWave Wave,
    string WaveLabel,
    IReadOnlyList<RuleViolation> RuleViolations,
    int PivotCount,
    int Depth,
    bool IsFullPattern,
    bool IsStructuralValid);

/// <summary>
/// Wave points for an impulse candidate.
/// </summary>
public sealed record ImpulseWave(
    PivotPoint P0,
    PivotPoint P1,
    PivotPoint P2,
    PivotPoint P3,
    PivotPoint? P4,
    PivotPoint? P5,
    bool IsUptrend);
