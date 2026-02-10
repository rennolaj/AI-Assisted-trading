# M9: Elliott visual unit-tests and parameter sweep

This document describes the planned graphical unit-tests and parameter-sweep harness for the ElliottEngine. It's a planning artifact (backlog item) and not an implementation plan — use it to coordinate work, CI configuration, and fixture capture.

## Goal
- Provide deterministic unit tests for `ElliottEngine` that (1) assert candidate acceptance/rejection against fixtures and (2) emit an interactive HTML visual report showing price candles and candidate overlays (accepted = green, rejected = red).
- Provide a parameter-sweep harness to exercise `Depth` and `DeviationPct` (and related parameters) so the team can observe sensitivity and choose robust defaults.

## Success criteria
- Deterministic fixtures under `tests/fixtures/elliott/` that encode expected accept/reject labels.
- Visual HTML reports in `tests/visual-reports/{fixture}[-depthX-devY].html` produced by tests and available as CI artifacts for failed runs (or on-demand).
- A CSV/JSON summary of parameter-sweep results with rows: fixture, depth, deviation, candidate_count, accepted_count, avg_score, avg_confidence.
- Tests are fast (fixtures small) and deterministic in CI.

## Files & artifacts
- Fixtures: `tests/fixtures/elliott/*.json`
- Visual reports: `tests/visual-reports/*.html`
- Test harness: `tests/Mvp.Trading.Elliott.Tests/ElliottEngine.VisualTests.cs`
- Visualizer helper: `tests/Mvp.Trading.Elliott.Tests/Helpers/ElliottVisualizer.cs`
- Sweep outputs: `tests/visual-reports/sweeps/{fixture}-sweep.csv`
- Docs/runbook: `docs/m9-elliott-visual-tests.md`

## Step-by-step plan (high level)
1. Design fixture JSON schema and add 2 example fixtures (one ACCEPT, one REJECT).
2. Design the visualizer output (HTML + Plotly) and agree the marker/hover metadata format.
3. Implement a single prototype test that runs the engine on a fixture and writes an HTML report (prototype only).
4. Add parameterized harness to run a small grid of `Depth × DeviationPct` and write per-config HTMLs plus a CSV summary.
5. Integrate into CI as artifact collection for failing tests (or dedicated nightly experiment job).
6. Create a short runbook describing how to run tests locally and view reports.

## Parameter-sweep details
- Parameters to sweep (suggested):
  - `Depth` ∈ {1,2,3,4,5,8,13}
  - `DeviationPct` ∈ {0.01,0.02,0.05,0.1,0.2,0.4}
- For each (Depth, Deviation) run and capture:
  - `candidate_count`, `accepted_count`, `avg_score`, `avg_confidence`, `rule_violation_summary`.
- Persist each run as a CSV row for downstream analysis.

## CI integration
- Add optional job that runs the visual tests (on-demand or nightly) and uploads `tests/visual-reports/**.html` and `tests/visual-reports/sweeps/*.csv` as build artifacts.
- For pull requests, only run the lightweight prototype test; collect artifacts on failure.

## Docs & runbook
- Add `docs/m9-elliott-visual-tests.md` (this file) with how-to steps:
  - Run a single fixture locally: `dotnet test --filter Category=VisualElliott` (example)
  - Open generated HTML report at `tests/visual-reports/{fixture}.html` in a browser.
  - Run a parameter sweep and inspect `sweep.csv` with Jupyter or a small dashboard.

## Analysis & dashboards
- Export CSVs to a lightweight notebook (Jupyter) or simple HTML dashboard to visualize how `Depth` / `DeviationPct` affect candidate counts, average score, and acceptance rate.

## Estimates
- Prototype (1 fixture + visualizer + one test): 3–6 hours.
- Full harness (5 fixtures + parameter sweep + CI + docs): 1–2 days.

## Risks & mitigations
- Large fixtures slow tests — keep fixtures small (200–1000 candles).
- Plotly CDN blocked in CI — vendor a local Plotly copy if necessary.
- Non-determinism — ensure `ElliottEngine` is deterministic or allow seeding.

---

If you want I can implement a small prototype next (create the visualizer helper and a single visual test). Otherwise this backlog item is ready to be scheduled by the team.
