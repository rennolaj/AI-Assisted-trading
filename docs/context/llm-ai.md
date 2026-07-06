# LLM And AI Context

Load this for LLM provider, prompt, advisory, structured output, or persistence work.

## Source Documents

- `docs/llm-ai/local-llm-options.md`
- `docs/milestones/m9-backtesting-fixtures/m9-llm-fixtures-findings.md`
- `docs/milestones/m9-backtesting-fixtures/m9-llm-persistence-design.md`
- `docs/adr/ADR-001-deterministic-gate-llm-advisory-separation.md`
- `docs/adr/ADR-002-signal-snapshot-as-primary-llm-context.md`
- `docs/adr/ADR-003-microsoft-extensions-ai-adoption.md`
- `docs/adr/ADR-004-llm-confluence-advisor.md`
- `docs/adr/ADR-005-llm-stop-loss-advisor.md`
- `docs/adr/ADR-006-post-trade-llm-review-loop.md`
- `docs/adr/ADR-007-market-regime-classifier.md`
- `docs/adr/ADR-008-structured-output-adoption.md`

## Key Boundary

The LLM layer is advisory. Deterministic gates and safety controls remain authoritative.
