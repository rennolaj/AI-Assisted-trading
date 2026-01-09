using System;

namespace Mvp.Trading.Contracts;

/// <summary>
/// Strict LLM decision payload used for trade gating.
/// </summary>
public sealed record LlmDecision(
    string Decision,
    decimal Confidence,
    string? ChosenCandidateId,
    string StopLossAnchor,
    string Notes
);

/// <summary>
/// Input payload for LLM Elliott adjudication.
/// </summary>
public sealed record ElliottAdjudicationInput(
    string Direction,
    SignalSnapshot Snapshot,
    ElliottCandidates Candidates,
    RiskPolicy Policy
);

/// <summary>
/// Input payload for LLM stop-loss explanation suggestions.
/// </summary>
public sealed record StopLossExplainInput(
    string Side,
    ElliottCandidate ChosenCandidate,
    SignalSnapshot Snapshot,
    RiskPolicy Policy
);

/// <summary>
/// Advisory stop-loss suggestion from the LLM.
/// </summary>
public sealed record StopLossSuggestion(
    string Anchor,
    decimal? SuggestedStopPrice,
    string? Notes
);
