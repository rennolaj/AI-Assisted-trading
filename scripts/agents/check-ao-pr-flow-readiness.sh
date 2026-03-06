#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  scripts/agents/check-ao-pr-flow-readiness.sh [options]

Options:
  --project <id>      AO project id in agent-orchestrator.yaml (default: AI-Assisted)
  --strict            Fail if git worktree has uncommitted changes
  --smoke             Run additional non-destructive PR smoke checks
  -h, --help          Show this help

Checks:
  - Required commands: ao, gh, git
  - agent-orchestrator.yaml exists and includes required project fields
  - GitHub auth is valid (gh auth status)
  - Linear path auth exists: LINEAR_API_KEY or COMPOSIO_API_KEY

Exit code:
  0 when all enabled checks pass, non-zero otherwise.
USAGE
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "FAIL Missing required command: $1"
    return 1
  }
}

PROJECT_ID="AI-Assisted"
STRICT=0
SMOKE=0
FAILURES=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --project)
      PROJECT_ID="${2:-}"
      shift 2
      ;;
    --strict)
      STRICT=1
      shift
      ;;
    --smoke)
      SMOKE=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1"
      usage
      exit 1
      ;;
  esac
done

ROOT="$(git rev-parse --show-toplevel)"
AO_CONFIG="${ROOT}/agent-orchestrator.yaml"

echo "AO PR-flow readiness preflight"
echo "Project: ${PROJECT_ID}"
echo "Config:  ${AO_CONFIG}"
echo

for cmd in ao gh git; do
  if require_cmd "$cmd"; then
    echo "PASS Command available: ${cmd}"
  else
    FAILURES=$((FAILURES + 1))
  fi
done

if [[ ! -f "$AO_CONFIG" ]]; then
  echo "FAIL Missing config: ${AO_CONFIG}"
  FAILURES=$((FAILURES + 1))
else
  echo "PASS Found config: ${AO_CONFIG}"

  if rg -n "^projects:" "$AO_CONFIG" >/dev/null 2>&1; then
    echo "PASS AO config contains projects block"
  else
    echo "FAIL AO config missing projects block"
    FAILURES=$((FAILURES + 1))
  fi

  if rg -n "^[[:space:]]{2}${PROJECT_ID}:" "$AO_CONFIG" >/dev/null 2>&1; then
    echo "PASS AO project exists: ${PROJECT_ID}"
  else
    echo "FAIL AO project not found: ${PROJECT_ID}"
    FAILURES=$((FAILURES + 1))
  fi

  for field in repo path defaultBranch; do
    if rg -n "^[[:space:]]+${field}:" "$AO_CONFIG" >/dev/null 2>&1; then
      echo "PASS Required project field present: ${field}"
    else
      echo "FAIL Missing required project field: ${field}"
      FAILURES=$((FAILURES + 1))
    fi
  done

  if rg -n "^[[:space:]]+tracker:" "$AO_CONFIG" >/dev/null 2>&1; then
    echo "PASS Tracker block present"
  else
    echo "FAIL Missing tracker block"
    FAILURES=$((FAILURES + 1))
  fi

  if rg -n "^[[:space:]]+plugin:[[:space:]]*(github|linear|composio)" "$AO_CONFIG" >/dev/null 2>&1; then
    echo "PASS Supported tracker plugin found (github|linear|composio)"
  else
    echo "FAIL Tracker plugin not found or unsupported"
    FAILURES=$((FAILURES + 1))
  fi
fi

if gh auth status >/dev/null 2>&1; then
  echo "PASS GitHub auth is valid (gh auth status)"
else
  echo "FAIL GitHub auth failed. Run: gh auth login"
  FAILURES=$((FAILURES + 1))
fi

if [[ -n "${LINEAR_API_KEY:-}" || -n "${COMPOSIO_API_KEY:-}" ]]; then
  echo "PASS Linear path auth present via LINEAR_API_KEY or COMPOSIO_API_KEY"
else
  echo "FAIL Missing LINEAR_API_KEY/COMPOSIO_API_KEY for Linear tracker path"
  FAILURES=$((FAILURES + 1))
fi

if [[ "$STRICT" -eq 1 ]]; then
  if [[ -n "$(git status --porcelain)" ]]; then
    echo "FAIL Strict mode: worktree is not clean"
    FAILURES=$((FAILURES + 1))
  else
    echo "PASS Strict mode: worktree clean"
  fi
fi

if [[ "$SMOKE" -eq 1 ]]; then
  echo
  echo "Running smoke checks..."

  if gh repo view >/dev/null 2>&1; then
    echo "PASS gh repo view"
  else
    echo "FAIL gh repo view failed"
    FAILURES=$((FAILURES + 1))
  fi

  if gh pr status >/dev/null 2>&1; then
    echo "PASS gh pr status"
  else
    echo "FAIL gh pr status failed"
    FAILURES=$((FAILURES + 1))
  fi

  if ao status >/dev/null 2>&1; then
    echo "PASS ao status"
  else
    echo "FAIL ao status failed"
    FAILURES=$((FAILURES + 1))
  fi
fi

echo
if [[ "$FAILURES" -gt 0 ]]; then
  echo "Readiness check failed with ${FAILURES} issue(s)."
  exit 1
fi

echo "Readiness check passed."
