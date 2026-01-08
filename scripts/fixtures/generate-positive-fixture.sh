#!/usr/bin/env bash
# Generate a positive LLM fixture by using ForceAllow mode

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# Colors
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

usage() {
    cat <<EOF
Usage: $0 [OPTIONS]

Generate a positive (ALLOW) LLM fixture using ForceAllow mode to bypass actual LLM.

This script:
1. Temporarily enables MCP_PROVIDER__FORCEALLOW=true
2. Sends a test webhook with good market conditions
3. Waits for processing
4. Captures the fixture
5. Restores original configuration

OPTIONS:
    -h, --help              Show this help message
    -d, --description TEXT  Description of the scenario
    -s, --symbol TEXT       Symbol to use (default: PF_XBTUSD)
    -D, --direction TEXT    Direction: LONG or SHORT (default: LONG)
    -k, --keep-config       Don't restore original config after test

NOTES:
    - Requires containers to be running
    - Will restart worker to apply config changes
    - Creates a fixture in tests/fixtures/llm-decisions/accept/
    - ForceAllow only works if valid Elliott candidates exist

EXAMPLES:
    # Generate LONG scenario
    $0 -d "Strong 5-wave impulse" -D LONG

    # Generate SHORT scenario
    $0 -d "Clear corrective wave" -D SHORT -s PF_ETHUSD
EOF
    exit 0
}

info() {
    echo -e "${GREEN}INFO: $1${NC}"
}

warn() {
    echo -e "${YELLOW}WARN: $1${NC}"
}

error() {
    echo -e "\033[0;31mERROR: $1${NC}" >&2
    exit 1
}

# Defaults
DESCRIPTION="ForceAllow generated positive case"
SYMBOL="PF_XBTUSD"
DIRECTION="LONG"
KEEP_CONFIG=false

# Parse args
while [[ $# -gt 0 ]]; do
    case $1 in
        -h|--help) usage ;;
        -d|--description) DESCRIPTION="$2"; shift 2 ;;
        -s|--symbol) SYMBOL="$2"; shift 2 ;;
        -D|--direction) DIRECTION="$2"; shift 2 ;;
        -k|--keep-config) KEEP_CONFIG=true; shift ;;
        *) error "Unknown option: $1" ;;
    esac
done

# Validate direction
if [[ "$DIRECTION" != "LONG" && "$DIRECTION" != "SHORT" ]]; then
    error "Direction must be LONG or SHORT"
fi

info "Generating positive fixture with ForceAllow mode"
info "Symbol: $SYMBOL, Direction: $DIRECTION"

# Check if .env exists
ENV_FILE="$PROJECT_ROOT/.env"
if [[ ! -f "$ENV_FILE" ]]; then
    error ".env not found. Please create it first."
fi

# Backup original config
BACKUP_FILE="$ENV_FILE.backup.$(date +%s)"
info "Backing up $ENV_FILE to $BACKUP_FILE"
cp "$ENV_FILE" "$BACKUP_FILE"

# Enable ForceAllow
info "Enabling ForceAllow in configuration..."
if grep -q "^MCP_FORCE_ALLOW=" "$ENV_FILE"; then
    sed -i '' 's/^MCP_FORCE_ALLOW=.*/MCP_FORCE_ALLOW=true/' "$ENV_FILE"
else
    echo "MCP_FORCE_ALLOW=true" >> "$ENV_FILE"
fi

# Restart worker
info "Restarting worker to apply config..."
cd "$PROJECT_ROOT"
docker compose stop worker > /dev/null 2>&1
docker compose up -d worker > /dev/null 2>&1

info "Waiting 10 seconds for worker to start..."
sleep 10

# Generate idempotency key
IDEMPOTENCY_KEY="forceallow-$(date +%s)"

# Get ngrok URL
NGROK_URL=$(curl -s http://localhost:4040/api/tunnels | jq -r '.tunnels[0].public_url')
if [[ -z "$NGROK_URL" || "$NGROK_URL" == "null" ]]; then
    error "Could not get ngrok URL. Is ngrok running?"
fi

info "Sending test webhook..."

# Send webhook
curl -X POST "$NGROK_URL/webhooks/tradingview/REDACTED_WEBHOOK_SECRET" \
  -H "Content-Type: application/json" \
  -d "{
    \"idempotencyKey\": \"$IDEMPOTENCY_KEY\",
    \"ticker\": \"BTC/USD\",
    \"exchange\": \"KRAKEN\",
    \"interval\": \"15m\",
    \"close\": 43500.00,
    \"volume\": 200.0,
    \"directionHint\": \"$DIRECTION\",
    \"symbolHint\": \"$SYMBOL\",
    \"reason\": \"ForceAllow positive test case\"
  }" > /dev/null 2>&1

info "Webhook sent. Waiting 20 seconds for processing..."
sleep 20

# Check if alert was processed
ALERT_STATUS=$(docker exec ai-assisted-postgres-1 psql -U postgres -d ai-trading-db -t -c \
    "SELECT status FROM alert_processing WHERE idempotency_key = '$IDEMPOTENCY_KEY';" | tr -d ' ')

if [[ -z "$ALERT_STATUS" ]]; then
    error "Alert not found in database. Check worker logs."
fi

info "Alert status: $ALERT_STATUS"

# Restore config unless --keep-config specified
if [[ "$KEEP_CONFIG" == false ]]; then
    info "Restoring original configuration..."
    mv "$BACKUP_FILE" "$ENV_FILE"
    info "Restarting worker to restore config..."
    cd "$PROJECT_ROOT"
    docker compose stop worker > /dev/null 2>&1
    docker compose up -d worker > /dev/null 2>&1
else
    warn "Keeping ForceAllow enabled as requested (--keep-config)"
    info "Backup saved at: $BACKUP_FILE"
fi

# Capture the fixture
info "Capturing fixture..."
"$SCRIPT_DIR/capture-llm-decision.sh" \
    -d "$DESCRIPTION" \
    -s "$DIRECTION entry with ForceAllow" \
    -c accept \
    "$IDEMPOTENCY_KEY"

echo ""
info "✓ Positive fixture generation complete!"
echo ""

if [[ "$ALERT_STATUS" == "executed" ]]; then
    echo -e "${GREEN}SUCCESS: Alert was executed (ForceAllow worked)${NC}"
elif [[ "$ALERT_STATUS" == *"failed"* ]]; then
    warn "Alert processing failed. Check the fixture for details."
else
    warn "Alert status is '$ALERT_STATUS'. Expected 'executed' for positive case."
fi

echo ""
echo "Next steps:"
echo "1. Review the captured fixture"
echo "2. Verify the Elliott candidates had sufficient data"
echo "3. Use this as a template for real positive cases"
echo "4. Try to get the LLM to produce similar ALLOW decisions naturally"
echo ""

exit 0
