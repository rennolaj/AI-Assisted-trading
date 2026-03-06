# Multi-Agent Operating Contract

This file defines the reusable local multi-agent workflow for this repository.

## Roles
- `orchestrator`: gatekeeper and final PASS/CHANGES REQUIRED decision.
- `planner`: deterministic plan, risks, and stop conditions.
- `builder`: implements feature scope.
- `reviewer`: code review findings by severity.
- `quality`: quality standard checks and actionable quality issues.
- `tester`: feature + regression validation.
- `integrator`: integration/dataflow verification.

## Global Policy
- `NO_PUSH` (default): no `git push` by agents in coordinated local multi-agent runs.
- `INFRA_FREEZE`: no Terraform/Bicep changes.
- Keep changes inside feature scope.

## AO PR Flow Exception
- In AO-managed issue sessions, agents may push and create PRs when the session/task instructions explicitly require it.
- When this exception is used, still enforce:
  - tests must pass before push
  - conventional commits
  - issue linkage in commits/PR description
  - focused PR scope

## Branch/Worktree Model
- Branch naming per scope:
  - `agent/planner/<scope>`
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
1. `planner`
2. `builder`
3. `reviewer` + `quality` (parallel)
4. `tester`
5. `integrator`
6. `orchestrator`

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

### 3A) Run one coordinated pass with AO sessions (recommended)
```bash
ao start
./scripts/agents/run-feature-once-ao.sh --scope <feature-scope-id>
```

Optional (enable push/PR actions for AO-managed issue-delivery runs):
```bash
./scripts/agents/run-feature-once-ao.sh \
  --scope <feature-scope-id> \
  --allow-push
```

Optional (auto backlog follow-up bug generation):
```bash
./scripts/agents/run-feature-once-ao.sh \
  --scope <feature-scope-id> \
  --followup-bugs
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

For AO flow:
```bash
ao status
ao session ls
# Attach commands are printed by run-feature-once-ao.sh output.
```

### 6) Full reset
```bash
/opt/homebrew/bin/tmux kill-server
```

### 7) Manual backlog bug generation (if needed)
```bash
./scripts/agents/create-followup-bugs.sh --scope <feature-scope-id>
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
  - `builder` waits for `planner.done`
  - `tester` waits for `reviewer.done` and `quality.done`
  - `integrator` waits for `tester.done`
  - `orchestrator` waits for `integrator.done`

## Backlog Bug Rule
- When `reviewer`, `quality`, or `integrator` report blocking findings, a bug must be added to backlog for next iteration.
- This is automated at orchestrator completion via:
  - `scripts/agents/create-followup-bugs.sh --scope <feature-scope-id>`
- Auto bug marker format:
  - `AUTOBUG:<scope>:reviewer`
  - `AUTOBUG:<scope>:quality`
  - `AUTOBUG:<scope>:integrator`
- Priority label applied:
  - `PRIORITY: NEXT_ITERATION`

## Troubleshooting
- If `watch` is missing on macOS:
  - scripts fall back to a portable monitor loop automatically.
- If a stage is stuck:
  - inspect the pane in tmux;
  - restart only the stuck stage.
- If downstream agents cannot validate builder changes:
  - synchronize feature code into their branch/worktree before rerunning gates.

## Agent Orchestrator (ao) Session

You are running inside an Agent Orchestrator managed workspace.
Session metadata is updated automatically via shell wrappers.

If automatic updates fail, you can manually update metadata:
```bash
~/.ao/bin/ao-metadata-helper.sh  # sourced automatically
# Then call: update_ao_metadata <key> <value>
```

### AO Activation Contract (minimal)
- Use `README.md` + this `AGENTS.md` as the source of truth.
- Required files/scripts:
  - `scripts/agents/bootstrap-feature.sh`
  - `scripts/agents/run-feature-once-ao.sh`
  - `scripts/agents/create-followup-bugs.sh` (optional via `--followup-bugs`)
- Required AO config:
  - `defaults.agent: codex`
  - project `AI-Assisted` present in `agent-orchestrator.yaml`
