#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<USAGE
Usage:
  scripts/agents/check-ao-pr-flow-readiness.sh [options]

Options:
  --project <id>       Project id in agent-orchestrator.yaml (default: AI-Assisted)
  --config <path>      AO config path (default: agent-orchestrator.yaml)
  --require-linear     Require Linear readiness via LINEAR_API_KEY or COMPOSIO_API_KEY
  -h, --help           Show help
USAGE
}

PROJECT_ID="AI-Assisted"
CONFIG_PATH="agent-orchestrator.yaml"
REQUIRE_LINEAR=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --project)
      PROJECT_ID="${2:-}"
      shift 2
      ;;
    --config)
      CONFIG_PATH="${2:-}"
      shift 2
      ;;
    --require-linear)
      REQUIRE_LINEAR=1
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

fail() {
  echo "[FAIL] $1" >&2
  exit 1
}

pass() {
  echo "[PASS] $1"
}

project_block() {
  awk -v project="$PROJECT_ID" '
    $0 ~ "^  " project ":$" { in_project=1; next }
    in_project && $0 ~ "^  [^[:space:]][^:]*:$" { in_project=0 }
    in_project { print }
  ' "$CONFIG_PATH"
}

if [[ ! -f "$CONFIG_PATH" ]]; then
  fail "Missing AO config: $CONFIG_PATH"
fi
pass "AO config exists: $CONFIG_PATH"

if ! command -v ao >/dev/null 2>&1; then
  fail "Missing AO CLI (ao). Install/start AO before running multi-agent flow."
fi
pass "AO CLI available"

if ! command -v gh >/dev/null 2>&1; then
  fail "Missing GitHub CLI (gh). Install it for PR flow readiness checks."
fi
pass "GitHub CLI available"

if ! grep -Eq '^defaults:' "$CONFIG_PATH"; then
  fail "Missing top-level defaults block in $CONFIG_PATH"
fi
if ! grep -Eq '^[[:space:]]+agent:[[:space:]]*[^[:space:]]+' "$CONFIG_PATH"; then
  fail "Missing defaults.agent in $CONFIG_PATH"
fi
pass "defaults.agent is configured"

if ! grep -Eq "^  ${PROJECT_ID}:$" "$CONFIG_PATH"; then
  fail "Project '${PROJECT_ID}' not found in $CONFIG_PATH"
fi
pass "Project '${PROJECT_ID}' exists in AO config"

PROJECT_BLOCK="$(project_block)"
if [[ -z "$PROJECT_BLOCK" ]]; then
  fail "Could not parse project block for '${PROJECT_ID}'"
fi

for required_key in 'repo:' 'path:' 'defaultBranch:' 'tracker:'; do
  if ! printf '%s\n' "$PROJECT_BLOCK" | grep -Eq "^[[:space:]]{4}${required_key}"; then
    fail "Project '${PROJECT_ID}' is missing '${required_key}'"
  fi
done

if ! printf '%s\n' "$PROJECT_BLOCK" | grep -Eq '^[[:space:]]{6}plugin:[[:space:]]*(github|linear)'; then
  fail "Project '${PROJECT_ID}' must set tracker.plugin to github or linear"
fi
pass "Required AO project keys are present (repo/path/defaultBranch/tracker.plugin)"

TRACKER_PLUGIN="$(printf '%s\n' "$PROJECT_BLOCK" | sed -n 's/^[[:space:]]\{6\}plugin:[[:space:]]*\([^[:space:]#][^[:space:]#]*\).*/\1/p' | head -1)"
if [[ -z "$TRACKER_PLUGIN" ]]; then
  fail "Could not parse tracker.plugin value for project '${PROJECT_ID}'"
fi
pass "tracker.plugin=${TRACKER_PLUGIN}"

if ! gh auth status >/dev/null 2>&1; then
  fail "gh auth status failed. Run: gh auth login"
fi
pass "GitHub auth is ready (gh auth status)"

if [[ "$REQUIRE_LINEAR" -eq 1 || "$TRACKER_PLUGIN" == "linear" ]]; then
  if [[ -n "${LINEAR_API_KEY:-}" || -n "${COMPOSIO_API_KEY:-}" ]]; then
    pass "Linear readiness env present (LINEAR_API_KEY or COMPOSIO_API_KEY)"
  else
    fail "Linear readiness missing. Set LINEAR_API_KEY or COMPOSIO_API_KEY"
  fi
fi

echo
cat <<SUMMARY
Readiness summary:
- Config: ${CONFIG_PATH}
- Project: ${PROJECT_ID}
- Tracker: ${TRACKER_PLUGIN}
- GitHub auth: ready
- Linear auth required: $([[ "$REQUIRE_LINEAR" -eq 1 || "$TRACKER_PLUGIN" == "linear" ]] && echo yes || echo no)
SUMMARY
