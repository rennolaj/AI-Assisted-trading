# AI-Assisted Trading Backend

> .NET 10 event-driven trading backend prototype — TradingView webhook ingestion, Redis queueing, PostgreSQL persistence, Elliott Wave analysis, risk-based position sizing, Kraken Futures demo execution, OpenTelemetry metrics, reconciliation, and emergency kill-switch controls.

**Status**: Demo mode — connected to Kraken Futures sandbox. No live capital.

---

## Architecture

```
TradingView alert
      │
      ▼
ASP.NET Core 10 API          idempotency check · payload normalization · 3s ACK
      │
      ├──► PostgreSQL          raw alert + audit trail
      ▼
  Redis queue
      │
      ▼
  AlertWorker (BackgroundService)
      │
      ├──► Indicator Engine    RSI · MACD · StochRSI · Volume  (M5/M15/M30/H1/H2)
      ├──► Elliott Wave Engine ZigZag pivots · candidate generation · rule checks
      │
      ▼
  Adjudication Gate           deterministic rule engine  (LLM advisory layer: M16/M17)
      │
   ALLOW ──► Risk Engine ──► TradePlan ──► Execution Service ──► Kraken Futures (demo)
   REJECT ──────────────────────────────────────────────────────► PostgreSQL audit
                                                                       ▲
                                                         Reconciliation Worker  60s
```

See [Architecture Reference](docs/architecture.md) for the full component map, persistence model, and design decisions.

---

## Key Features

| Feature | Details |
|---------|---------|
| Webhook ingestion | TradingView Pine Script → `POST /webhooks/tradingview/{secret}`, idempotency by key, 3s ACK |
| Multi-timeframe indicators | RSI, MACD, StochRSI, Volume across M5/M15/M30/H1/H2 — deterministic, fixture-tested |
| Elliott Wave analysis | ZigZag pivot extraction, candidate generation with rule violations and W3/W5END labelling |
| Deterministic gate | 4-rule adjudication engine in pure C# — no LLM on the critical execution path (ADR-001) |
| Risk engine | Policy-driven position sizing, stop-loss anchoring, multi-target take-profit |
| Kill switch | Three levels: `PAUSE_NEW` / `PAUSE_ALL` / `EMERGENCY_STOP` — persisted, audited |
| Reconciliation | Background worker cross-checks internal state with Kraken exchange state every 60s |
| Observability | OpenTelemetry → Prometheus → Grafana; `/health`, `/health/dependencies`, `/health/ready` |
| Demo execution | Kraken Futures sandbox — entry, stop-loss, and take-profit orders placed and tracked |

---

## Quick Start

```bash
# Install .NET SDK (macOS)
brew install --cask dotnet-sdk

# Build and test
./scripts/restore.sh
./scripts/build.sh
./scripts/test.sh

# Run locally with Docker
cp .env.example .env
docker compose up --build
```

> `build.sh` installs and starts PostgreSQL + Redis locally by default. Set `DEV_BOOTSTRAP=0` to skip if you manage these services yourself.

See [Local Development](docs/local-development.md) for Docker, TradingView webhook testing (ngrok), smoke tests, and fixture capture.

---

## Documentation

| Topic | |
|-------|-|
| [Architecture overview](docs/architecture.md) | Component map, data flow, persistence model, design decisions |
| [Local development](docs/local-development.md) | Build, Docker, ngrok, smoke test, fixture capture |
| [Configuration reference](docs/configuration.md) | All runtime config keys by section |
| [Environment files](docs/environment-files.md) | `.env.*` file guide — what goes where |
| [Production deployment](docs/production-deployment-guide.md) | Docker build, DB init, health checks, LLM setup |
| [Kill switch operations](docs/m7.2-kill-switch-operations.md) | Emergency stop procedures and API reference |
| [Metrics and observability](docs/m7.3-metrics-guide.md) | Prometheus metrics catalog and Grafana query examples |
| [ngrok webhook setup](docs/ngrok-quickstart.md) | TradingView webhook tunnel configuration |
| [Architecture Decision Records](docs/adr/ADR-000-index.md) | ADR-001–ADR-018: LLM architecture, Azure security, git hygiene |
| [**Backlog / Roadmap**](docs/backlog.md) | All milestones M0–M19: status, stories, effort estimates |
| [Development agent pipeline](docs/dev-agents.md) | Multi-agent feature workflow (Claude / Codex) |

---

## Roadmap and Known Limitations

Full backlog with story-level detail: [docs/backlog.md](docs/backlog.md).

Pending items of note:

| Area | Story |
|------|-------|
| LLM adjudication → deterministic engine | M16 |
| LLM advisory layer for confluence scoring | M17 |
| AlertWorker refactor into domain services | M14.9.1 |
| Redis at-least-once delivery (list → Streams) | M19.6 |
| String `StartsWith("ALLOW")` → closed enum | M19.2 |
| Azure Key Vault, Managed Identity, VNet | M18 |
| Worker and webhook integration tests | M14.7 |
| CancellationToken propagation (14+ files) | M14.4 |

**Scope note**: This is a backend prototype, not a production trading system. There are no backtests, no PnL attribution, no Sharpe ratio, no slippage model. Execution is demo-only until M18 hardening is complete.

---

## Kill Switch

Three-level emergency control, checked before every execution:

```bash
# Activate
curl -X POST http://localhost:8080/api/killswitch/activate \
  -H "Content-Type: application/json" \
  -d '{"secret":"<KILL_SWITCH_SECRET>","level":"EMERGENCY_STOP","reason":"...","activatedBy":"operator"}'

# Deactivate
curl -X POST http://localhost:8080/api/killswitch/deactivate \
  -H "Content-Type: application/json" \
  -d '{"secret":"<KILL_SWITCH_SECRET>","deactivatedBy":"operator","reason":"..."}'

# Status (no auth)
curl http://localhost:8080/api/killswitch/status
```

Levels: `PAUSE_NEW` (new alerts only) · `PAUSE_ALL` (all workers) · `EMERGENCY_STOP` (cancel orders + pause).
State persists in `system_state`; all activations audited in `kill_switch_audit`.
See [Kill Switch Operations](docs/m7.2-kill-switch-operations.md).

---

## Observability

```bash
curl http://localhost:8080/health               # liveness
curl http://localhost:8080/health/dependencies  # Postgres + Redis
curl http://localhost:8080/metrics              # Prometheus (API)
curl http://localhost:9464/metrics              # Prometheus (Worker)
```

Prometheus UI: http://localhost:9090 · Grafana: http://localhost:3000

See [Metrics Guide](docs/m7.3-metrics-guide.md).

---

## Development Workflow

Features go through a 7-stage multi-agent review pipeline: planner → rubber-duck → builder → (reviewer + quality) → tester → integrator → orchestrator.

```bash
# Bootstrap a feature
./scripts/agents/bootstrap-feature-claude.sh --scope <feature-id> --base main

# Generate orchestration prompt
./scripts/agents/run-feature-once-claude.sh --scope <feature-id>
```

See [Development Agent Pipeline](docs/dev-agents.md) for full setup, stage order, and troubleshooting.
