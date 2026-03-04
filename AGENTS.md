# Multi-Agent Operating Contract

This file defines the reusable local multi-agent workflow for this repository.

## Roles
- `orchestrator`: gatekeeper and final PASS/CHANGES REQUIRED decision.
- `builder`: implements feature scope.
- `reviewer`: code review findings by severity.
- `quality`: quality standard checks and actionable quality issues.
- `tester`: feature + regression validation.
- `integrator`: integration/dataflow verification.

## Global Policy
- `NO_PUSH`: no `git push` by any agent.
- `INFRA_FREEZE`: no Terraform/Bicep changes.
- Keep changes inside feature scope.

## Branch/Worktree Model
- Branch naming per scope:
  - `agent/builder/<scope>`
  - `agent/reviewer/<scope>`
  - `agent/quality/<scope>`
  - `agent/tester/<scope>`
  - `agent/integrator/<scope>`
- Each agent runs in its own worktree.
- Orchestrator runs on `main` worktree.

## Shared Handoff Bus
Per scope, all handoffs are in:
- `/tmp/multi-agent-sync/<scope>/README.txt`
- `/tmp/multi-agent-sync/<scope>/context.md`
- `/tmp/multi-agent-sync/<scope>/inbox/*.md`
- `/tmp/multi-agent-sync/<scope>/outbox/*.md`
- `/tmp/multi-agent-sync/<scope>/state/*.done`

## Stage Order
1. `builder`
2. `reviewer` + `quality` (parallel)
3. `tester`
4. `integrator`
5. `orchestrator`

## Commands (Reusable)

### 1) Start from scratch
```bash
./scripts/agents/bootstrap-feature.sh \
  --scope <feature-scope-id> \
  --base main \
  --with-tmux \
  --force
```

### 2) Set context for this feature
```bash
nano /tmp/multi-agent-sync/<feature-scope-id>/context.md
```

### 3) Run one coordinated pass (starts agents with `codex exec`)
```bash
./scripts/agents/run-feature-once.sh \
  --scope <feature-scope-id> \
  --session multi-agent-<feature-scope-id>
```

### 4) Run with prepared context file (optional)
```bash
./scripts/agents/run-feature-once.sh \
  --scope <feature-scope-id> \
  --session multi-agent-<feature-scope-id> \
  --context-file <path-to-context.md>
```

### 5) Observe
```bash
tmux attach -t multi-agent-<feature-scope-id>
```
Window `6` monitors `state/` and `outbox/`.

### 6) Full reset
```bash
/opt/homebrew/bin/tmux kill-server
```

## Agent Communication Contract
- All agents read from:
  - `/tmp/multi-agent-sync/<scope>/context.md`
  - `/tmp/multi-agent-sync/<scope>/inbox/<agent>.md`
- All agents write to:
  - `/tmp/multi-agent-sync/<scope>/outbox/<agent>.md`
  - `/tmp/multi-agent-sync/<scope>/state/<agent>.done`
- Orchestrator final output:
  - `/tmp/multi-agent-sync/<scope>/outbox/orchestrator.md`

## Parallelism and Gates
- Parallel stages:
  - `reviewer` and `quality`
- Gate-controlled stages:
  - `tester` waits for `reviewer.done` and `quality.done`
  - `integrator` waits for `tester.done`
  - `orchestrator` waits for `integrator.done`

## Troubleshooting
- If `watch` is missing on macOS:
  - scripts fall back to a portable monitor loop automatically.
- If a stage is stuck:
  - inspect the pane in tmux;
  - restart only the stuck stage.
- If downstream agents cannot validate builder changes:
  - synchronize feature code into their branch/worktree before rerunning gates.
