# Reviewer Agent Context

Load this for code review and regression risk analysis.

## Start With

- `docs/context/core.md`
- `docs/adr/ADR-000-index.md`
- `docs/development/csharp-dotnet10-skill.md`

## Add By Scope

- Dataflow changes: `docs/context/trading-pipeline.md`
- Operational changes: `docs/context/operations.md`
- Security-sensitive changes: `docs/context/security.md`
- LLM changes: `docs/context/llm-ai.md`

## Review Focus

- Behavioral regressions.
- ADR violations.
- Missing or weak tests.
- Safety boundaries around execution, kill switch, reconciliation, and secrets.
