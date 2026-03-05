#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<USAGE
Usage:
  scripts/agents/create-followup-bugs.sh --scope <scope-id> [--backlog <path>]

Reads reviewer/quality/integrator outbox reports and appends backlog bug items
for next iteration when blocking findings are detected.
USAGE
}

SCOPE=""
BACKLOG="docs/backlog.md"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --scope) SCOPE="${2:-}"; shift 2 ;;
    --backlog) BACKLOG="${2:-}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown arg: $1" >&2; usage; exit 1 ;;
  esac
done

if [[ -z "$SCOPE" ]]; then
  echo "--scope is required" >&2
  exit 1
fi

if [[ ! -f "$BACKLOG" ]]; then
  echo "backlog not found: $BACKLOG" >&2
  exit 1
fi

SCOPE="$(echo "$SCOPE" | tr '[:upper:]' '[:lower:]' | sed -E 's#[^a-z0-9._-]+#-#g; s#^-+##; s#-+$##')"
SYNC_DIR="/tmp/multi-agent-sync/${SCOPE}"

if [[ ! -d "$SYNC_DIR/outbox" ]]; then
  echo "outbox dir not found: $SYNC_DIR/outbox" >&2
  exit 1
fi

needs_bug() {
  local file="$1"
  rg -qi "CHANGES_REQUIRED|FAIL|FAILED|REJECT|BLOCKER|BLOCKED|CRITICAL|HIGH" "$file"
}

extract_trigger() {
  local file="$1"
  rg -in "CHANGES_REQUIRED|FAIL|FAILED|REJECT|BLOCKER|BLOCKED|CRITICAL|HIGH" "$file" | head -n 1 | sed -E 's/^([0-9]+:)?//'
}

append_section_if_missing() {
  if ! rg -q "^### Multi-Agent Follow-up Bugs \(Auto\)" "$BACKLOG"; then
    cat >> "$BACKLOG" <<'SEC'

### Multi-Agent Follow-up Bugs (Auto)
**Goal**: Track blocking findings from reviewer/quality/integrator for the next iteration.
SEC
  fi
}

append_bug() {
  local role="$1" file="$2"
  local marker="AUTOBUG:${SCOPE}:${role}"
  if rg -q "$marker" "$BACKLOG"; then
    echo "Backlog item already exists for ${marker}; skipping"
    return 0
  fi

  local stamp trigger role_upper
  stamp="$(date +%Y-%m-%d)"
  trigger="$(extract_trigger "$file")"
  role_upper="$(echo "$role" | tr '[:lower:]' '[:upper:]')"
  if [[ -z "$trigger" ]]; then
    trigger="Blocking findings reported in ${role} output"
  fi

  cat >> "$BACKLOG" <<BUG
- Story BUG.${stamp}.${role}: [PRIORITY: NEXT_ITERATION] ${SCOPE} - ${role_upper} reported blocking findings (${marker})
  - Source: /tmp/multi-agent-sync/${SCOPE}/outbox/${role}.md
  - Trigger: ${trigger}
  - Required action: Fix blocking findings before continuing feature delivery.
BUG

  echo "Added backlog bug for role: $role"
}

append_section_if_missing

for role in reviewer quality integrator; do
  file="$SYNC_DIR/outbox/${role}.md"
  if [[ ! -f "$file" ]]; then
    continue
  fi
  if needs_bug "$file"; then
    append_bug "$role" "$file"
  fi
done

echo "Done: backlog follow-up bug generation completed for scope ${SCOPE}"
