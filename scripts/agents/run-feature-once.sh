#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<USAGE
Usage:
  scripts/agents/run-feature-once.sh --scope <scope-id> [--session <tmux-session>] [--context-file <path>]

This dispatches one coordinated local multi-agent run in tmux windows using codex exec.
USAGE
}

SCOPE=""
SESSION=""
CONTEXT_FILE=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --scope) SCOPE="${2:-}"; shift 2 ;;
    --session) SESSION="${2:-}"; shift 2 ;;
    --context-file) CONTEXT_FILE="${2:-}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown arg: $1" >&2; usage; exit 1 ;;
  esac
done

if [[ -z "$SCOPE" ]]; then
  echo "--scope is required" >&2
  exit 1
fi

SCOPE="$(echo "$SCOPE" | tr '[:upper:]' '[:lower:]' | sed -E 's#[^a-z0-9._-]+#-#g; s#^-+##; s#-+$##')"
if [[ -z "$SESSION" ]]; then
  SESSION="multi-agent-${SCOPE}"
fi

TMUX_BIN="$(command -v tmux || true)"
if [[ -z "$TMUX_BIN" ]]; then
  TMUX_BIN="/opt/homebrew/bin/tmux"
fi

ROOT="$(git rev-parse --show-toplevel)"
SYNC_DIR="/tmp/multi-agent-sync/${SCOPE}"

if [[ -n "$CONTEXT_FILE" ]]; then
  if [[ ! -f "$CONTEXT_FILE" ]]; then
    echo "context file not found: $CONTEXT_FILE" >&2
    exit 1
  fi
  cp "$CONTEXT_FILE" "$SYNC_DIR/context.md"
fi

if [[ ! -f "$SYNC_DIR/context.md" ]]; then
  echo "missing context file: $SYNC_DIR/context.md (run bootstrap first or pass --context-file)" >&2
  exit 1
fi

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

ORCH_PATH="$(resolve_worktree_for_branch "main" "${ROOT}/.worktrees/${SCOPE}/orchestrator" || true)"
PLANNER_PATH="$(resolve_worktree_for_branch "agent/planner/${SCOPE}" "${ROOT}/.worktrees/${SCOPE}/planner" || true)"
BUILDER_PATH="$(resolve_worktree_for_branch "agent/builder/${SCOPE}" "${ROOT}/.worktrees/${SCOPE}/builder" || true)"
REVIEWER_PATH="$(resolve_worktree_for_branch "agent/reviewer/${SCOPE}" "${ROOT}/.worktrees/${SCOPE}/reviewer" || true)"
QUALITY_PATH="$(resolve_worktree_for_branch "agent/quality/${SCOPE}" "${ROOT}/.worktrees/${SCOPE}/quality" || true)"
TESTER_PATH="$(resolve_worktree_for_branch "agent/tester/${SCOPE}" "${ROOT}/.worktrees/${SCOPE}/tester" || true)"
INTEGRATOR_PATH="$(resolve_worktree_for_branch "agent/integrator/${SCOPE}" "${ROOT}/.worktrees/${SCOPE}/integrator" || true)"

for var_name in ORCH_PATH PLANNER_PATH BUILDER_PATH REVIEWER_PATH QUALITY_PATH TESTER_PATH INTEGRATOR_PATH; do
  if [[ -z "${!var_name}" ]]; then
    echo "Unable to resolve worktree path for ${var_name}. Run bootstrap first for scope '${SCOPE}'." >&2
    exit 1
  fi
done

if ! "$TMUX_BIN" has-session -t "$SESSION" 2>/dev/null; then
  echo "tmux session not found: $SESSION" >&2
  exit 1
fi

run_in_pane() {
  local pane="$1" path="$2" cmd="$3"
  "$TMUX_BIN" send-keys -t "$SESSION:$pane" C-c
  "$TMUX_BIN" send-keys -t "$SESSION:$pane" C-c
  "$TMUX_BIN" send-keys -t "$SESSION:$pane" C-c
  "$TMUX_BIN" send-keys -t "$SESSION:$pane" "cd $path || exit 1" C-m
  "$TMUX_BIN" send-keys -t "$SESSION:$pane" "$cmd" C-m
}

run_in_pane 1 "$PLANNER_PATH" "codex exec \"Read ${SYNC_DIR}/context.md and ${SYNC_DIR}/inbox/planner.md. Do planner task and write report to ${SYNC_DIR}/outbox/planner.md. When done create ${SYNC_DIR}/state/planner.done. Do not push. Do not modify terraform/bicep.\""

run_in_pane 2 "$BUILDER_PATH" "while [ ! -f ${SYNC_DIR}/state/planner.done ]; do sleep 2; done; codex exec \"Read ${SYNC_DIR}/context.md, ${SYNC_DIR}/inbox/builder.md and ${SYNC_DIR}/outbox/planner.md. Do the builder task and write report to ${SYNC_DIR}/outbox/builder.md. When done create ${SYNC_DIR}/state/builder.done. Do not push. Do not modify terraform/bicep.\""

run_in_pane 3 "$REVIEWER_PATH" "while [ ! -f ${SYNC_DIR}/state/builder.done ]; do sleep 2; done; codex exec \"Read ${SYNC_DIR}/context.md, ${SYNC_DIR}/inbox/reviewer.md and ${SYNC_DIR}/outbox/builder.md. Do reviewer task and write report to ${SYNC_DIR}/outbox/reviewer.md. When done create ${SYNC_DIR}/state/reviewer.done. Do not push. Do not modify terraform/bicep.\""

run_in_pane 4 "$QUALITY_PATH" "while [ ! -f ${SYNC_DIR}/state/builder.done ]; do sleep 2; done; codex exec \"Read ${SYNC_DIR}/context.md, ${SYNC_DIR}/inbox/quality.md and ${SYNC_DIR}/outbox/builder.md. Run quality task and write report to ${SYNC_DIR}/outbox/quality.md. When done create ${SYNC_DIR}/state/quality.done. Do not push. Do not modify terraform/bicep.\""

run_in_pane 5 "$TESTER_PATH" "while [ ! -f ${SYNC_DIR}/state/reviewer.done ] || [ ! -f ${SYNC_DIR}/state/quality.done ]; do sleep 2; done; codex exec \"Read ${SYNC_DIR}/context.md, ${SYNC_DIR}/inbox/tester.md, ${SYNC_DIR}/outbox/reviewer.md, ${SYNC_DIR}/outbox/quality.md. Run tester task and write report to ${SYNC_DIR}/outbox/tester.md. When done create ${SYNC_DIR}/state/tester.done. Do not push. Do not modify terraform/bicep.\""

run_in_pane 6 "$INTEGRATOR_PATH" "while [ ! -f ${SYNC_DIR}/state/tester.done ]; do sleep 2; done; codex exec \"Read ${SYNC_DIR}/context.md, ${SYNC_DIR}/inbox/integrator.md, ${SYNC_DIR}/outbox/tester.md. Run integrator task and write report to ${SYNC_DIR}/outbox/integrator.md. When done create ${SYNC_DIR}/state/integrator.done. Do not push. Do not modify terraform/bicep.\""

run_in_pane 0 "$ORCH_PATH" "while [ ! -f ${SYNC_DIR}/state/integrator.done ]; do sleep 2; done; codex exec \"Read ${SYNC_DIR}/README.txt, ${SYNC_DIR}/context.md, and all files under ${SYNC_DIR}/outbox. Produce final orchestrator decision in ${SYNC_DIR}/outbox/orchestrator.md and create ${SYNC_DIR}/state/orchestrator.done.\"; ${ROOT}/scripts/agents/create-followup-bugs.sh --scope ${SCOPE}"

if command -v watch >/dev/null 2>&1; then
  "$TMUX_BIN" send-keys -t "$SESSION:7" "watch -n 2 'echo === STATE ===; ls -1 ${SYNC_DIR}/state 2>/dev/null; echo; echo === OUTBOX ===; ls -1 ${SYNC_DIR}/outbox 2>/dev/null'" C-m
else
  "$TMUX_BIN" send-keys -t "$SESSION:7" "while true; do clear; echo '=== STATE ==='; ls -1 ${SYNC_DIR}/state 2>/dev/null; echo; echo '=== OUTBOX ==='; ls -1 ${SYNC_DIR}/outbox 2>/dev/null; sleep 2; done" C-m
fi

echo "Dispatched coordinated run for scope: ${SCOPE}"
echo "Session: ${SESSION}"
echo "Sync dir: ${SYNC_DIR}"
