# AI-Assisted-trading
This repository contains everything about my own AI assisted trading server.

## Tooling
- .NET SDK: 10.0.x (installed via Homebrew cask `dotnet-sdk`)
- Scripts: `scripts/restore.sh`, `scripts/build.sh`, `scripts/test.sh`

## Multi-Agent Setup (Any Feature)

This setup is fully generic and driven by two scripts:
- `scripts/agents/bootstrap-feature.sh`
- `scripts/agents/run-feature-once.sh`
- `scripts/agents/run-feature-once-ao.sh`
- `scripts/agents/create-followup-bugs.sh`

It runs agents in parallel with file-based communication and stage gates.

### Communication model
- Agent terminals are managed by `tmux`.
- Agent coordination is through `/tmp/multi-agent-sync/<scope>`:
  - `context.md`: shared feature context
  - `inbox/<agent>.md`: role instructions
  - `outbox/<agent>.md`: role outputs
  - `state/<agent>.done`: stage completion markers

### Stage order
1. `planner`
2. `builder`
3. `reviewer` + `quality` (parallel)
4. `tester`
5. `integrator`
6. `orchestrator` final decision

### AO integration (recommended)

Use this path when you want AO-managed sessions, dashboard visibility, and `ao` lifecycle commands.

Prerequisites:
- AO CLI installed and available as `ao`
- `agent-orchestrator.yaml` in repo root
- `defaults.agent: codex` in `agent-orchestrator.yaml`
- `tmux` installed

Minimal AO config for this repo (already supported):
```yaml
defaults:
  runtime: tmux
  agent: codex
  workspace: worktree

projects:
  AI-Assisted:
    repo: rennolaj/AI-Assisted-trading
    path: /Users/jrennola/Hobby/AI-Assisted
    defaultBranch: main
    tracker:
      plugin: github
```

Activation steps:

1. Start AO dashboard + orchestrator:
```bash
ao start
```

2. Bootstrap feature contract files/worktrees:
```bash
./scripts/agents/bootstrap-feature.sh \
  --scope <feature-scope-id> \
  --base main
```

3. Run AO-based multi-agent pass:
```bash
./scripts/agents/run-feature-once-ao.sh --scope <feature-scope-id>
```

AO PR-flow readiness preflight (recommended before first run in a new environment):
```bash
./scripts/agents/check-ao-pr-flow-readiness.sh --smoke
```
- Required auth/env:
  - `gh auth login` completed (`gh auth status` passes)
  - `LINEAR_API_KEY` or `COMPOSIO_API_KEY` exported for Linear tracker path
- Required AO YAML fields:
  - `defaults.agent`
  - `projects.<id>.repo`
  - `projects.<id>.path`
  - `projects.<id>.defaultBranch`
  - `projects.<id>.tracker.plugin` (`github|linear|composio`)

You can enforce this preflight as a fail-fast gate when running AO sessions:
```bash
./scripts/agents/run-feature-once-ao.sh \
  --scope <feature-scope-id> \
  --pr-flow-readiness
```

Optional: include automatic backlog follow-up bug generation:
```bash
./scripts/agents/run-feature-once-ao.sh \
  --scope <feature-scope-id> \
  --followup-bugs
```

4. Inspect/operate:
```bash
ao status
ao session ls
```

5. Attach to sessions:
- The AO runner prints all `tmux attach -t <name>` targets after spawning.
- Orchestrator session is also available from `ao start` output.

6. Cleanup:
```bash
ao session kill <session-id>
# or stop everything:
ao stop AI-Assisted
```

### Legacy tmux-only flow

Use this path only if you explicitly want the original custom tmux session orchestration (without AO session layer).

1. Start clean (optional but recommended):
```bash
/opt/homebrew/bin/tmux kill-server
```

2. Bootstrap a feature scope:
```bash
./scripts/agents/bootstrap-feature.sh \
  --scope <feature-scope-id> \
  --base main \
  --with-tmux \
  --force
```

3. Set shared context:
```bash
nano /tmp/multi-agent-sync/<feature-scope-id>/context.md
```

4. Dispatch one coordinated run:
```bash
./scripts/agents/run-feature-once.sh \
  --scope <feature-scope-id> \
  --session multi-agent-<feature-scope-id>
```

Alternative dispatch with pre-written context:
```bash
./scripts/agents/run-feature-once.sh \
  --scope <feature-scope-id> \
  --session multi-agent-<feature-scope-id> \
  --context-file <path-to-context.md>
```

5. Observe and monitor:
```bash
tmux attach -t multi-agent-<feature-scope-id>
```
- Window `6` is the monitor.
- `state/*.done` indicates stage completion.
- `outbox/*.md` contains each agent report.

6. Verify completion:
```bash
ls -1 /tmp/multi-agent-sync/<feature-scope-id>/state
ls -1 /tmp/multi-agent-sync/<feature-scope-id>/outbox
```

7. Check backlog follow-up bugs:
```bash
rg -n "AUTOBUG:<feature-scope-id>:" docs/backlog.md
```

Backlog bug policy:
- If `reviewer`, `quality`, or `integrator` report blocking findings, a backlog bug is auto-added.
- Auto-added bugs are marked `PRIORITY: NEXT_ITERATION`.
- The auto bug marker format is:
  - `AUTOBUG:<scope>:reviewer`
  - `AUTOBUG:<scope>:quality`
  - `AUTOBUG:<scope>:integrator`

### Troubleshooting
- If `watch` is not installed on macOS:
  - scripts automatically use a portable `while` loop monitor fallback.
- If an agent appears stuck:
  - inspect pane output in tmux;
  - restart only that stage by re-running dispatch or sending a new `codex exec` in that pane.
- If using AO flow and a session gets stuck:
  - check `ao status`;
  - attach using printed tmux target;
  - send corrective instruction with `ao send <session> "<message>"`.
- If reviewer/quality/tester do not see builder changes:
  - ensure code handoff is present in their branches/worktrees before re-running gates.
- If you need to re-run backlog bug generation manually:
```bash
./scripts/agents/create-followup-bugs.sh --scope <feature-scope-id>
```

### Policy constraints (always enforced)
- `NO_PUSH`: no `git push`.
- `INFRA_FREEZE`: no Terraform/Bicep modifications.

## Quick start
```bash
brew install --cask dotnet-sdk
./scripts/restore.sh
./scripts/build.sh
./scripts/test.sh
```

## Docker

### Quick Start (Local Development)
```bash
cp .env.example .env
docker compose up --build
```

### Production Deployment

For complete production deployment with `.env.prod.local`, see the **[Production Deployment Guide](docs/production-deployment-guide.md)** for step-by-step instructions including:
- Container build and startup procedures
- Database initialization and health checks
- Service verification and monitoring
- TradingView webhook configuration
- LLM provider setup (OpenAI, Local, or Auto-fallback)
- Troubleshooting common issues

### With TradingView Webhook Access (ngrok)
```bash
# Copy and configure environment
cp .env.demo .env.demo.local
nano .env.demo.local  # Add NGROK_AUTHTOKEN and other credentials

# Start with ngrok enabled
docker compose --env-file .env.demo.local --profile ngrok up --build -d

# Get your webhook URL
./scripts/get-ngrok-url.sh
```

See [ngrok Quick Start](docs/ngrok-quickstart.md) for complete webhook setup.

### Configuration

Set `KRAKEN_FUTURES_ENV=demo` (sandbox) or `KRAKEN_FUTURES_ENV=prod` (live) in `.env` to switch Kraken Futures environments.
Endpoint defaults live in `config/kraken-futures.json`.
Set `OPENAI_API_KEY` in `.env` for MCP adjudication (keep it local, never commit secrets).
Set `MCP_PROVIDER=openai|local|auto` to choose OpenAI, a local LLM, or OpenAI with local fallback on 429.
When using a local LLM, configure `LOCAL_LLM_BASE_URL` and optionally `LOCAL_LLM_MODEL_OVERRIDE`.
For LM Studio, use `LOCAL_LLM_BASE_URL=http://host.docker.internal:1234/v1/` and `LOCAL_LLM_MODE=chat`.
See `docs/local-llm-options.md` for local runtime/model notes.

**Complete environment file documentation:** [Environment Files Guide](docs/environment-files.md)

### Environment Switching for Docker
For safe environment management (simulated/demo/prod), use the environment switcher:

```bash
# Switch to simulated environment
./scripts/switch-env.sh simulated

# Update .env to match (docker-compose environment variable)
# For simulated, set: KRAKEN_FUTURES_ENV=demo (uses simulated mode internally)
# Then rebuild containers
docker compose up --build
```

```bash
# Switch to demo environment
./scripts/switch-env.sh demo

# Update .env to match
# Set: KRAKEN_FUTURES_ENV=demo
docker compose up --build
```

```bash
# Switch to production (requires CONFIRM=yes)
CONFIRM=yes ./scripts/switch-env.sh prod

# Update .env to match
# Set: KRAKEN_FUTURES_ENV=prod
docker compose up --build
```

⚠️ **Important**: The switch-env.sh script updates `config/execution.json` which is copied into the Docker image at build time. Always rebuild containers after switching environments. See `docs/environment-switching.md` for detailed guidance and safety checklists.

## Smoke test (ngrok)
This uses `scripts/smoke.sh` plus a local `.env.smoke` file (not committed).

1) Create `.env.smoke` with your TradingView secret and smoke inputs:
```bash
TRADINGVIEW_WEBHOOK_SECRET=your-secret-here
SYMBOL_HINT=BTCUSD.P
TICKER=BTCUSD.P
EXCHANGE=krakenfutures
INTERVAL=1
KRAKEN_FUTURES_ENV=demo
KRAKEN_FUTURES_DEMO_API_KEY=your-demo-key
KRAKEN_FUTURES_DEMO_API_SECRET=your-demo-secret
SLEEP_SECONDS=3
SMOKE_TIMEOUT_SECONDS=300
NGROK_AUTOSTART=1
```

2) Start API + worker using the smoke env file (does not touch dev config):
```bash
docker compose --env-file .env.smoke up -d --build api worker
```

3) The script will start ngrok automatically (port 8080) and stop it after the timeout.

4) In TradingView, load `tests/pineScript/mvp-smoke-test-alerts.pine` and create an alert:
- Webhook URL: `https://your-ngrok-host.ngrok-free.dev/webhooks/tradingview/your-secret-here`
- Message: `{{alert_message}}`
- Condition: "Any alert() function call"

5) Run the smoke test:
```bash
./scripts/smoke.sh
```

Health checks:
- `http://localhost:8080/health`
- `http://localhost:8080/health/dependencies`

Trade monitoring seed:
- `POST http://localhost:8080/trades/open`
Example:
```bash
curl -X POST http://localhost:8080/trades/open \
  -H "Content-Type: application/json" \
  -d '{"exchangeId":"kraken-futures","symbol":"BTCUSD.P","side":"LONG","entryPrice":70000,"invalidationPrice":68000}'
```

## Kill Switch (Emergency Controls)
The system includes a three-level kill switch for emergency halting of trading operations:

**Kill Switch Levels:**
- `PAUSE_NEW`: Stops only new alert processing (prevents new trades)
- `PAUSE_ALL`: Pauses all background workers (alerts, reconciliation, monitoring)
- `EMERGENCY_STOP`: Cancels all open orders and pauses all operations

**API Endpoints:**
```bash
# Check kill switch status (no authentication required)
curl http://localhost:8080/api/killswitch/status

# Activate kill switch
curl -X POST http://localhost:8080/api/killswitch/activate \
  -H "Content-Type: application/json" \
  -d '{
    "secret": "your-secret-here",
    "level": "EMERGENCY_STOP",
    "reason": "Market conditions require immediate halt",
    "activatedBy": "operator-name"
  }'

# Deactivate kill switch
curl -X POST http://localhost:8080/api/killswitch/deactivate \
  -H "Content-Type: application/json" \
  -d '{
    "secret": "your-secret-here",
    "deactivatedBy": "operator-name",
    "reason": "Issue resolved"
  }'
```

**Configuration:**
Set `KILL_SWITCH_SECRET` environment variable (or `KillSwitchApi:Secret` in config):
```bash
export KILL_SWITCH_SECRET="your-secure-secret-here"
```

**Worker Behavior:**
- Workers check kill switch before each processing iteration
- When paused, workers log status and continue health checks
- EMERGENCY_STOP cancels all open orders before pausing
- State persists in PostgreSQL `system_state` table
- All actions are audited in `kill_switch_audit` table

**Monitoring:**
```sql
-- Check current status
SELECT * FROM system_state WHERE key = 'kill_switch';

-- View audit history
SELECT * FROM kill_switch_audit ORDER BY ts DESC LIMIT 20;
```

See `docs/m7.2-kill-switch-operations.md` for emergency procedures.

## Observability & Metrics

The system exposes OpenTelemetry metrics for monitoring and observability.

**Metrics Endpoints:**
```bash
# API metrics (Prometheus format)
curl http://localhost:8080/metrics

# Worker metrics (Prometheus format)
curl http://localhost:9464/metrics
```

**Health Check Endpoints:**
```bash
# Basic health check
curl http://localhost:8080/health

# Dependency health (Postgres, Redis)
curl http://localhost:8080/health/dependencies

# Kubernetes readiness probe
curl http://localhost:8080/health/ready

# Kubernetes liveness probe
curl http://localhost:8080/health/live
```

**Prometheus & Grafana:**
When running with docker-compose, Prometheus and Grafana are automatically configured:
- **Prometheus UI**: http://localhost:9090
  - Pre-configured to scrape API (port 8080) and Worker (port 9464)
  - 15-second scrape interval
- **Grafana UI**: http://localhost:3000
  - Default credentials: `admin` / `admin`
  - Prometheus datasource pre-configured
  - Create custom dashboards to visualize metrics

**Key Metrics:**
- `alerts_received_total` - Counter of alerts received (by exchange, symbol)
- `alerts_processed_total` - Counter of alerts processed (by outcome)
- `alert_processing_duration_seconds` - Histogram of processing duration
- `queue_depth` - Gauge of current Redis queue depth
- `orders_placed_total` - Counter of orders placed (by direction, type)
- `orders_filled_total` - Counter of filled orders
- `active_trades` - Gauge of currently open positions
- `errors_total` - Counter of errors (by component, type)

See `docs/m7.3-metrics-guide.md` for complete metrics catalog and Prometheus query examples.

## Reconciliation System
The system continuously monitors order state consistency between internal tracking and exchange state:

**Automatic Detection:**
- Runs every 60 seconds via background worker (configurable via `Reconciliation:IntervalSeconds`)
- Detects 6 types of discrepancies:
  - `MISSING_ON_EXCHANGE`: Order placed internally but not found on exchange
  - `ORPHANED_ON_EXCHANGE`: Order exists on exchange but not tracked internally
  - `STATUS_MISMATCH`: Order status differs between internal and exchange state
  - `FILL_MISMATCH`: Fill quantity/price discrepancies
  - `INVALIDATION_TRIGGERED`: Stop-loss/take-profit triggered but not reflected in status
  - `ERROR`: Reconciliation process errors

**Manual Resolution:**
When discrepancies are detected, review them in the database:
```sql
-- View recent discrepancies
SELECT * FROM reconciliation_discrepancy 
WHERE detected_at > NOW() - INTERVAL '1 hour'
ORDER BY detected_at DESC;

-- Check reconciliation state
SELECT * FROM reconciliation_state 
ORDER BY checked_at DESC LIMIT 10;
```

**Resolution Steps:**
1. Investigate the discrepancy details (stored as JSON in `expected_state` and `actual_state` columns)
2. Verify the actual exchange state using exchange UI or API
3. Take corrective action:
   - Cancel orphaned orders on exchange
   - Update internal state to match exchange reality
   - Re-submit missing orders if appropriate
4. Monitor subsequent reconciliation runs to ensure resolution

See `docs/m7-low-level-requirements.md` for detailed technical specifications.

## Kraken integration tests
These are disabled by default. To run them against demo endpoints:
```bash
export KRAKEN_FUTURES_INTEGRATION_TESTS=1
export KRAKEN_FUTURES_REST_BASE=https://demo-futures.kraken.com/derivatives/api/v3
export KRAKEN_FUTURES_TEST_SYMBOL=BTCUSD.P
./scripts/test.sh
```

## Fixture capture (Kraken Futures history)
Capture real trade history and aggregate into candles for tests:
```bash
SYMBOL=PF_ETHUSD INTERVAL_MINUTES=1 DURATION_SECONDS=600 \
  ./scripts/fixtures/capture-futures-history.sh
```
The output defaults to `tests/fixtures/kraken-futures/<symbol>_m<interval>.json`.

## Runtime configuration
- `TradingView:WebhookSecret`
- `Postgres:ConnectionString`
- `Redis:ConnectionString`
- `Redis:AlertQueueKey` (default: `mvp:alerts`)
- `OpenAI:ApiKey`
- `OpenAI:BaseUrl`
- `OpenAI:Organization`
- `OpenAI:Project`
- `McpProvider:Provider`
- `McpProvider:FallbackOnOpenAi429`
- `LocalLlm:BaseUrl`
- `LocalLlm:ApiKey`
- `LocalLlm:ResponsesPath`
- `LocalLlm:ChatCompletionsPath`
- `LocalLlm:Mode`
- `LocalLlm:UseResponseFormat`
- `LocalLlm:ModelOverride`
- `Worker:PollIntervalMs`
- `Reconciliation:IntervalSeconds` (default: 60)
- `KillSwitchApi:Secret` (required for activate/deactivate endpoints)
- `KrakenFutures:Environment`
- `KrakenFutures:BaseUrl`
- `KrakenFutures:AuthBaseUrl`
- `KrakenFutures:WebSocketUrl`
- `KrakenFutures:TestSymbol`
- `KrakenFutures:ApiKey`
- `KrakenFutures:ApiSecret`
- `KrakenFutures:DemoApiKey`
- `KrakenFutures:DemoApiSecret`
- `KrakenFutures:ProdApiKey`
- `KrakenFutures:ProdApiSecret`
- `KrakenFutures:TimeoutSeconds`
- `KrakenFutures:Cache:InstrumentsTtlSeconds`
- `KrakenFutures:Cache:TickersTtlSeconds`
- `KrakenFutures:Cache:CandlesTtlSeconds`
- `KrakenFutures:RateLimit:MaxCostPerWindow`
- `KrakenFutures:RateLimit:WindowSeconds`
- `KrakenFutures:RateLimit:InstrumentsCost`
- `KrakenFutures:RateLimit:TickersCost`
- `KrakenFutures:RateLimit:CandlesCost`
- `Elliott:BaseTimeframe`
- `Elliott:Parameters:PivotMethod`
- `Elliott:Parameters:Depth`
- `Elliott:Parameters:DeviationPct`
- `Elliott:Parameters:MaxCandidates`
- `Elliott:TickSizeFallback`
- `Elliott:TickSizeOverrides` (per-symbol overrides, e.g. `Elliott:TickSizeOverrides:BTCUSD.P=0.5`)

## Local services
`./scripts/build.sh` will install and start Postgres/Redis and create the `ai-trading-db` database by default. To disable, set `DEV_BOOTSTRAP=0`.
On Linux, `scripts/dev/bootstrap.sh` uses `apt-get` and may require `sudo`. If your Postgres uses a non-default superuser, set `PG_USER`.

## Container notes
When running in containers, set connection strings via environment variables so the API can reach sibling Postgres/Redis containers on the same network, for example:
`Postgres__ConnectionString=Host=postgres;Port=5432;Database=ai-trading-db;Username=postgres;Password=postgres`
`Redis__ConnectionString=redis:6379`
