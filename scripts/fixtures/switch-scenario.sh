#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
SCENARIOS_DIR="$PROJECT_ROOT/config/scenarios"
CONFIG_DIR="$PROJECT_ROOT/config"

# Color output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

usage() {
    cat << EOF
Usage: $0 <scenario>

Switch configuration files for different M9 fixture testing scenarios.

Available scenarios:
  original          - $500k equity, qtyStep=1 (whole contracts)
  fractional-100    - $100 equity, qtyStep=0.001 (fractional)
  fractional-1000   - $1,000 equity, qtyStep=0.001 (fractional)
  fractional-10000  - $10,000 equity, qtyStep=0.001 (fractional)

Examples:
  $0 original
  $0 fractional-100

Current scenario can be checked with:
  cat config/account.json | jq '.equity'
EOF
    exit 1
}

if [ $# -ne 1 ]; then
    usage
fi

SCENARIO=$1

# Validate scenario
case "$SCENARIO" in
    original)
        ACCOUNT_FILE="account.original-500k.json"
        INSTRUMENTS_FILE="instruments.original-qtystep1.json"
        EQUITY="\$500,000"
        QTY_STEP="1"
        ;;
    fractional-100)
        ACCOUNT_FILE="account.fractional-100.json"
        INSTRUMENTS_FILE="instruments.fractional-100.json"
        EQUITY="\$100"
        QTY_STEP="0.0002"
        ;;
    fractional-1000)
        ACCOUNT_FILE="account.fractional-1000.json"
        INSTRUMENTS_FILE="instruments.fractional-1000.json"
        EQUITY="\$1,000"
        QTY_STEP="0.002"
        ;;
    fractional-10000)
        ACCOUNT_FILE="account.fractional-10000.json"
        INSTRUMENTS_FILE="instruments.fractional-10000.json"
        EQUITY="\$10,000"
        QTY_STEP="0.02"
        ;;
    *)
        echo -e "${RED}Error: Unknown scenario '$SCENARIO'${NC}"
        usage
        ;;
esac

echo -e "${BLUE}=== Switching to scenario: $SCENARIO ===${NC}"
echo -e "Equity: ${GREEN}$EQUITY${NC}"
echo -e "qtyStep: ${GREEN}$QTY_STEP${NC}"
echo ""

# Check if scenario files exist
if [ ! -f "$SCENARIOS_DIR/$ACCOUNT_FILE" ]; then
    echo -e "${RED}Error: Account file not found: $SCENARIOS_DIR/$ACCOUNT_FILE${NC}"
    exit 1
fi

if [ ! -f "$SCENARIOS_DIR/$INSTRUMENTS_FILE" ]; then
    echo -e "${RED}Error: Instruments file not found: $SCENARIOS_DIR/$INSTRUMENTS_FILE${NC}"
    exit 1
fi

# Backup current config (if not already backed up)
TIMESTAMP=$(date +%Y%m%d-%H%M%S)
BACKUP_DIR="$CONFIG_DIR/.backups/$TIMESTAMP"
mkdir -p "$BACKUP_DIR"

echo -e "${YELLOW}Backing up current config to: $BACKUP_DIR${NC}"
cp "$CONFIG_DIR/account.json" "$BACKUP_DIR/account.json"
cp "$CONFIG_DIR/instruments.json" "$BACKUP_DIR/instruments.json"

# Copy scenario files to active config
echo -e "${YELLOW}Copying scenario files...${NC}"
cp "$SCENARIOS_DIR/$ACCOUNT_FILE" "$CONFIG_DIR/account.json"
cp "$SCENARIOS_DIR/$INSTRUMENTS_FILE" "$CONFIG_DIR/instruments.json"

echo -e "${GREEN}✓ Configuration switched successfully${NC}"
echo ""

# Show current config
echo -e "${BLUE}Current configuration:${NC}"
echo -n "  Account equity: "
cat "$CONFIG_DIR/account.json" | jq -r '.equity'
echo -n "  BTC qtyStep: "
cat "$CONFIG_DIR/instruments.json" | jq -r '.instruments[] | select(.symbol == "BTCUSD.P") | .qtyStep'
echo -n "  BTC minQty: "
cat "$CONFIG_DIR/instruments.json" | jq -r '.instruments[] | select(.symbol == "BTCUSD.P") | .minQty'

echo ""
echo -e "${YELLOW}⚠️  Remember to rebuild the Docker worker to apply changes:${NC}"
echo -e "   ${BLUE}docker compose build worker${NC}"
echo -e "   ${BLUE}docker compose down && docker compose up -d${NC}"
