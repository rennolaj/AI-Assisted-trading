# M4 Elliott Candidates - Low Level Requirements
Version: 1.2 (aligned)

## Purpose
Define deterministic Elliott Wave candidate generation for MVP M4 based on the
constants locked in `MVP_M4_Elliott_Candidates_CodexReady_v1_2.md`.

## Scope
- Generate `ElliottCandidates` from closed candles.
- Deterministic output ordering, scoring, and IDs.
- Schema-aligned output (`schemas/ElliottCandidates.schema.json`).

Out of scope:
- Diagonal patterns (explicitly disabled).
- Live trading decisions or execution logic.

## Inputs
- `baseTimeframe`: schema-supported only (`M1`, `M5`, `M15`, `H1`, `H4`, `D1`).
- `ElliottParameters`: `PivotMethod`, `Depth`, `DeviationPct`, `MaxCandidates`, optional `ProfileName`.
- `symbol`: market symbol used for lookups and ID hashing.
- `evaluationTimeUtc`: explicit evaluation time; use closed bars only.

## Output
`ElliottCandidates` with:
- `Candidates` list (bounded by `MaxCandidates`).
- Explicit empty list when no candidates are available.
- `RuleViolations` and `Invalidation` per candidate.
- Synthetic candidate for run-level failures (unsupported timeframe or insufficient pivots).

## Timeframe policy
- If `baseTimeframe` is not schema-supported:
  - Emit a single synthetic candidate with `Rule=EW_TIMEFRAME_UNSUPPORTED`.
  - Synthetic candidate uses `WaveLabel=OTHER`, `Score=0`, `Confidence=0`, and invalidation with null values.

## Lookback sizing
Function:
- `lookbackBars = clamp(MinBars, Depth * DepthMultiplier + 200, MaxBars)`
Constants:
- `MinBars = 800`
- `DepthMultiplier = 30`
- `MaxBars = 5000`
- Target minimum pivots: 12
- If pivots insufficient:
  - Emit a single synthetic candidate with `Rule=EW_PIVOTS_INSUFFICIENT`.
  - Synthetic candidate uses `WaveLabel=OTHER`, `Score=0`, `Confidence=0`, and invalidation with null values.

## Pivot extraction (ZigZag)
Method:
- ZigZag with `Depth` + `DeviationPct`.
- Price source: High/Low (wicks).
- Closed bars only; no realtime bars.

Rules:
- Track swing direction and candidate extremes.
- Confirm pivot when:
  - At least `Depth` bars since last confirmed pivot, and
  - Price retraces by `DeviationPct` from the candidate extreme.
- DeviationPct basis: last confirmed pivot price.
  - High -> Low reversal: `((lastPivotHigh - candidateLow) / lastPivotHigh) * 100 >= DeviationPct`
  - Low -> High reversal: `((candidateHigh - lastPivotLow) / lastPivotLow) * 100 >= DeviationPct`
- Enforce alternating HIGH/LOW pivots.

Determinism:
- Use decimal math (avoid double).
- Round comparison inputs using `SnapshotPrecisionDecimals` with
  `MidpointRounding.ToEven`.
- Tie breaks:
  - Prefer earliest bar index.
  - If equal index, prefer more extreme price.

## Candidate generation
Patterns supported:
- Impulse 1-2-3-4-5 only.

Windowing:
- Use sliding windows over pivot list (minimum 6 pivots for 0-1-2-3-4-5).
- Build monowaves between pivots.

Rule checks (hard, ERROR on violation):
- `EW_IMP_R1_W2_NOT_BEYOND_W1_START`
- `EW_IMP_R2_W3_NOT_SHORTEST`
- `EW_IMP_R3_W4_NO_OVERLAP_W1`

Wave labeling:
- `W3`: wave 1 and 2 confirmed, wave 3 underway and breaks wave 1 end.
- `W5END`: full impulse built (1-2-3-4-5), all hard rules pass.
- `OTHER`: everything else that is plausible but not W3/W5END.

## Invalidation
Buffer:
- `InvalidationBufferTicks * TickSize`
Constants:
- `InvalidationBufferTicks = 2`
- Tick size resolution order:
  - instrument metadata (preferred)
  - fallback `TickSize = 0`

Rules:
- W3 LONG: `wave2Low - buffer`
- W3 SHORT: `wave2High + buffer`
- W5END SHORT after uptrend: `wave5High + buffer`
- W5END LONG after downtrend: `wave5Low - buffer`

Tick rounding:
- If `TickSize > 0`: round to tick **away from entry** (conservative).
- Else: round to 6 decimals.

## Scoring and confidence (deterministic)
Score:
- Integer in range 0..100.
- Base points for structural validity and hard-rule pass.
- Add points for guideline checks (Fib, channel, wave strength, alternation, pivot quality).
- Subtract penalties for WARN/ERROR violations.
- Clamp to 0..100.
Weights (max points):
- StructuralValidity: 35
- HardRulePassBonus: 10
- FibGuidelines: 20
- ChannelFit: 10
- Wave3Strength: 15
- AlternationProxy: 5
- PivotQuality: 5
Fib guideline partition:
- W3 >= 1.0 * W1: +12
- W5 approx W1 within [0.85, 1.15]: +8
Penalties:
- WARN: -5 each
- ERROR: -25 each
- DiagonalPenalty: -10 (reserved, diagonals disabled)

Confidence:
- `confidence = score / 100`
- If pivotCount < 10: confidence *= 0.85
- If Depth > 30: confidence *= 0.9
- Round to `SnapshotPrecisionDecimals` and clamp to 0..1.

## Candidate ID determinism
- Stable hash of `(symbol, timeframe, pivot timestamps+prices, parameters)`.
- Use SHA-256 hex, truncated to 16-32 chars for readability.
- Stable ordering required before truncation to ensure consistent IDs.

## RuleViolation.details alignment
- Schema requires `details` as string.
- When structured details are needed:
  - Encode as stable JSON string with sorted keys.

## Components to implement
- `IPivotExtractor` and `ZigZagPivotExtractor`
- `IElliottEngine` with `GenerateCandidatesAsync`
- `ImpulseCandidateBuilder`
- `CandidateScorer`
- `InvalidationCalculator`
- `RuleViolation` helpers (details string encoder)
- Schema validator for `ElliottCandidates`

## Implementation checkpoints
1. Add `ElliottOptions` with the constants above.
2. Implement `LookbackSizer` using the formula above.
3. Implement `ZigZagPivotExtractor` with the DeviationPct definition, closed bars, deterministic ties.
4. Implement `ImpulseCandidateBuilder` (6-pivot windows), hard rules, labeling.
5. Implement `InvalidationCalculator` (buffer + tick rounding).
6. Implement `CandidateScorer` + `ConfidenceCalculator` (deterministic).
7. Implement `CandidateId` hashing and stable ordering; apply `MaxCandidates`.
8. Validate output against `schemas/ElliottCandidates.schema.json` (tests + runtime).

## Tests (acceptance)
- ZigZag deterministic fixtures (pivot list exact match).
- Each impulse hard rule violation emits ERROR.
- Score/confidence determinism and bounds.
- Determinism over repeated runs (byte-identical JSON).
- Schema validation strictness.
- EvaluationTime handling (no realtime bars).
