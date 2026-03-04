#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<USAGE
Usage:
  scripts/agents/bootstrap-feature.sh --scope <scope-id> [options]

Options:
  --base <branch>         Base branch for new agent branches (default: main)
  --session <name>        tmux session name (default: multi-agent-<scope>)
  --with-tmux             Create/recreate tmux session for the feature
  --seed-prompts          Start codex in each agent window and submit prompt
  --force                 Recreate existing tmux session if present
  -h, --help              Show this help

Example:
  scripts/agents/bootstrap-feature.sh --scope m13-1-sanitize-webhook-header-logging --with-tmux --seed-prompts --force
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

find_worktree_for_branch() {
  local target_branch="$1"
  git worktree list --porcelain | awk -v b="refs/heads/${target_branch}" '
    /^worktree / { wt=$2 }
    /^branch / && $2==b { print wt; exit }
  '
}

resolve_worktree_for_branch() {
  local branch="$1" fallback="$2"
  local path
  path="$(find_worktree_for_branch "$branch" || true)"
  if [[ -n "$path" ]]; then
    echo "$path"
    return 0
  fi

  if [[ -d "$fallback/.git" || -f "$fallback/.git" ]]; then
    echo "$fallback"
    return 0
  fi

  return 1
}

SCOPE=""
BASE_BRANCH="main"
WITH_TMUX=0
SEED_PROMPTS=0
FORCE=0
SESSION=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --scope)
      SCOPE="${2:-}"
      shift 2
      ;;
    --base)
      BASE_BRANCH="${2:-}"
      shift 2
      ;;
    --session)
      SESSION="${2:-}"
      shift 2
      ;;
    --with-tmux)
      WITH_TMUX=1
      shift
      ;;
    --seed-prompts)
      SEED_PROMPTS=1
      shift
      ;;
    --force)
      FORCE=1
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

require_cmd git

ROOT="$(git rev-parse --show-toplevel)"
SCOPE="$(sanitize_scope "$SCOPE")"
if [[ -z "$SESSION" ]]; then
  SESSION="multi-agent-${SCOPE}"
fi

WORKTREE_ROOT="${ROOT}/.worktrees/${SCOPE}"
SYNC_DIR="/tmp/multi-agent-sync/${SCOPE}"
PROMPT_DIR="${SYNC_DIR}/prompts"
LOG_DIR="/tmp/multi-agent-logs/${SCOPE}"

AGENTS=(builder reviewer quality tester integrator)
WINDOWS=(orchestrator builder reviewer quality tester integrator monitor)

mkdir -p "$WORKTREE_ROOT" "$SYNC_DIR/inbox" "$SYNC_DIR/outbox" "$SYNC_DIR/state" "$PROMPT_DIR" "$LOG_DIR"
find "$SYNC_DIR/inbox" "$SYNC_DIR/outbox" "$SYNC_DIR/state" "$PROMPT_DIR" -type f -delete

if ! git show-ref --verify --quiet "refs/heads/${BASE_BRANCH}"; then
  echo "Base branch not found: ${BASE_BRANCH}" >&2
  exit 1
fi

for role in "${AGENTS[@]}"; do
  branch="agent/${role}/${SCOPE}"
  if ! git show-ref --verify --quiet "refs/heads/${branch}"; then
    git branch "$branch" "$BASE_BRANCH"
    echo "Created branch: $branch"
  else
    echo "Branch exists: $branch"
  fi
done

ensure_worktree() {
  local path="$1"
  local branch="$2"

  if [[ -d "$path/.git" || -f "$path/.git" ]]; then
    echo "Worktree exists: $path"
    return 0
  fi

  local existing
  existing="$(find_worktree_for_branch "$branch" || true)"
  if [[ -n "$existing" ]]; then
    echo "Branch already checked out in worktree: $branch -> $existing"
    return 0
  fi

  git worktree add "$path" "$branch"
  echo "Created worktree: $path ($branch)"
}

ensure_worktree "$WORKTREE_ROOT/orchestrator" "$BASE_BRANCH"
for role in "${AGENTS[@]}"; do
  ensure_worktree "$WORKTREE_ROOT/${role}" "agent/${role}/${SCOPE}"
done

ORCH_PATH="$(resolve_worktree_for_branch "$BASE_BRANCH" "$WORKTREE_ROOT/orchestrator" || true)"
BUILDER_PATH="$(resolve_worktree_for_branch "agent/builder/${SCOPE}" "$WORKTREE_ROOT/builder" || true)"
REVIEWER_PATH="$(resolve_worktree_for_branch "agent/reviewer/${SCOPE}" "$WORKTREE_ROOT/reviewer" || true)"
QUALITY_PATH="$(resolve_worktree_for_branch "agent/quality/${SCOPE}" "$WORKTREE_ROOT/quality" || true)"
TESTER_PATH="$(resolve_worktree_for_branch "agent/tester/${SCOPE}" "$WORKTREE_ROOT/tester" || true)"
INTEGRATOR_PATH="$(resolve_worktree_for_branch "agent/integrator/${SCOPE}" "$WORKTREE_ROOT/integrator" || true)"

for var_name in ORCH_PATH BUILDER_PATH REVIEWER_PATH QUALITY_PATH TESTER_PATH INTEGRATOR_PATH; do
  if [[ -z "${!var_name}" ]]; then
    echo "Unable to resolve worktree path for ${var_name}" >&2
    exit 1
  fi
done

cat > "$SYNC_DIR/README.txt" <<SYNC
Feature: ${SCOPE}

Contract:
- inbox/<agent>.md: task sent to agent
- outbox/<agent>.md: latest report from agent
- state/<agent>.done: phase complete marker

Execution order:
1) builder
2) reviewer + quality (parallel)
3) tester
4) integrator
5) orchestrator final decision

Hard constraints:
- NO_PUSH: no git push
- INFRA_FREEZE: do not modify terraform/bicep
SYNC

cat > "$SYNC_DIR/context.md" <<CTX
# Context for ${SCOPE}

## Objective
- Replace this with the feature objective.

## Scope
- Replace this with in-scope items.

## Out of Scope
- Replace this with excluded items.

## Constraints
- NO_PUSH
- INFRA_FREEZE (no terraform/bicep changes)

## Acceptance Criteria
- Replace this with testable acceptance criteria.
CTX

cat > "$SYNC_DIR/inbox/builder.md" <<'EOF_BUILDER'
Implement the feature for your scope.
Write report to outbox/builder.md:
- files changed
- commands + results
- tests run
- risks
Then create state/builder.done
EOF_BUILDER

cat > "$SYNC_DIR/inbox/reviewer.md" <<'EOF_REVIEWER'
Wait for state/builder.done.
Review builder output and code.
Write findings to outbox/reviewer.md with severity + file refs.
Then create state/reviewer.done
EOF_REVIEWER

cat > "$SYNC_DIR/inbox/quality.md" <<'EOF_QUALITY'
Wait for state/builder.done.
Run quality standard checks for this repository.
Write QUALITY_STATUS and actionable items to outbox/quality.md.
Then create state/quality.done
EOF_QUALITY

cat > "$SYNC_DIR/inbox/tester.md" <<'EOF_TESTER'
Wait for state/reviewer.done and state/quality.done.
Run test validation for the feature.
Write verdict to outbox/tester.md.
Then create state/tester.done
EOF_TESTER

cat > "$SYNC_DIR/inbox/integrator.md" <<'EOF_INTEGRATOR'
Wait for state/tester.done.
Validate integration/dataflow impact.
Write verdict to outbox/integrator.md.
Then create state/integrator.done
EOF_INTEGRATOR

cat > "$SYNC_DIR/inbox/orchestrator.md" <<'EOF_ORCH'
Coordinate all agents through inbox/outbox/state.
Enforce stage order and constraints.
Publish final PASS/CHANGES REQUIRED to outbox/orchestrator.md.
Then create state/orchestrator.done
EOF_ORCH

cat > "$PROMPT_DIR/orchestrator.txt" <<PROMPT_ORCH
You are the ORCHESTRATOR for scope '${SCOPE}'.
Read ${SYNC_DIR}/README.txt and ${SYNC_DIR}/inbox/orchestrator.md.
Coordinate all agents through ${SYNC_DIR}/inbox, ${SYNC_DIR}/outbox, ${SYNC_DIR}/state.
Constraints: no push, no terraform/bicep changes.
PROMPT_ORCH

for role in "${AGENTS[@]}"; do
  role_upper="$(echo "$role" | tr '[:lower:]' '[:upper:]')"
  cat > "$PROMPT_DIR/${role}.txt" <<PROMPT
You are the ${role_upper} agent for scope '${SCOPE}'.
Read ${SYNC_DIR}/README.txt and ${SYNC_DIR}/inbox/${role}.md.
Write your report to ${SYNC_DIR}/outbox/${role}.md.
When your phase is complete, create ${SYNC_DIR}/state/${role}.done.
Constraints: no push, no terraform/bicep changes.
PROMPT
done

if [[ "$WITH_TMUX" -eq 1 ]]; then
  require_cmd tmux

  if tmux has-session -t "$SESSION" 2>/dev/null; then
    if [[ "$FORCE" -eq 1 ]]; then
      tmux kill-session -t "$SESSION"
    else
      echo "tmux session already exists: $SESSION (use --force to recreate)" >&2
      exit 1
    fi
  fi

  tmux new-session -d -s "$SESSION" -n orchestrator -c "$ORCH_PATH"
  tmux new-window -t "$SESSION":1 -n builder -c "$BUILDER_PATH"
  tmux new-window -t "$SESSION":2 -n reviewer -c "$REVIEWER_PATH"
  tmux new-window -t "$SESSION":3 -n quality -c "$QUALITY_PATH"
  tmux new-window -t "$SESSION":4 -n tester -c "$TESTER_PATH"
  tmux new-window -t "$SESSION":5 -n integrator -c "$INTEGRATOR_PATH"
  tmux new-window -t "$SESSION":6 -n monitor -c "$ROOT"

  for idx in 0 1 2 3 4 5; do
    tmux send-keys -t "$SESSION:$idx" 'pwd; git branch --show-current' C-m
  done

  for pair in "0 orchestrator" "1 builder" "2 reviewer" "3 quality" "4 tester" "5 integrator"; do
    idx="${pair%% *}"
    role="${pair##* }"
    tmux pipe-pane -o -t "$SESSION:$idx" "cat >> ${LOG_DIR}/${role}.log"
  done

  if command -v watch >/dev/null 2>&1; then
    tmux send-keys -t "$SESSION:6" "watch -n 2 'echo === STATE ===; ls -1 ${SYNC_DIR}/state 2>/dev/null; echo; echo === OUTBOX ===; ls -1 ${SYNC_DIR}/outbox 2>/dev/null'" C-m
  else
    tmux send-keys -t "$SESSION:6" "while true; do clear; echo '=== STATE ==='; ls -1 ${SYNC_DIR}/state 2>/dev/null; echo; echo '=== OUTBOX ==='; ls -1 ${SYNC_DIR}/outbox 2>/dev/null; sleep 2; done" C-m
  fi

  if [[ "$SEED_PROMPTS" -eq 1 ]]; then
    for pair in "0 orchestrator" "1 builder" "2 reviewer" "3 quality" "4 tester" "5 integrator"; do
      idx="${pair%% *}"
      role="${pair##* }"
      tmux send-keys -t "$SESSION:$idx" C-c
      tmux send-keys -t "$SESSION:$idx" 'codex' C-m
      sleep 1
      tmux load-buffer -b "prompt_${role}" "${PROMPT_DIR}/${role}.txt"
      tmux paste-buffer -b "prompt_${role}" -t "$SESSION:$idx"
      tmux send-keys -t "$SESSION:$idx" C-m
    done
  fi
fi

cat <<DONE
Bootstrap complete.

Scope: ${SCOPE}
Base branch: ${BASE_BRANCH}
Worktrees: ${WORKTREE_ROOT}
Resolved paths:
- orchestrator: ${ORCH_PATH}
- builder: ${BUILDER_PATH}
- reviewer: ${REVIEWER_PATH}
- quality: ${QUALITY_PATH}
- tester: ${TESTER_PATH}
- integrator: ${INTEGRATOR_PATH}
Sync dir: ${SYNC_DIR}
Prompt dir: ${PROMPT_DIR}
Logs: ${LOG_DIR}
Session: ${SESSION}

Next:
1) tmux attach -t ${SESSION}
2) If you did not use --seed-prompts, start codex in each agent window and paste prompt from ${PROMPT_DIR}
DONE
