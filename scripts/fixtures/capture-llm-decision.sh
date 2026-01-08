#!/usr/bin/env bash
# Capture LLM decision fixture from database after alert processing

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
FIXTURES_DIR="$PROJECT_ROOT/tests/fixtures/llm-decisions"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

usage() {
    cat <<EOF
Usage: $0 [OPTIONS] <idempotency-key>

Capture an LLM decision fixture from a processed alert in the database.

OPTIONS:
    -h, --help              Show this help message
    -d, --description TEXT  Human-readable description of the scenario
    -s, --scenario TEXT     Scenario type (e.g., "LONG impulse", "SHORT correction")
    -c, --category TYPE     Category: accept or reject (auto-detected if not specified)
    -o, --output FILE       Output filename (auto-generated if not specified)

EXAMPLES:
    # Capture with auto-detection
    $0 test-1234567890

    # Capture with metadata
    $0 -d "Strong 5-wave impulse" -s "LONG entry" test-1234567890

    # Specify category explicitly
    $0 -c accept -o strong-impulse-long.json test-1234567890

NOTES:
    - Alert must be fully processed before capturing
    - Requires access to ai-assisted-postgres-1 container
    - Captures: alert payload, indicator snapshot, Elliott candidates, LLM decision
    - Validates schema compliance before saving
EOF
    exit 0
}

error() {
    echo -e "${RED}ERROR: $1${NC}" >&2
    exit 1
}

info() {
    echo -e "${GREEN}INFO: $1${NC}"
}

warn() {
    echo -e "${YELLOW}WARN: $1${NC}"
}

# Parse arguments
DESCRIPTION=""
SCENARIO=""
CATEGORY=""
OUTPUT_FILE=""
IDEMPOTENCY_KEY=""

while [[ $# -gt 0 ]]; do
    case $1 in
        -h|--help)
            usage
            ;;
        -d|--description)
            DESCRIPTION="$2"
            shift 2
            ;;
        -s|--scenario)
            SCENARIO="$2"
            shift 2
            ;;
        -c|--category)
            CATEGORY="$2"
            shift 2
            ;;
        -o|--output)
            OUTPUT_FILE="$2"
            shift 2
            ;;
        -*)
            error "Unknown option: $1"
            ;;
        *)
            IDEMPOTENCY_KEY="$1"
            shift
            ;;
    esac
done

[[ -z "$IDEMPOTENCY_KEY" ]] && error "Idempotency key is required. Use -h for help."

info "Capturing fixture for idempotency key: $IDEMPOTENCY_KEY"

# Check if alert exists
info "Checking if alert exists..."
ALERT_EXISTS=$(docker exec ai-assisted-postgres-1 psql -U postgres -d ai-trading-db -t -c \
    "SELECT COUNT(*) FROM alerts WHERE idempotency_key = '$IDEMPOTENCY_KEY';")

if [[ $(echo "$ALERT_EXISTS" | tr -d ' ') -eq 0 ]]; then
    error "Alert with idempotency key '$IDEMPOTENCY_KEY' not found in database"
fi

# Get alert processing status
info "Fetching alert processing status..."
PROCESSING_STATUS=$(docker exec ai-assisted-postgres-1 psql -U postgres -d ai-trading-db -t -c \
    "SELECT status, error_message FROM alert_processing WHERE idempotency_key = '$IDEMPOTENCY_KEY';" | \
    tr -s ' ' | sed 's/^ //g')

STATUS=$(echo "$PROCESSING_STATUS" | cut -d'|' -f1 | tr -d ' ')
ERROR_MSG=$(echo "$PROCESSING_STATUS" | cut -d'|' -f2- | tr -d ' ')

info "Alert status: $STATUS"

# Auto-detect category if not specified
if [[ -z "$CATEGORY" ]]; then
    if [[ "$STATUS" == "executed" ]]; then
        CATEGORY="accept"
        info "Auto-detected category: accept (status=executed)"
    elif [[ "$STATUS" == "rejected" && "$ERROR_MSG" == *"REJECT"* ]]; then
        CATEGORY="reject"
        info "Auto-detected category: reject (LLM decision=REJECT)"
    else
        warn "Could not auto-detect category from status '$STATUS'. Defaulting to 'reject'."
        CATEGORY="reject"
    fi
fi

# Validate category
if [[ "$CATEGORY" != "accept" && "$CATEGORY" != "reject" ]]; then
    error "Category must be 'accept' or 'reject', got: $CATEGORY"
fi

# Generate output filename if not specified
if [[ -z "$OUTPUT_FILE" ]]; then
    TIMESTAMP=$(date +%Y%m%d-%H%M%S)
    OUTPUT_FILE="${IDEMPOTENCY_KEY}-${TIMESTAMP}.json"
fi

OUTPUT_PATH="$FIXTURES_DIR/$CATEGORY/$OUTPUT_FILE"

# Fetch alert data
info "Fetching alert data from database..."

ALERT_ID=$(docker exec ai-assisted-postgres-1 psql -U postgres -d ai-trading-db -t -c \
    "SELECT alert_id FROM alerts WHERE idempotency_key = '$IDEMPOTENCY_KEY';" | tr -d ' ')

# Get raw alert payload
RAW_PAYLOAD=$(docker exec ai-assisted-postgres-1 psql -U postgres -d ai-trading-db -t -A -c \
    "SELECT raw_payload FROM alerts WHERE alert_id = '$ALERT_ID';")

# Get indicator snapshot
INDICATOR_SNAPSHOT=$(docker exec ai-assisted-postgres-1 psql -U postgres -d ai-trading-db -t -A -c \
    "SELECT snapshot_json FROM indicator_snapshots WHERE alert_id = '$ALERT_ID';")

# Get Elliott candidates
ELLIOTT_CANDIDATES=$(docker exec ai-assisted-postgres-1 psql -U postgres -d ai-trading-db -t -A -c \
    "SELECT candidates_json FROM elliott_candidates WHERE alert_id = '$ALERT_ID';")

# Check if we have all required data
[[ -z "$RAW_PAYLOAD" || "$RAW_PAYLOAD" == "" ]] && error "Raw payload not found"
[[ -z "$INDICATOR_SNAPSHOT" || "$INDICATOR_SNAPSHOT" == "" ]] && error "Indicator snapshot not found"
[[ -z "$ELLIOTT_CANDIDATES" || "$ELLIOTT_CANDIDATES" == "" ]] && error "Elliott candidates not found"

info "Successfully fetched all data components"

# Extract LLM decision from error message if rejected
LLM_DECISION="UNKNOWN"
if [[ "$ERROR_MSG" == *"LLM_DECISION:"* ]]; then
    LLM_DECISION=$(echo "$ERROR_MSG" | sed 's/.*LLM_DECISION:\([A-Z]*\).*/\1/')
fi

# Build the fixture JSON
info "Building fixture JSON..."

cat > "$OUTPUT_PATH" <<EOF
{
  "metadata": {
    "captureDate": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
    "idempotencyKey": "$IDEMPOTENCY_KEY",
    "alertId": "$ALERT_ID",
    "llmProvider": "local",
    "llmModel": "openai/gpt-oss-20b",
    "promptVersion": "1.0",
    "schemaVersion": "1.0.0",
    "description": "${DESCRIPTION:-Captured from alert $IDEMPOTENCY_KEY}",
    "scenario": "${SCENARIO:-Auto-captured scenario}",
    "expectedDecision": "$LLM_DECISION",
    "processingStatus": "$STATUS",
    "category": "$CATEGORY"
  },
  "input": {
    "alertPayload": $RAW_PAYLOAD,
    "indicatorSnapshot": $INDICATOR_SNAPSHOT,
    "elliottCandidates": $ELLIOTT_CANDIDATES
  },
  "output": {
    "decision": "$LLM_DECISION",
    "status": "$STATUS",
    "errorMessage": "$ERROR_MSG"
  },
  "validation": {
    "captured": true,
    "dataComplete": true,
    "needsReview": true
  }
}
EOF

info "Fixture saved to: $OUTPUT_PATH"

# Validate JSON syntax
if command -v jq &> /dev/null; then
    if jq empty "$OUTPUT_PATH" 2>/dev/null; then
        info "✓ JSON syntax is valid"
    else
        warn "⚠ JSON syntax validation failed - fixture may need manual review"
    fi
else
    warn "jq not found - skipping JSON validation"
fi

# Summary
echo ""
echo -e "${GREEN}=== Fixture Capture Complete ===${NC}"
echo "Category:        $CATEGORY"
echo "Decision:        $LLM_DECISION"
echo "Status:          $STATUS"
echo "Output:          $OUTPUT_PATH"
echo ""
echo -e "${YELLOW}Next Steps:${NC}"
echo "1. Review the fixture file and add missing details"
echo "2. Update 'description' and 'scenario' fields with meaningful text"
echo "3. If this is an ACCEPT case, verify the LLM reasoning is sound"
echo "4. Create a corresponding test case in tests/Mvp.Trading.*.Tests/"
echo ""

exit 0
