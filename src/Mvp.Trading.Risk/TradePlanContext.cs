using System;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Risk;

/// <summary>
/// Deterministic inputs required to build a trade plan.
/// </summary>
public sealed record TradePlanContext(
    AlertEvent Alert,
    SignalSnapshot Snapshot,
    ElliottCandidates Candidates,
    LlmDecision Decision,
    RiskPolicy Policy,
    string RiskPolicyVersion,
    string DecisionSchemaVersion,
    DateTimeOffset EvaluationTimeUtc
);
