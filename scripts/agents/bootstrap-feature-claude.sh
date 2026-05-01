#!/usr/bin/env bash
set -euo pipefail

# Claude-native feature bootstrap.
# Creates the /tmp handoff bus and per-role git branches.
# No git worktrees needed — Claude agents share the main worktree.

usage() {
  cat <<USAGE
Usage:
  scripts/agents/bootstrap-feature-claude.sh --scope <scope-id> [options]

Options:
  --base <branch>     Base branch for agent branches (default: main)
  --force             Delete existing sync dir and re-bootstrap
  --no-branches       Skip git branch creation (useful for doc-only scopes)
  -h, --help          Show this help

Example:
  scripts/agents/bootstrap-feature-claude.sh --scope m14-8-security-hardening --base main
USAGE
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || { echo "Missing: $1" >&2; exit 1; }
}

sanitize_scope() {
  echo "$1" | tr '[:upper:]' '[:lower:]' | sed -E 's#[^a-z0-9._-]+#-#g; s#^-+##; s#-+$##'
}

SCOPE=""
BASE_BRANCH="main"
FORCE=0
NO_BRANCHES=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --scope)       SCOPE="${2:-}"; shift 2 ;;
    --base)        BASE_BRANCH="${2:-}"; shift 2 ;;
    --force)       FORCE=1; shift ;;
    --no-branches) NO_BRANCHES=1; shift ;;
    -h|--help)     usage; exit 0 ;;
    *)             echo "Unknown argument: $1" >&2; usage; exit 1 ;;
  esac
done

[[ -z "$SCOPE" ]] && { echo "Error: --scope is required" >&2; usage; exit 1; }

require_cmd git

ROOT="$(git rev-parse --show-toplevel)"
SCOPE="$(sanitize_scope "$SCOPE")"
SYNC_DIR="/tmp/multi-agent-sync/${SCOPE}"

echo "Bootstrap Claude multi-agent run"
echo "Scope: ${SCOPE}"
echo "Base:  ${BASE_BRANCH}"
echo "Sync:  ${SYNC_DIR}"
echo

# ── Sync dir ────────────────────────────────────────────────────────────────
if [[ -d "$SYNC_DIR" ]]; then
  if [[ "$FORCE" -eq 1 ]]; then
    echo "Removing existing sync dir (--force)..."
    rm -rf "$SYNC_DIR"
  else
    echo "Sync dir already exists: ${SYNC_DIR}"
    echo "Use --force to recreate, or continue with existing state."
  fi
fi

mkdir -p \
  "${SYNC_DIR}/inbox" \
  "${SYNC_DIR}/outbox" \
  "${SYNC_DIR}/state" \
  "${SYNC_DIR}/prompts-claude"

# ── README ───────────────────────────────────────────────────────────────────
cat > "${SYNC_DIR}/README.txt" <<README
Claude Multi-Agent Sync Bus
Scope: ${SCOPE}
Bootstrap date: $(date -u +"%Y-%m-%dT%H:%M:%SZ")
Base branch: ${BASE_BRANCH}
Root: ${ROOT}

Inbox files:   inbox/<role>.md   — role-specific instructions
Outbox files:  outbox/<role>.md  — human-readable reports
               outbox/<role>.json — machine-readable findings
State files:   state/<role>.done — completion markers

Stage order: planner → (rubber-duck) → builder → [reviewer + quality] → tester → integrator → orchestrator
README

# ── Context placeholder ──────────────────────────────────────────────────────
if [[ ! -f "${SYNC_DIR}/context.md" ]]; then
  cat > "${SYNC_DIR}/context.md" <<CONTEXT
# Feature Context: ${SCOPE}

## Scope
<!-- Describe what needs to be done -->

## Acceptance Criteria
<!-- What does done look like? -->

## Constraints
<!-- Any specific constraints or risks -->

## References
- Backlog stories: <!-- e.g., M14.5.1, M14.5.2 -->
- Skill file: ${ROOT}/docs/csharp-dotnet10-skill.md
- Contract: ${ROOT}/CLAUDE.md
CONTEXT
  echo "Created context placeholder: ${SYNC_DIR}/context.md"
  echo "→ Edit it before running the agent pipeline."
fi

# ── Inbox files ──────────────────────────────────────────────────────────────
for role in planner builder reviewer quality tester integrator orchestrator; do
  inbox="${SYNC_DIR}/inbox/${role}.md"
  role_upper="$(echo "$role" | tr '[:lower:]' '[:upper:]')"
  if [[ ! -f "$inbox" ]]; then
    cat > "$inbox" <<INBOX
# ${role_upper} Inbox — ${SCOPE}

Read \`${SYNC_DIR}/context.md\` for feature brief.
Read \`${ROOT}/CLAUDE.md\` for role instructions, policies, and M14 anti-patterns.
Read \`${ROOT}/docs/csharp-dotnet10-skill.md\` for C# 14 / .NET 10 standards.

Write your report to:
  ${SYNC_DIR}/outbox/${role}.md  (human-readable)
  ${SYNC_DIR}/outbox/${role}.json (machine-readable)

Create completion marker when done:
  ${SYNC_DIR}/state/${role}.done
INBOX
  fi
done

echo "Inbox files created."

# ── Git branch (single feature branch for all agents) ────────────────────────
if [[ "$NO_BRANCHES" -eq 0 ]]; then
  echo
  FEATURE_BRANCH="feature/${SCOPE}"
  echo "Ensuring feature branch: ${FEATURE_BRANCH} (from ${BASE_BRANCH})..."
  git fetch origin "${BASE_BRANCH}" --quiet 2>/dev/null || true

  if git show-ref --verify --quiet "refs/heads/${FEATURE_BRANCH}"; then
    echo "  Branch already exists: ${FEATURE_BRANCH}"
  else
    git branch "${FEATURE_BRANCH}" "origin/${BASE_BRANCH}" 2>/dev/null || \
      git branch "${FEATURE_BRANCH}" "${BASE_BRANCH}"
    echo "  Created: ${FEATURE_BRANCH}"
  fi
  echo
  echo "All agents work on: ${FEATURE_BRANCH}"
  echo "Only the builder commits. After committing, builder saves:"
  echo "  git diff ${BASE_BRANCH}..HEAD > ${SYNC_DIR}/outbox/builder.diff"
fi

echo
echo "Bootstrap complete."
echo
echo "Next steps:"
echo "  1. Edit context:  nano ${SYNC_DIR}/context.md"
echo "  2. Run pipeline:  ./scripts/agents/run-feature-once-claude.sh --scope ${SCOPE}"
