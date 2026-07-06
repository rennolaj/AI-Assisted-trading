# Dry Run Checklist — Claude (Copilot CLI)

Use this checklist to validate the Claude multi-agent setup before real feature work.
This is the Claude-native equivalent of `DRY_RUN_CHECKLIST.md` (which is Codex/tmux-oriented).

---

## Goal

Validate the orchestration flow, stage gates, policy compliance, and C# 14 standard
enforcement using Claude's task-tool pipeline — without making risky code changes.

---

## Pre-flight

- [ ] `CLAUDE.md` exists at repo root
- [ ] `docs/development/csharp-dotnet10-skill.md` exists
- [ ] `claude-orchestrator.yaml` exists at repo root
- [ ] `scripts/agents/bootstrap-feature-claude.sh` is executable (`chmod +x`)
- [ ] `scripts/agents/run-feature-once-claude.sh` is executable (`chmod +x`)
- [ ] `scripts/agents/create-followup-bugs.sh` is executable
- [ ] `docs/backlog/backlog.md` is accessible and M14 stories are populated

---

## Scope Selection

1. Pick a harmless, doc-only scope id: `dryrun-claude-doc-only`
2. Confirm: no source code changes required, no infra changes

---

## Bootstrap

Run:
```bash
./scripts/agents/bootstrap-feature-claude.sh \
  --scope dryrun-claude-doc-only \
  --base main \
  --no-branches
```

Verify:
- [ ] `/tmp/multi-agent-sync/dryrun-claude-doc-only/` created
- [ ] `context.md` placeholder exists
- [ ] All `inbox/<role>.md` files created (planner, builder, reviewer, quality, tester, integrator, orchestrator)
- [ ] `outbox/`, `state/`, `prompts-claude/` directories created

---

## Context Setup

Edit context:
```bash
nano /tmp/multi-agent-sync/dryrun-claude-doc-only/context.md
```

Set:
```markdown
## Scope
Add a sentence to docs/development/command-reference.md explaining the Claude multi-agent setup.

## Acceptance Criteria
- One sentence added to docs/development/command-reference.md
- No source code changed
- Build passes
- Tests pass (no changes expected)

## Constraints
- Doc-only change. No src/ modifications allowed.
```

---

## Pipeline Execution (Claude-Native)

Unlike the Codex setup (which uses `tmux` windows + `ao spawn` + shell sleep loops),
the Claude pipeline runs as **sequential background task agents** with built-in notifications.

### How stages work in Claude

1. **Launch** a stage with `task(agent_type=..., mode="background")`
2. **Wait** — you will receive a `system_notification` when it completes
3. **Read** results with `read_agent(agent_id=..., wait=true)`
4. **Gate check** — verify stage passed before launching next
5. **Launch next stage** (or parallel stages together in one response)

### Observability

| Codex setup | Claude equivalent |
|-------------|-------------------|
| `tmux attach -t <session>` | System notifications appear automatically in chat |
| `ao status` | `list_agents` tool |
| `ao session ls` | `list_agents(include_completed=true)` |
| Watch `state/` files | Read `read_agent(agent_id=..., wait=true)` |
| Window 6 monitor pane | No equivalent — notifications are automatic |

---

## Stage Execution — Dry Run Order

### Stage 1: Planner

Launch:
```
task(agent_type="general-purpose", mode="background", name="planner-dryrun")
Prompt: read context.md, produce plan for doc-only change to command-reference.md
```

Gate check:
- [ ] Agent completed (system_notification received)
- [ ] `read_agent` returns status: success
- [ ] Plan identifies exactly one file: `docs/development/command-reference.md`
- [ ] Plan has no source code changes
- [ ] Policy check in output: `NO_PUSH=confirmed, INFRA_FREEZE=confirmed`

### Stage 2: Rubber-Duck (plan validation)

Launch:
```
task(agent_type="rubber-duck", mode="sync")
Give: full planner output
Ask: are there any risks or missing steps?
```

Gate check:
- [ ] Rubber-duck confirms plan is low-risk
- [ ] No critical critique that would block the builder

### Stage 3: Builder

Launch:
```
task(agent_type="general-purpose", mode="background", name="builder-dryrun")
Prompt: implement the doc change per planner output; run build and test
```

Gate check:
- [ ] `outbox/builder.md` written
- [ ] Build command result: PASS
- [ ] Test command result: PASS (no regressions)
- [ ] Only `docs/development/command-reference.md` modified
- [ ] Policy check confirmed

### Stage 4: Reviewer + Quality (parallel)

Launch BOTH in one response:
```
task(agent_type="code-review", mode="background", name="reviewer-dryrun")
task(agent_type="general-purpose", mode="background", name="quality-dryrun")
```

Gate check (both must pass before tester):
- [ ] Reviewer: no critical/high findings for a doc-only change
- [ ] Quality: QUALITY_STATUS = PASS
- [ ] Both `state/reviewer.done` and `state/quality.done` created

### Stage 5: Tester

Launch:
```
task(agent_type="general-purpose", mode="background", name="tester-dryrun")
Prompt: run full test suite; verify no regressions from doc-only change
```

Gate check:
- [ ] All tests pass
- [ ] No new test failures
- [ ] `state/tester.done` created

### Stage 6: Integrator

Launch:
```
task(agent_type="general-purpose", mode="background", name="integrator-dryrun")
Prompt: verify doc-only change has no dataflow impact; confirm no contract changes
```

Gate check:
- [ ] No dataflow contracts affected
- [ ] Sign-off: PASS
- [ ] `state/integrator.done` created

### Stage 7: Orchestrator Decision

Read all outbox files and make final decision:
- [ ] All 6 stages green
- [ ] No policy violations
- [ ] No blocking findings
- [ ] `outbox/orchestrator.md` written with GO decision
- [ ] `state/orchestrator.done` created

---

## Mandatory Output Per Agent

Each agent output MUST include:

1. What was done (role-specific report)
2. Files changed (list with reasons)
3. Commands run and results (verbatim output for build/test)
4. Policy footer:
   ```
   Policy check: NO_PUSH=confirmed, INFRA_FREEZE=confirmed, SCOPE=confirmed
   Blocked items: <none or list>
   ```
5. Both `.md` and `.json` outbox files written

Quality agent additionally MUST include:
- `QUALITY_STATUS: PASS | CHANGES_REQUIRED`
- Check results for all 7 quality items from CLAUDE.md

---

## Stage Gates

| Gate | Pass Condition |
|------|----------------|
| Planner gate | Plan is complete, scoped, no source changes for doc-only run |
| Rubber-duck gate | No critical critique |
| Builder gate | Build PASS, test PASS, policy confirmed |
| Reviewer gate | No unresolved critical/high findings |
| Quality gate | QUALITY_STATUS = PASS |
| Tester gate | All tests pass, no regressions |
| Integrator gate | Dataflow unaffected, sign-off PASS |
| Orchestrator gate | All above passed → GO |

---

## Stop Conditions

Immediately mark dry run BLOCKED if:
1. Any infra change attempted (INFRA_FREEZE violation)
2. Any `git push` attempted (NO_PUSH violation)
3. Any source file modified in doc-only dry run (SCOPE violation)
4. Build fails at any stage
5. Any blocking finding remains unresolved at a gate

---

## M14 Anti-Pattern Check (Quality Gate)

During the dry run, verify the quality agent checks for:
- [ ] No `DateTime.UtcNow` introduced
- [ ] No `new JsonSerializerOptions()` per-call introduced
- [ ] No missing CancellationToken on new async methods
- [ ] No `public` on implementation types (should be `internal`)
- [ ] No `Result<T>` missing at new service boundaries
- [ ] No `string ==` for secret comparison
- [ ] No `object` lock fields (should be `Lock`)

---

## Dry Run Pass Criteria

Dry run is `PASS` only if ALL are true:
1. No policy violations at any stage
2. All 7 stages completed in order
3. All stage gates passed (including both parallel reviewer + quality)
4. Rubber-duck found no critical issues
5. M14 anti-pattern check passed
6. Final orchestrator outcome: GO

---

## Final Dry Run Report Template

```
Dry Run Result: <PASS|FAIL>
Scope: dryrun-claude-doc-only
Executor: Claude (GitHub Copilot CLI)

Stages completed:
  planner      ✅/❌
  rubber-duck  ✅/❌
  builder      ✅/❌
  reviewer     ✅/❌  (parallel)
  quality      ✅/❌  (parallel)
  tester       ✅/❌
  integrator   ✅/❌
  orchestrator ✅/❌

Blockers encountered: <none|list>
Policy violations: <none|list>
M14 anti-pattern violations: <none|list>
DoD status: <PASS|FAIL>
Final outcome: <GO|NO-GO>
Next action: <start real task | fix orchestration gaps>
```

---

## Comparison: Codex vs Claude Setup

| Aspect | Codex Setup | Claude Setup |
|--------|-------------|--------------|
| Contract file | `AGENTS.md` | `CLAUDE.md` |
| Orchestrator config | `agent-orchestrator.yaml` | `claude-orchestrator.yaml` |
| Agent launch | `ao spawn --agent codex` | `task(agent_type=..., mode="background")` |
| Gate mechanism | `while [ ! -f state/X.done ]; do sleep 2; done` | `read_agent(wait=true)` after notification |
| Observability | `tmux attach`, Window 6 monitor | `list_agents`, auto system_notifications |
| Parallelism | Separate tmux panes | Single response with multiple `task` calls |
| State tracking | `.done` files in `/tmp/` | `.done` files + SQL todos |
| Plan validation | None | `rubber-duck` agent stage |
| Code review | Generic reviewer role | Dedicated `code-review` agent type |
| Worker isolation | Git worktrees per agent | Single `feature/<scope>` branch; builder saves `builder.diff` for review |
| Skill reference | Not in prompts | Mandated read in every role's inbox |
| Anti-pattern rules | Not in prompts | Full M14 list in CLAUDE.md |
