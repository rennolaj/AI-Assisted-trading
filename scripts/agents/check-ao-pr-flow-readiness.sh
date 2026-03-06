#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<USAGE
Usage:
  scripts/agents/check-ao-pr-flow-readiness.sh [options]

Options:
  --project <id>           AO project id from agent-orchestrator.yaml (default: AI-Assisted)
  --config <path>          AO config file path (default: agent-orchestrator.yaml)
  --allow-missing-linear   Do not fail when LINEAR_API_KEY/COMPOSIO_API_KEY are missing
  -h, --help               Show this help

Purpose:
  Fail-fast readiness validation for AO + GitHub PR + Linear tracker workflow.
USAGE
}

PROJECT_ID="AI-Assisted"
CONFIG_FILE="agent-orchestrator.yaml"
ALLOW_MISSING_LINEAR=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --project)
      PROJECT_ID="${2:-}"
      shift 2
      ;;
    --config)
      CONFIG_FILE="${2:-}"
      shift 2
      ;;
    --allow-missing-linear)
      ALLOW_MISSING_LINEAR=1
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

require_cmd() {
  command -v "$1" >/dev/null 2>&1
}

get_defaults_agent() {
  awk '
    /^defaults:[[:space:]]*$/ { in_defaults=1; next }
    in_defaults && /^[^[:space:]]/ { in_defaults=0 }
    in_defaults && /^  agent:[[:space:]]*/ {
      line=$0
      sub(/^  agent:[[:space:]]*/, "", line)
      print line
      exit
    }
  ' "$CONFIG_FILE"
}

project_field_exists() {
  local field="$1"
  awk -v project="$PROJECT_ID" -v field="$field" '
    /^projects:[[:space:]]*$/ { in_projects=1; next }
    in_projects && /^[^[:space:]]/ { in_projects=0; in_project=0 }
    in_projects && /^  [^[:space:]][^:]*:[[:space:]]*$/ {
      key=$0
      sub(/^  /, "", key)
      sub(/:.*/, "", key)
      if (key == project) {
        in_project=1
        next
      }
      if (in_project) {
        in_project=0
      }
    }
    in_project && $0 ~ ("^    " field ":[[:space:]]*[^[:space:]]+") { found=1 }
    END { exit(found ? 0 : 1) }
  ' "$CONFIG_FILE"
}

get_tracker_plugin() {
  awk -v project="$PROJECT_ID" '
    /^projects:[[:space:]]*$/ { in_projects=1; next }
    in_projects && /^[^[:space:]]/ { in_projects=0; in_project=0; in_tracker=0 }
    in_projects && /^  [^[:space:]][^:]*:[[:space:]]*$/ {
      key=$0
      sub(/^  /, "", key)
      sub(/:.*/, "", key)
      if (key == project) {
        in_project=1
        in_tracker=0
        next
      }
      if (in_project) {
        in_project=0
        in_tracker=0
      }
    }
    in_project && /^    tracker:[[:space:]]*$/ { in_tracker=1; next }
    in_project && /^    [^[:space:]][^:]*:[[:space:]]*$/ && $0 !~ /^    tracker:[[:space:]]*$/ {
      in_tracker=0
    }
    in_project && in_tracker && /^      plugin:[[:space:]]*/ {
      line=$0
      sub(/^      plugin:[[:space:]]*/, "", line)
      print line
      exit
    }
  ' "$CONFIG_FILE"
}

line() {
  local status="$1"
  local message="$2"
  printf '%-6s %s\n' "$status" "$message"
}

failures=0
warnings=0

echo "AO PR flow readiness check"
echo "Project: ${PROJECT_ID}"
echo "Config: ${CONFIG_FILE}"
echo
echo "Checklist"

if require_cmd ao; then
  line "PASS" "Command available: ao"
else
  line "FAIL" "Missing command: ao"
  failures=$((failures + 1))
fi

if require_cmd gh; then
  line "PASS" "Command available: gh"
else
  line "FAIL" "Missing command: gh"
  failures=$((failures + 1))
fi

if [[ -f "$CONFIG_FILE" ]]; then
  line "PASS" "Config file present: ${CONFIG_FILE}"
else
  line "FAIL" "Missing config file: ${CONFIG_FILE}"
  failures=$((failures + 1))
fi

if [[ -f "$CONFIG_FILE" ]]; then
  default_agent="$(get_defaults_agent || true)"
  if [[ -n "$default_agent" ]]; then
    line "PASS" "YAML field present: defaults.agent=${default_agent}"
    if [[ "$default_agent" != "codex" ]]; then
      line "FAIL" "defaults.agent must be 'codex' for this repo"
      failures=$((failures + 1))
    fi
  else
    line "FAIL" "Missing YAML field: defaults.agent"
    failures=$((failures + 1))
  fi

  if awk -v project="$PROJECT_ID" '
    /^projects:[[:space:]]*$/ { in_projects=1; next }
    in_projects && /^[^[:space:]]/ { in_projects=0 }
    in_projects && /^  [^[:space:]][^:]*:[[:space:]]*$/ {
      key=$0
      sub(/^  /, "", key)
      sub(/:.*/, "", key)
      if (key == project) {
        found=1
        exit
      }
    }
    END { exit(found ? 0 : 1) }
  ' "$CONFIG_FILE"; then
    line "PASS" "YAML field present: projects.${PROJECT_ID}"
  else
    line "FAIL" "Missing YAML field: projects.${PROJECT_ID}"
    failures=$((failures + 1))
  fi

  for field in repo path defaultBranch; do
    if project_field_exists "$field"; then
      line "PASS" "YAML field present: projects.${PROJECT_ID}.${field}"
    else
      line "FAIL" "Missing YAML field: projects.${PROJECT_ID}.${field}"
      failures=$((failures + 1))
    fi
  done

  tracker_plugin="$(get_tracker_plugin || true)"
  if [[ -n "$tracker_plugin" ]]; then
    line "PASS" "YAML field present: projects.${PROJECT_ID}.tracker.plugin=${tracker_plugin}"
  else
    line "FAIL" "Missing YAML field: projects.${PROJECT_ID}.tracker.plugin"
    failures=$((failures + 1))
  fi
fi

if require_cmd gh; then
  if gh auth status -h github.com >/dev/null 2>&1; then
    line "PASS" "GitHub CLI auth ok (gh auth status)"
  else
    line "FAIL" "GitHub CLI not authenticated. Run: gh auth login"
    failures=$((failures + 1))
  fi
fi

if [[ -n "${LINEAR_API_KEY:-}" || -n "${COMPOSIO_API_KEY:-}" ]]; then
  line "PASS" "Linear auth env found (LINEAR_API_KEY or COMPOSIO_API_KEY)"
else
  if [[ "$ALLOW_MISSING_LINEAR" -eq 1 ]]; then
    line "WARN" "Linear auth env missing but allowed by flag"
    warnings=$((warnings + 1))
  else
    line "FAIL" "Missing LINEAR_API_KEY/COMPOSIO_API_KEY"
    failures=$((failures + 1))
  fi
fi

echo
echo "PR flow smoke test steps"
echo "1. ao start"
echo "2. ./scripts/agents/bootstrap-feature.sh --scope <scope> --base main --with-tmux --force"
echo "3. Edit /tmp/multi-agent-sync/<scope>/context.md with issue objective and acceptance criteria"
echo "4. ./scripts/agents/run-feature-once-ao.sh --scope <scope>"
echo "5. ao status && ao session ls"
echo "6. Verify /tmp/multi-agent-sync/<scope>/outbox/orchestrator.md contains PASS or CHANGES REQUIRED"
echo "7. If CHANGES REQUIRED, run ./scripts/agents/create-followup-bugs.sh --scope <scope>"

echo
if [[ "$failures" -gt 0 ]]; then
  echo "Result: FAILED (${failures} blocking, ${warnings} warning)"
  exit 1
fi

echo "Result: READY (${warnings} warning)"
