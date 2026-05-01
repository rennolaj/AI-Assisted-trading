#!/usr/bin/env bash
set -euo pipefail

# Claude-native multi-agent pipeline runner.
#
# This script prints the orchestration instructions for a Claude (Copilot CLI)
# session to execute one coordinated multi-agent pass using the task tool pipeline.
#
# Usage pattern:
#   1. Run this script to get the orchestration prompt
#   2. Paste the prompt into a Copilot CLI session (or trigger via ao if supported)
#   3. Claude acts as orchestrator, launching task agents in sequence

usage() {
  cat <<USAGE
Usage:
  scripts/agents/run-feature-once-claude.sh --scope <scope-id> [options]

Options:
  --project <id>       AO project id (default: AI-Assisted)
  --agent <name>       Agent override (default: claude)
  --followup-bugs      Run create-followup-bugs.sh after orchestrator completes
  --print-prompt       Print orchestration prompt to stdout (default: true)
  --no-send            Print prompt only, do not attempt ao send
  -h, --help           Show this help
USAGE
}

sanitize_scope() {
  echo "$1" | tr '[:upper:]' '[:lower:]' | sed -E 's#[^a-z0-9._-]+#-#g; s#^-+##; s#-+$##'
}

SCOPE=""
PROJECT_ID="AI-Assisted"
AGENT_NAME="claude"
FOLLOWUP_BUGS=0
NO_SEND=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --scope)         SCOPE="${2:-}"; shift 2 ;;
    --project)       PROJECT_ID="${2:-}"; shift 2 ;;
    --agent)         AGENT_NAME="${2:-}"; shift 2 ;;
    --followup-bugs) FOLLOWUP_BUGS=1; shift ;;
    --no-send)       NO_SEND=1; shift ;;
    -h|--help)       usage; exit 0 ;;
    *)               echo "Unknown argument: $1" >&2; usage; exit 1 ;;
  esac
done

[[ -z "$SCOPE" ]] && { echo "Error: --scope is required" >&2; usage; exit 1; }

ROOT="$(git rev-parse --show-toplevel)"
SCOPE="$(sanitize_scope "$SCOPE")"
SYNC_DIR="/tmp/multi-agent-sync/${SCOPE}"

for required in \
  "${SYNC_DIR}/context.md" \
  "${SYNC_DIR}/inbox/planner.md" \
  "${SYNC_DIR}/inbox/builder.md" \
  "${SYNC_DIR}/inbox/reviewer.md" \
  "${SYNC_DIR}/inbox/quality.md" \
  "${SYNC_DIR}/inbox/tester.md" \
  "${SYNC_DIR}/inbox/integrator.md"
do
  if [[ ! -f "$required" ]]; then
    echo "Missing required file: $required" >&2
    echo "Run bootstrap first: ./scripts/agents/bootstrap-feature-claude.sh --scope ${SCOPE}" >&2
    exit 1
  fi
done

FOLLOWUP_FLAG=""
if [[ "$FOLLOWUP_BUGS" -eq 1 ]]; then
  FOLLOWUP_FLAG="--followup-bugs"
fi

# ── Build orchestration prompt ────────────────────────────────────────────────
PROMPT_FILE="${SYNC_DIR}/prompts-claude/orchestrator-prompt.md"
mkdir -p "${SYNC_DIR}/prompts-claude"

cat > "$PROMPT_FILE" <<PROMPT
You are the ORCHESTRATOR for scope '${SCOPE}' in the AI-Assisted .NET 10 trading repository.

Read the contract and anti-pattern rules first:
  ${ROOT}/CLAUDE.md
  ${ROOT}/docs/csharp-dotnet10-skill.md

Feature context:
  ${SYNC_DIR}/context.md

Hard constraints:
  - NO_PUSH: do not run git push at any point
  - INFRA_FREEZE: do not modify Terraform or Bicep files
  - Keep all changes inside feature scope: '${SCOPE}'

Execute the following pipeline in order. Use the task tool to launch each agent.
Read each agent result with read_agent(wait:true) before proceeding to the next stage.

═══════════════════════════════════════════════════════
STAGE 1: PLANNER
═══════════════════════════════════════════════════════
Launch: task agent_type=general-purpose, mode=background, name="planner-${SCOPE}"

Prompt for planner:
  You are the PLANNER for scope '${SCOPE}'.
  Read: ${SYNC_DIR}/context.md, ${SYNC_DIR}/inbox/planner.md
  Read: ${ROOT}/CLAUDE.md (roles + anti-patterns), ${ROOT}/docs/csharp-dotnet10-skill.md
  Produce a deterministic implementation plan:
    1. Exact files to create or modify with reason
    2. Implementation steps in dependency order
    3. Risks and stop conditions
    4. Acceptance checks
    5. Flag any M14 anti-pattern risks
  Write: ${SYNC_DIR}/outbox/planner.md and ${SYNC_DIR}/outbox/planner.json
  Create: ${SYNC_DIR}/state/planner.done
  End with: Policy check: NO_PUSH=confirmed, INFRA_FREEZE=confirmed, SCOPE=confirmed

After planner completes: review the plan output. If critical risks are found, STOP and report to user.

═══════════════════════════════════════════════════════
STAGE 2: RUBBER-DUCK (plan validation)
═══════════════════════════════════════════════════════
Launch: task agent_type=rubber-duck, mode=sync, name="rubber-duck-${SCOPE}"

Give rubber-duck: the full planner output from ${SYNC_DIR}/outbox/planner.md
Ask it to critique:
  - Logic errors or missing steps
  - M14 anti-patterns that the plan might introduce
  - Risks not identified by planner
  - Whether acceptance checks are sufficient

Incorporate valid critique findings into context before launching builder.

═══════════════════════════════════════════════════════
STAGE 3: BUILDER
═══════════════════════════════════════════════════════
Launch: task agent_type=general-purpose, mode=background, name="builder-${SCOPE}"

Prompt for builder:
  You are the BUILDER for scope '${SCOPE}'.
  Read: ${SYNC_DIR}/context.md, ${SYNC_DIR}/inbox/builder.md
  Read: ${ROOT}/CLAUDE.md (M14 anti-patterns), ${ROOT}/docs/csharp-dotnet10-skill.md
  Read: ${SYNC_DIR}/outbox/planner.md
  Steps:
    1. Run: cd ${ROOT} && ./scripts/restore.sh
    2. Implement all changes using edit/create/bash tools
    3. Verify no M14 anti-patterns introduced (check CLAUDE.md anti-pattern list)
    4. Run: cd ${ROOT} && ./scripts/build.sh  ← MUST PASS
    5. Run: cd ${ROOT} && ./scripts/test.sh   ← MUST PASS
    6. Commit all changes to the feature branch (do NOT push):
         cd ${ROOT} && git add -A && git commit -m "feat(${SCOPE}): <short summary>"
    7. Save diff artifact for downstream review (reviewer/quality/integrator will use this):
         cd ${ROOT} && git diff main..HEAD > ${SYNC_DIR}/outbox/builder.diff
  Write: ${SYNC_DIR}/outbox/builder.md (include build + test output verbatim)
  Write: ${SYNC_DIR}/outbox/builder.json
  Create: ${SYNC_DIR}/state/builder.done
  End with: Policy check: NO_PUSH=confirmed, INFRA_FREEZE=confirmed, SCOPE=confirmed

═══════════════════════════════════════════════════════
STAGE 4: REVIEWER + QUALITY (parallel)
═══════════════════════════════════════════════════════
Launch BOTH in the same response (parallel):

4a. task agent_type=code-review, mode=background, name="reviewer-${SCOPE}"
  Prompt: Review all changes for scope '${SCOPE}'.
  Read: ${ROOT}/CLAUDE.md, ${ROOT}/docs/csharp-dotnet10-skill.md
  Read: ${SYNC_DIR}/outbox/builder.diff  ← primary review source (full unified diff)
  Read: ${SYNC_DIR}/outbox/builder.md for context on what was changed and why
  Do NOT checkout any branch. The diff file contains everything needed.
  Check every changed file against C# 14 / .NET 10 standards.
  Check CLAUDE.md M14 anti-patterns — flag any new instances introduced.
  Rate findings: critical / high / medium / low / info.
  Only block on critical or high.
  Write: ${SYNC_DIR}/outbox/reviewer.md, ${SYNC_DIR}/outbox/reviewer.json
  Create: ${SYNC_DIR}/state/reviewer.done

4b. task agent_type=general-purpose, mode=background, name="quality-${SCOPE}"
  Prompt: You are the QUALITY agent for scope '${SCOPE}'.
  Read: ${ROOT}/CLAUDE.md (quality checklist in role instructions)
  Read: ${SYNC_DIR}/outbox/builder.diff  ← primary quality source (full unified diff)
  Read: ${SYNC_DIR}/outbox/builder.md for context
  Do NOT checkout any branch. The diff file contains everything needed.
  Check all 7 quality items from CLAUDE.md quality role instructions.
  Output QUALITY_STATUS: PASS | CHANGES_REQUIRED
  Write: ${SYNC_DIR}/outbox/quality.md, ${SYNC_DIR}/outbox/quality.json
  Create: ${SYNC_DIR}/state/quality.done

Wait for BOTH to complete before proceeding.

═══════════════════════════════════════════════════════
STAGE 5: TESTER
═══════════════════════════════════════════════════════
Launch: task agent_type=general-purpose, mode=background, name="tester-${SCOPE}"

Prompt for tester:
  You are the TESTER for scope '${SCOPE}'.
  Read: ${SYNC_DIR}/inbox/tester.md, ${SYNC_DIR}/outbox/reviewer.md, ${SYNC_DIR}/outbox/quality.md
  The feature branch is already checked out — run tests directly against the committed code.
  Steps:
    1. Run: cd ${ROOT} && ./scripts/restore.sh && ./scripts/build.sh && ./scripts/test.sh
    2. Verify changed behavior has test coverage
    3. Verify no regressions
    4. Check CancellationToken paths are tested
    5. Check failure/error paths are tested (not just happy path)
  Do NOT make any commits.
  Write: ${SYNC_DIR}/outbox/tester.md (include FULL test output), ${SYNC_DIR}/outbox/tester.json
  Create: ${SYNC_DIR}/state/tester.done

═══════════════════════════════════════════════════════
STAGE 6: INTEGRATOR
═══════════════════════════════════════════════════════
Launch: task agent_type=general-purpose, mode=background, name="integrator-${SCOPE}"

Prompt for integrator:
  You are the INTEGRATOR for scope '${SCOPE}'.
  Read: ${SYNC_DIR}/context.md, ${SYNC_DIR}/inbox/integrator.md, ${SYNC_DIR}/outbox/tester.md
  Read: ${SYNC_DIR}/outbox/builder.diff  ← use for dataflow impact assessment
  Read: ${ROOT}/CLAUDE.md (integrator role instructions)
  Trace the full alert dataflow and verify changes respect all contracts.
  Check happy path and fail-closed path.
  Verify no new circular dependencies.
  Do NOT make any commits.
  Write: ${SYNC_DIR}/outbox/integrator.md, ${SYNC_DIR}/outbox/integrator.json
  Create: ${SYNC_DIR}/state/integrator.done

═══════════════════════════════════════════════════════
STAGE 7: ORCHESTRATOR DECISION
═══════════════════════════════════════════════════════
You (orchestrator) read all outbox files and make the final decision.

Read:
  ${SYNC_DIR}/outbox/planner.md
  ${SYNC_DIR}/outbox/builder.md
  ${SYNC_DIR}/outbox/reviewer.md
  ${SYNC_DIR}/outbox/quality.md
  ${SYNC_DIR}/outbox/tester.md
  ${SYNC_DIR}/outbox/integrator.md

Decision rules:
  PASS if: no blocking findings, all stages green, tests pass, policies confirmed
  CHANGES REQUIRED if: any critical/high unresolved finding

Write final decision to: ${SYNC_DIR}/outbox/orchestrator.md
Write machine report to: ${SYNC_DIR}/outbox/orchestrator.json
Create: ${SYNC_DIR}/state/orchestrator.done

$(if [[ "$FOLLOWUP_BUGS" -eq 1 ]]; then
echo "After decision, if blocking findings exist, run:"
echo "  ${ROOT}/scripts/agents/create-followup-bugs.sh --scope ${SCOPE}"
else
echo "Follow-up bug generation disabled for this run."
fi)

Report format:
\`\`\`
Scope: ${SCOPE}
Stages: planner ✅ | rubber-duck ✅ | builder ✅ | reviewer ✅ | quality ✅ | tester ✅ | integrator ✅
Blockers: none | <list>
Policy violations: none | <list>
DoD: PASS | FAIL
Final outcome: GO | NO-GO
Next action: <merge branch | fix list>
\`\`\`
PROMPT

echo "Orchestration prompt written to: ${PROMPT_FILE}"
echo

if [[ "$NO_SEND" -eq 0 ]] && command -v ao &>/dev/null; then
  echo "Attempting ao spawn with agent: ${AGENT_NAME}..."
  session_id="$(ao spawn "$PROJECT_ID" "orchestrator-${SCOPE}" --agent "$AGENT_NAME" 2>&1 | \
    awk -F= '/^SESSION=/{print $2}' | tail -1 || true)"
  if [[ -n "$session_id" ]]; then
    echo "Spawned AO session: ${session_id}"
    ao send "$session_id" --file "$PROMPT_FILE" --no-wait >/dev/null && \
      echo "Prompt sent to session ${session_id}" || \
      echo "ao send failed — paste prompt manually from: ${PROMPT_FILE}"
  else
    echo "ao spawn did not return a session id."
    echo "Paste the prompt manually from: ${PROMPT_FILE}"
  fi
else
  echo "Manual step: paste the contents of ${PROMPT_FILE} into your Copilot CLI session."
  echo
  echo "Or, if running from within Copilot CLI, the orchestration is defined"
  echo "in CLAUDE.md under 'Stage Order (Claude-Native)'."
fi
