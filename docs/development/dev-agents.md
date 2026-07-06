# Multi-Agent Development Pipeline

This project uses a multi-agent pipeline to implement features. Each feature goes through a 7-stage review cycle: planner → rubber-duck → builder → (reviewer + quality in parallel) → tester → integrator → orchestrator. See `CLAUDE.md` for the full role contracts and M14 anti-pattern rules that every agent must follow.

## Branch model (both setups)

Every feature uses **one branch**: `feature/<scope>`.  
All agents work on the same branch. Only the builder commits.  
After committing, the builder saves a diff artifact:
```bash
git diff main..HEAD > /tmp/multi-agent-sync/<scope>/outbox/builder.diff
```
Reviewer, quality, and integrator read this file — they never switch branches.

---

## Claude / GitHub Copilot CLI

Driven by:
- `CLAUDE.md` — auto-read by Claude; defines roles, M14 anti-pattern rules, C# 14 / .NET 10 standards mandate
- `claude-orchestrator.yaml` — Claude project config
- `scripts/agents/bootstrap-feature-claude.sh`
- `scripts/agents/run-feature-once-claude.sh`
- `scripts/agents/create-followup-bugs.sh`
- `docs/development/DRY_RUN_CHECKLIST_CLAUDE.md`

### Communication model

Agent coordination is through `/tmp/multi-agent-sync/<scope>`:
- `context.md` — shared feature context (you write this)
- `inbox/<role>.md` — role instructions
- `outbox/<role>.md` — human-readable reports
- `outbox/<role>.json` — machine-readable findings
- `outbox/builder.diff` — unified diff for review (no branch switching needed)
- `state/<role>.done` — stage completion markers

### Stage order

1. `planner` — deterministic plan
2. `rubber-duck` — plan critique (Claude-native, catches design flaws early)
3. `builder` — implements, commits, saves diff
4. `reviewer` + `quality` — parallel, both read `builder.diff`
5. `tester` — runs full test suite
6. `integrator` — validates dataflow contracts
7. `orchestrator` — final GO / NO-GO decision

Stages are sequenced via `task` tool agents + `read_agent(wait:true)` — no tmux or shell polling.

### Quickstart

1. Bootstrap the sync bus and feature branch:
```bash
./scripts/agents/bootstrap-feature-claude.sh \
  --scope <feature-scope-id> \
  --base main
```

2. Write the feature context:
```bash
nano /tmp/multi-agent-sync/<feature-scope-id>/context.md
```

3. Generate the orchestration prompt (paste into Copilot CLI session):
```bash
./scripts/agents/run-feature-once-claude.sh --scope <feature-scope-id>
```

4. In your Copilot CLI session, Claude acts as orchestrator — it will:
   - Launch each stage as a background `task` agent
   - Receive completion notifications automatically
   - Gate each stage before proceeding

5. After completion, optionally generate follow-up backlog bugs:
```bash
./scripts/agents/create-followup-bugs.sh --scope <feature-scope-id>
```

### Observability

| What you want | Command |
|---|---|
| See all running agents | `list_agents` (in Copilot CLI) |
| Read a completed agent | `read_agent(agent_id=..., wait=true)` |
| Check outbox reports | `ls /tmp/multi-agent-sync/<scope>/outbox/` |
| Check stage completion | `ls /tmp/multi-agent-sync/<scope>/state/` |

### Dry run before real work

Before using on a real feature, validate the setup:
```bash
./scripts/agents/bootstrap-feature-claude.sh \
  --scope dryrun-claude-doc-only \
  --base main \
  --no-branches
```
See `docs/development/DRY_RUN_CHECKLIST_CLAUDE.md` for the full pass criteria checklist.

---

## Codex / AO (original)

Driven by:
- `AGENTS.md` — multi-agent contract for Codex
- `agent-orchestrator.yaml` — AO project config
- `scripts/agents/bootstrap-feature.sh`
- `scripts/agents/run-feature-once-ao.sh`
- `scripts/agents/create-followup-bugs.sh`
- `DRY_RUN_CHECKLIST.md`

### Communication model
- Agent terminals are managed by `tmux`.
- Agent coordination is through `/tmp/multi-agent-sync/<scope>`:
  - `context.md`: shared feature context
  - `inbox/<agent>.md`: role instructions
  - `outbox/<agent>.md`: role outputs
  - `outbox/builder.diff`: unified diff from builder commit
  - `state/<agent>.done`: stage completion markers

### Stage order
1. `planner`
2. `builder`
3. `reviewer` + `quality` (parallel)
4. `tester`
5. `integrator`
6. `orchestrator` final decision

### AO integration (recommended for Codex)

Prerequisites:
- AO CLI installed and available as `ao`
- `agent-orchestrator.yaml` in repo root
- `defaults.agent: codex` in `agent-orchestrator.yaml`
- `tmux` installed

Activation steps:

1. Start AO dashboard + orchestrator:
```bash
ao start
```

2. Bootstrap feature contract files and feature branch:
```bash
./scripts/agents/bootstrap-feature.sh \
  --scope <feature-scope-id> \
  --base main
```

3. Write the feature context:
```bash
nano /tmp/multi-agent-sync/<feature-scope-id>/context.md
```

4. Run AO-based multi-agent pass:
```bash
./scripts/agents/run-feature-once-ao.sh --scope <feature-scope-id>
```

Optional — include automatic backlog follow-up bug generation:
```bash
./scripts/agents/run-feature-once-ao.sh \
  --scope <feature-scope-id> \
  --followup-bugs
```

5. Inspect/operate:
```bash
ao status
ao session ls
```

6. Attach to sessions:
- The AO runner prints all `tmux attach -t <name>` targets after spawning.

7. Cleanup:
```bash
ao session kill <session-id>
# or stop everything:
ao stop AI-Assisted
```

### Legacy tmux-only flow (no AO)

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

5. Observe and monitor:
```bash
tmux attach -t multi-agent-<feature-scope-id>
```
- Window `6` is the monitor.
- `state/*.done` indicates stage completion.
- `outbox/*.md` contains each agent report.

---

## Shared policies (both setups)

- `NO_PUSH`: no `git push` — the human decides when to push
- `INFRA_FREEZE`: no Terraform/Bicep modifications
- `SCOPE`: all changes stay inside the declared feature scope
- `BUILD_GATE`: builder must pass `./scripts/build.sh` before marking done
- `TEST_GATE`: builder must pass `./scripts/test.sh` before marking done
- `SKILL_REF`: all agents must read `docs/development/csharp-dotnet10-skill.md` before writing C#
- `ANTI_PATTERN`: all agents must check M14 anti-patterns in `CLAUDE.md` / `AGENTS.md`

## Backlog follow-up bug policy

If `reviewer`, `quality`, or `integrator` report blocking findings, a backlog bug is auto-added:
```bash
./scripts/agents/create-followup-bugs.sh --scope <feature-scope-id>
```
Auto-added bugs are marked `PRIORITY: NEXT_ITERATION`. Marker format:
- `AUTOBUG:<scope>:reviewer`
- `AUTOBUG:<scope>:quality`
- `AUTOBUG:<scope>:integrator`

## Troubleshooting

**Claude setup:**
- If an agent task gets stuck: check `list_agents` in Copilot CLI; use `read_agent` to see partial output
- If builder diff is empty: ensure builder actually made commits before running `git diff main..HEAD`
- If quality gate fails on M14 patterns: check `CLAUDE.md` anti-pattern section for the exact rule

**Codex/AO setup:**
- If `watch` is not installed on macOS: scripts automatically use a portable `while` loop fallback
- If an agent appears stuck: inspect pane output in tmux; restart only that stage
- If using AO and a session gets stuck: check `ao status`; send corrective instruction with `ao send <session> "<message>"`
- If reviewer/quality do not see builder changes: ensure `outbox/builder.diff` was written correctly
- If you need to re-run backlog bug generation manually: `./scripts/agents/create-followup-bugs.sh --scope <scope>`
