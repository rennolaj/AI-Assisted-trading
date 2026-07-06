# Documentation Map

This directory is organized for both human navigation and agent context loading.

## Human Navigation

- `architecture/` - system architecture, API contracts, and alert dataflow.
- `development/` - local development, commands, agent workflow, and C#/.NET guidance.
- `configuration/` - runtime configuration, environment files, and fixture mode switching.
- `integrations/` - external system guides for Kraken Futures, TradingView, and ngrok.
- `operations/` - deployment, observability, reconciliation, and kill switch runbooks.
- `milestones/` - milestone-specific requirements, plans, status, and validation artifacts.
- `adr/` - architecture decision records.
- `backlog/` - roadmap and story backlog.
- `llm-ai/` - local LLM design notes.
- `context/` - curated context packs for agents.

## Agent Context Loading

Agents should start with `context-index.yaml`, then load only the context pack and source documents needed for the current scope.

Examples:

- Kraken feature work: `context/builder.md`, `context/trading-pipeline.md`, `integrations/kraken/`, `configuration/configuration.md`.
- M9 fixture validation: `context/tester.md`, `milestones/m9-backtesting-fixtures/`, `configuration/fixture-mode-switching.md`.
- Security work: `context/security.md`, `milestones/m18-security/`, relevant `adr/ADR-009` through `adr/ADR-018`.

Keep new docs in the most specific folder. If a document becomes useful across multiple task types, add it to `context-index.yaml` instead of copying it.
