#!/usr/bin/env bash
set -euo pipefail

# M6.5 E2E Audit Chain Validation Script
# Validates the complete alert-to-execution audit chain in the database.
# Usage: ./validate-audit-chain.sh <idempotency_key>

IDEMPOTENCY_KEY="${1:-}"
USE_DOCKER="${USE_DOCKER:-auto}"
DB_HOST="${POSTGRES_HOST:-localhost}"
DB_PORT="${POSTGRES_PORT:-5432}"
DB_NAME="${POSTGRES_DB:-ai-trading-db}"
DB_USER="${POSTGRES_USER:-postgres}"
PGPASSWORD="${POSTGRES_PASSWORD:-postgres}"
export PGPASSWORD

# Auto-detect if we should use Docker
if [ "$USE_DOCKER" = "auto" ]; then
  if docker compose ps postgres 2>/dev/null | grep -q "Up"; then
    USE_DOCKER="yes"
  else
    USE_DOCKER="no"
  fi
fi

if [ -z "$IDEMPOTENCY_KEY" ]; then
  echo "Usage: $0 <idempotency_key>" >&2
  echo "Example: $0 smoke-1234567890" >&2
  exit 1
fi

echo "=========================================="
echo "M6.5 E2E Audit Chain Validation"
echo "=========================================="
echo "Idempotency Key: $IDEMPOTENCY_KEY"
echo "Database: $DB_HOST:$DB_PORT/$DB_NAME"
echo ""

PSQL="psql -h $DB_HOST -p $DB_PORT -U $DB_USER -d $DB_NAME -t -A"

# Step 1: Get alert_id and processing status
echo "Step 1: Checking alert processing status..."
ALERT_DATA=$($PSQL -c "SELECT alert_id, status FROM alert_processing WHERE idempotency_key = '$IDEMPOTENCY_KEY';")

if [ -z "$ALERT_DATA" ]; then
  echo "❌ FAIL: No alert found for idempotency key '$IDEMPOTENCY_KEY'"
  exit 1
fi

ALERT_ID=$(echo "$ALERT_DATA" | cut -d'|' -f1)
ALERT_STATUS=$(echo "$ALERT_DATA" | cut -d'|' -f2)

echo "   Alert ID: $ALERT_ID"
echo "   Status: $ALERT_STATUS"

if [ "$ALERT_STATUS" != "executed" ]; then
  echo "⚠️  WARNING: Alert status is '$ALERT_STATUS', not 'executed'"
  echo "   This may be expected if execution was rejected or failed."
fi
echo ""

# Step 2: Check trade plan
echo "Step 2: Checking trade plan..."
PLAN_DATA=$($PSQL -c "SELECT plan_id FROM trade_plan WHERE alert_id = '$ALERT_ID';")

if [ -z "$PLAN_DATA" ]; then
  echo "❌ FAIL: No trade plan found for alert $ALERT_ID"
  if [ "$ALERT_STATUS" = "rejected" ] || [ "$ALERT_STATUS" = "adjudication_failed" ]; then
    echo "   This is expected for status '$ALERT_STATUS'"
    exit 0
  fi
  exit 1
fi

PLAN_ID=$(echo "$PLAN_DATA")
echo "   Plan ID: $PLAN_ID"

# Get trade plan details
PLAN_JSON=$($PSQL -c "SELECT plan_json FROM trade_plan WHERE plan_id = '$PLAN_ID';")
SYMBOL=$(echo "$PLAN_JSON" | grep -o '"Symbol":"[^"]*"' | cut -d'"' -f4 || echo "N/A")
SIDE=$(echo "$PLAN_JSON" | grep -o '"Side":"[^"]*"' | cut -d'"' -f4 || echo "N/A")
QTY=$(echo "$PLAN_JSON" | grep -o '"Quantity":[0-9.]*' | cut -d':' -f2 || echo "N/A")
TP_COUNT=$(echo "$PLAN_JSON" | grep -o '"TakeProfitTargets":\[[^]]*\]' | grep -o '"Price"' | wc -l | xargs)

echo "   Symbol: $SYMBOL"
echo "   Side: $SIDE"
echo "   Quantity: $QTY"
echo "   Take-Profit Targets: $TP_COUNT"

if [ "$TP_COUNT" -lt 3 ]; then
  echo "⚠️  WARNING: Expected at least 3 take-profit targets, found $TP_COUNT"
fi
echo ""

# Step 3: Check execution intent
echo "Step 3: Checking execution intent..."
INTENT_DATA=$($PSQL -c "SELECT execution_id, mode, status FROM execution_intent WHERE plan_id = '$PLAN_ID';")

if [ -z "$INTENT_DATA" ]; then
  echo "❌ FAIL: No execution intent found for plan $PLAN_ID"
  exit 1
fi

EXECUTION_ID=$(echo "$INTENT_DATA" | cut -d'|' -f1)
EXECUTION_MODE=$(echo "$INTENT_DATA" | cut -d'|' -f2)
EXECUTION_STATUS=$(echo "$INTENT_DATA" | cut -d'|' -f3)

echo "   Execution ID: $EXECUTION_ID"
echo "   Mode: $EXECUTION_MODE"
echo "   Status: $EXECUTION_STATUS"
echo ""

# Step 4: Check order receipts (CRITICAL)
echo "Step 4: Checking order receipts..."
RECEIPT_COUNT=$($PSQL -c "SELECT COUNT(*) FROM order_receipt WHERE execution_id = '$EXECUTION_ID';")

echo "   Total Receipts: $RECEIPT_COUNT"

if [ "$RECEIPT_COUNT" -eq 0 ]; then
  echo "❌ FAIL: No order receipts found for execution $EXECUTION_ID"
  exit 1
fi

# Count by order kind
echo ""
echo "   Receipt breakdown:"
RECEIPT_BREAKDOWN=$($PSQL -c "SELECT order_kind, COUNT(*) FROM order_receipt WHERE execution_id = '$EXECUTION_ID' GROUP BY order_kind ORDER BY order_kind;")

ENTRY_COUNT=0
STOP_COUNT=0
TP_RECEIPT_COUNT=0

while IFS='|' read -r kind count; do
  echo "     - $kind: $count"
  case "$kind" in
    ENTRY) ENTRY_COUNT=$count ;;
    STOP) STOP_COUNT=$count ;;
    TAKE_PROFIT_*) TP_RECEIPT_COUNT=$((TP_RECEIPT_COUNT + count)) ;;
  esac
done <<< "$RECEIPT_BREAKDOWN"

echo ""

# Validate receipt counts
ISSUES=0

if [ "$ENTRY_COUNT" -ne 1 ]; then
  echo "❌ FAIL: Expected 1 ENTRY receipt, found $ENTRY_COUNT"
  ISSUES=$((ISSUES + 1))
fi

if [ "$STOP_COUNT" -ne 1 ]; then
  echo "❌ FAIL: Expected 1 STOP receipt, found $STOP_COUNT"
  ISSUES=$((ISSUES + 1))
fi

if [ "$TP_RECEIPT_COUNT" -lt 3 ]; then
  echo "❌ FAIL: Expected at least 3 TAKE_PROFIT receipts, found $TP_RECEIPT_COUNT"
  ISSUES=$((ISSUES + 1))
fi

# Step 5: Check execution heartbeat
echo "Step 5: Checking execution heartbeat..."
HEARTBEAT_DATA=$($PSQL -c "SELECT last_beat_utc, stale_threshold_seconds FROM execution_heartbeat WHERE service_name = 'execution-service';")

if [ -z "$HEARTBEAT_DATA" ]; then
  echo "⚠️  WARNING: No heartbeat record found for execution-service"
else
  LAST_BEAT=$(echo "$HEARTBEAT_DATA" | cut -d'|' -f1)
  THRESHOLD=$(echo "$HEARTBEAT_DATA" | cut -d'|' -f2)
  echo "   Last Beat: $LAST_BEAT"
  echo "   Stale Threshold: ${THRESHOLD}s"
fi
echo ""

# Step 6: Audit chain linkage summary
echo "=========================================="
echo "Audit Chain Summary"
echo "=========================================="
echo "✓ Alert:           $ALERT_ID ($ALERT_STATUS)"
echo "✓ Trade Plan:      $PLAN_ID ($SYMBOL $SIDE)"
echo "✓ Execution:       $EXECUTION_ID ($EXECUTION_MODE → $EXECUTION_STATUS)"
echo "✓ Order Receipts:  $RECEIPT_COUNT total"
echo "  - ENTRY:         $ENTRY_COUNT"
echo "  - STOP:          $STOP_COUNT"
echo "  - TAKE_PROFIT:   $TP_RECEIPT_COUNT"
echo ""

# Final verdict
if [ "$ISSUES" -eq 0 ] && [ "$ALERT_STATUS" = "executed" ]; then
  echo "=========================================="
  echo "✅ PASS: M6.5 E2E Audit Chain Validated"
  echo "=========================================="
  exit 0
elif [ "$ISSUES" -eq 0 ]; then
  echo "=========================================="
  echo "⚠️  PARTIAL: Audit chain complete but status is '$ALERT_STATUS'"
  echo "=========================================="
  exit 0
else
  echo "=========================================="
  echo "❌ FAIL: $ISSUES issue(s) found in audit chain"
  echo "=========================================="
  exit 1
fi
