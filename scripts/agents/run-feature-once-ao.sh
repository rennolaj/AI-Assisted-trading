#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<USAGE
Usage:
  scripts/agents/run-feature-once-ao.sh --scope <scope-id> [options]

Options:
  --project <id>      AO project id from agent-orchestrator.yaml (default: AI-Assisted)
  --agent <name>      Agent plugin override for ao spawn (default: codex)
  --followup-bugs     Run create-followup-bugs.sh after orchestrator completion
  --no-send           Only spawn sessions, do not send role prompts
  -h, --help          Show this help

This script creates one AO session per role:
  planner, builder, reviewer, quality, tester, integrator, orchestrator

It then sends each role a gated instruction payload that uses the existing
/tmp/multi-agent-sync/<scope> inbox/outbox/state contract.

Important:
  This coordinated role workflow is report-driven and intentionally non-pushing.
  Do branch/push/PR work in AO tracker-assigned issue sessions, not here.
USAGE
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "Missing required command: $1" >&2
    exit 1
  }
}

sanitize_scope() {
  echo "$1" | tr '[:upper:]' '[:lower:]' | sed -E 's#[^a-z0-9._-]+#-#g; s#^-+##; s#-+$##'
}

SCOPE=""
PROJECT_ID="AI-Assisted"
AGENT_NAME="codex"
FOLLOWUP_BUGS=0
NO_SEND=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --scope)
      SCOPE="${2:-}"
      shift 2
      ;;
    --project)
      PROJECT_ID="${2:-}"
      shift 2
      ;;
    --agent)
      AGENT_NAME="${2:-}"
      shift 2
      ;;
    --followup-bugs)
      FOLLOWUP_BUGS=1
      shift
      ;;
    --no-send)
      NO_SEND=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      exit 1
      ;;
  esac
done

if [[ -z "$SCOPE" ]]; then
  echo "Error: --scope is required" >&2
  usage
  exit 1
fi

require_cmd ao
require_cmd git

ROOT="$(git rev-parse --show-toplevel)"
PROJECT_BASENAME="$(basename "$ROOT")"
SCOPE="$(sanitize_scope "$SCOPE")"
SYNC_DIR="/tmp/multi-agent-sync/${SCOPE}"
PROMPT_DIR="${SYNC_DIR}/prompts-ao"
SESSION_MAP="${SYNC_DIR}/state/ao-sessions.map"
ATTACH_MAP="${SYNC_DIR}/state/ao-attach.map"

for required in \
  "${SYNC_DIR}/context.md" \
  "${SYNC_DIR}/inbox/planner.md" \
  "${SYNC_DIR}/inbox/builder.md" \
  "${SYNC_DIR}/inbox/reviewer.md" \
  "${SYNC_DIR}/inbox/quality.md" \
  "${SYNC_DIR}/inbox/tester.md" \
  "${SYNC_DIR}/inbox/integrator.md" \
  "${SYNC_DIR}/inbox/orchestrator.md"
do
  if [[ ! -f "$required" ]]; then
    echo "Missing required file: $required" >&2
    echo "Run scripts/agents/bootstrap-feature.sh --scope ${SCOPE} first." >&2
    exit 1
  fi
done

mkdir -p "$PROMPT_DIR" "${SYNC_DIR}/state"
: > "$SESSION_MAP"
: > "$ATTACH_MAP"

resolve_tmux_name() {
  local session_id="$1"
  local meta
  for meta in "$HOME"/.agent-orchestrator/*-"$PROJECT_BASENAME"/sessions/"$session_id"; do
    if [[ -f "$meta" ]]; then
      local tmux_name
      tmux_name="$(sed -n 's/^tmuxName=//p' "$meta" | head -1)"
      if [[ -n "$tmux_name" ]]; then
        echo "$tmux_name"
        return 0
      fi
    fi
  done
  echo "$session_id"
}

gate_block_for_role() {
  local role="$1"
  case "$role" in
    planner)
      cat <<'GATE'
No gate. Start immediately.
GATE
      ;;
    builder)
      cat <<GATE
Before starting work, run this gate in your shell:
while [ ! -f "${SYNC_DIR}/state/planner.done" ]; do sleep 2; done
GATE
      ;;
    reviewer|quality)
      cat <<GATE
Before starting work, run this gate in your shell:
while [ ! -f "${SYNC_DIR}/state/builder.done" ]; do sleep 2; done
GATE
      ;;
    tester)
      cat <<GATE
Before starting work, run this gate in your shell:
while [ ! -f "${SYNC_DIR}/state/reviewer.done" ] || [ ! -f "${SYNC_DIR}/state/quality.done" ]; do sleep 2; done
GATE
      ;;
    integrator)
      cat <<GATE
Before starting work, run this gate in your shell:
while [ ! -f "${SYNC_DIR}/state/tester.done" ]; do sleep 2; done
GATE
      ;;
    orchestrator)
      cat <<GATE
Before final decision, run this gate in your shell:
while [ ! -f "${SYNC_DIR}/state/integrator.done" ]; do sleep 2; done
GATE
      ;;
    *)
      echo "Unknown role: $role" >&2
      exit 1
      ;;
  esac
}

action_block_for_role() {
  local role="$1"
  case "$role" in
    planner)
      cat <<'ACT'
1) Checkout role branch:
   git checkout -B "agent/planner/<scope>"
2) Read:
   - context.md
   - inbox/planner.md
3) Produce deterministic implementation plan and acceptance checks.
4) Write report to outbox/planner.md.
5) Also write machine-readable report to outbox/planner.json.
6) Mark done by creating state/planner.done.
ACT
      ;;
    builder)
      cat <<'ACT'
1) Checkout role branch:
   git checkout -B "agent/builder/<scope>"
2) Read:
   - context.md
   - inbox/builder.md
   - outbox/planner.md
3) Implement feature scope according to planner output.
4) Validate with .NET scripts:
   ./scripts/restore.sh
   ./scripts/build.sh
   ./scripts/test.sh
5) Write report to outbox/builder.md with:
   - Summary
   - Files changed
   - Commands + results
   - Risks / follow-ups
6) Also write machine-readable report to outbox/builder.json.
7) Mark done by creating state/builder.done
ACT
      ;;
    reviewer)
      cat <<'ACT'
1) Checkout role branch:
   git checkout -B "agent/reviewer/<scope>"
2) Read:
   - context.md
   - inbox/reviewer.md
   - outbox/builder.md
3) Perform code review with severity and file references.
4) Write report to outbox/reviewer.md.
5) Also write machine-readable report to outbox/reviewer.json.
6) Mark done by creating state/reviewer.done.
ACT
      ;;
    quality)
      cat <<'ACT'
1) Checkout role branch:
   git checkout -B "agent/quality/<scope>"
2) Read:
   - context.md
   - inbox/quality.md
   - outbox/builder.md
3) Run quality checks relevant to .NET 10 codebase.
4) Write report to outbox/quality.md including QUALITY_STATUS and actionable items.
5) Also write machine-readable report to outbox/quality.json.
6) Mark done by creating state/quality.done.
ACT
      ;;
    tester)
      cat <<'ACT'
1) Checkout role branch:
   git checkout -B "agent/tester/<scope>"
2) Read:
   - context.md
   - inbox/tester.md
   - outbox/reviewer.md
   - outbox/quality.md
3) Execute validation:
   ./scripts/restore.sh
   ./scripts/build.sh
   ./scripts/test.sh
4) Write report to outbox/tester.md with test verdict.
5) Also write machine-readable report to outbox/tester.json.
6) Mark done by creating state/tester.done.
ACT
      ;;
    integrator)
      cat <<'ACT'
1) Checkout role branch:
   git checkout -B "agent/integrator/<scope>"
2) Read:
   - context.md
   - inbox/integrator.md
   - outbox/tester.md
3) Validate integration/dataflow and cross-component impacts.
4) Write report to outbox/integrator.md.
5) Also write machine-readable report to outbox/integrator.json.
6) Mark done by creating state/integrator.done.
ACT
      ;;
    orchestrator)
      if [[ "$FOLLOWUP_BUGS" -eq 1 ]]; then
        cat <<ACT
1) Stay on main (orchestrator worktree) and coordinate by reading all outbox files.
2) Wait for integrator gate, then write final decision to:
   ${SYNC_DIR}/outbox/orchestrator.md
3) Also write machine-readable decision to:
   ${SYNC_DIR}/outbox/orchestrator.json
4) Mark done by creating:
   ${SYNC_DIR}/state/orchestrator.done
5) If blocking findings exist from reviewer/quality/integrator, run:
   ${ROOT}/scripts/agents/create-followup-bugs.sh --scope ${SCOPE}
ACT
      else
        cat <<ACT
1) Stay on main (orchestrator worktree) and coordinate by reading all outbox files.
2) Wait for integrator gate, then write final decision to:
   ${SYNC_DIR}/outbox/orchestrator.md
3) Also write machine-readable decision to:
   ${SYNC_DIR}/outbox/orchestrator.json
4) Mark done by creating:
   ${SYNC_DIR}/state/orchestrator.done
5) Do not run follow-up bug generation (flag disabled).
ACT
      fi
      ;;
    *)
      echo "Unknown role: $role" >&2
      exit 1
      ;;
  esac
}

build_prompt_file() {
  local role="$1"
  local file="$PROMPT_DIR/${role}.md"
  local role_upper
  role_upper="$(echo "$role" | tr '[:lower:]' '[:upper:]')"

  cat > "$file" <<PROMPT
You are the ${role_upper} agent for scope '${SCOPE}' in a .NET 10 repository.

Hard constraints:
- NO_PUSH (sync-bus run): do not run git push from this coordinated role session.
- INFRA_FREEZE: do not modify Terraform/Bicep files.
- Keep changes in feature scope.

Contract paths:
- Context: ${SYNC_DIR}/context.md
- Inbox: ${SYNC_DIR}/inbox/${role}.md
- Outbox target: ${SYNC_DIR}/outbox/${role}.md
- Outbox JSON target: ${SYNC_DIR}/outbox/${role}.json
- Done marker: ${SYNC_DIR}/state/${role}.done

$(gate_block_for_role "$role")

Execution:
$(action_block_for_role "$role" | sed 's/^/  /')

Important:
- Use explicit shell commands; do not only describe intent.
- If a command fails, include failure + fix attempt in your outbox report.
- Write both outputs:
  1) Human summary markdown (.md)
  2) Machine-readable JSON (.json) with this schema:
  {
    "role": "${role}",
    "scope": "${SCOPE}",
    "status": "success|changes_required|blocked|failed",
    "summary": "short summary",
    "blocking": true,
    "findings": [
      {
        "severity": "critical|high|medium|low|info",
        "title": "issue title",
        "file": "path/optional",
        "line": 0,
        "action": "required fix"
      }
    ],
    "commands": [
      {"cmd": "command", "result": "pass|fail", "details": "optional"}
    ],
    "artifacts": {
      "outbox_md": "${SYNC_DIR}/outbox/${role}.md"
    }
  }
PROMPT

  echo "$file"
}

spawn_role_session() {
  local role="$1"
  local issue_id="${role}-${SCOPE}"
  local output

  output="$(ao spawn "$PROJECT_ID" "$issue_id" --agent "$AGENT_NAME" 2>&1)" || {
    echo "$output" >&2
    return 1
  }

  echo "$output" >&2

  local session_id
  session_id="$(echo "$output" | awk -F= '/^SESSION=/{print $2}' | tail -1)"
  if [[ -z "$session_id" ]]; then
    echo "Failed to parse SESSION id for role ${role}" >&2
    return 1
  fi

  echo "$role=$session_id" >> "$SESSION_MAP"
  local tmux_name
  tmux_name="$(resolve_tmux_name "$session_id")"
  echo "$role=$tmux_name" >> "$ATTACH_MAP"

  printf '%s' "$session_id"
}

ROLES=(planner builder reviewer quality tester integrator orchestrator)

echo "Starting AO multi-agent run"
echo "Scope: ${SCOPE}"
echo "Project: ${PROJECT_ID}"
echo "Agent: ${AGENT_NAME}"
echo "Follow-up bugs: ${FOLLOWUP_BUGS}"
echo "Sync dir: ${SYNC_DIR}"
echo

for role in "${ROLES[@]}"; do
  echo "==> Spawning ${role}"
  session_id="$(spawn_role_session "$role")"

  if [[ "$NO_SEND" -eq 0 ]]; then
    prompt_file="$(build_prompt_file "$role")"
    echo "==> Sending prompt to ${role} (${session_id})"
    ao send "$session_id" --file "$prompt_file" --no-wait >/dev/null
  fi

done

echo
echo "AO sessions created:"
cat "$SESSION_MAP"
echo
echo "tmux attach targets:"
while IFS='=' read -r role tmux_name; do
  printf '  %-12s tmux attach -t %s\n' "$role" "$tmux_name"
done < "$ATTACH_MAP"
echo
echo "Tip: ao status"
