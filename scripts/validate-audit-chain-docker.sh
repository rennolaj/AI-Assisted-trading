#!/usr/bin/env bash
set -euo pipefail

# M6.5 E2E Audit Chain Validation Script (Docker version)
# Validates the complete alert-to-execution audit chain in Docker postgres.
# Usage: ./validate-audit-chain-docker.sh <idempotency_key>

IDEMPOTENCY_KEY="${1:-}"

if [ -z "$IDEMPOTENCY_KEY" ]; then
  echo "Usage: $0 <idempotency_key>" >&2
  echo "Example: $0 smoke-1234567890" >&2
  exit 1
fi

echo "=========================================="
echo "M6.5 E2E Audit Chain Validation"
echo "=========================================="
echo "Idempotency Key: $IDEMPOTENCY_KEY"
echo "Database: Docker postgres container"
echo ""

# Helper function to run psql in Docker
psql_exec() {
  docker compose exec -T postgres psql -U postgres -d ai-trading-db -t -A -c "$1"
}

# Step 1: Get alert_id and processing status
echo "Step 1: Checking alert processing status..."
ALERT_DATA=$(psql_exec "SELECT alert_id, status FROM alert_processing WHERE idempotency_key = '$IDEMPOTENCY_KEY';")

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
  echo "   Audit chain validation will stop here (expected for rejected/failed alerts)."
  echo ""
  echo "=========================================="
  echo "✓ PARTIAL: Alert processed but not executed"
  echo "=========================================="
  exit 0
fi
echo ""

# Step 2: Check trade plan
echo "Step 2: Checking trade plan..."
PLAN_DATA=$(psql_exec "SELECT plan_id FROM trade_plan WHERE alert_id = '$ALERT_ID';")

if [ -z "$PLAN_DATA" ]; then
  echo "❌ FAIL: No trade plan found for alert $ALERT_ID"
  exit 1
fi

PLAN_ID=$(echo "$PLAN_DATA")
echo "   Plan ID: $PLAN_ID"

# Get trade plan details
PLAN_JSON=$(psql_exec "SELECT plan_json FROM trade_plan WHERE plan_id = '$PLAN_ID';")
SYMBOL=$(echo "$PLAN_JSON" | grep -o '"Symbol":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "N/A")
SIDE=$(echo "$PLAN_JSON" | grep -o '"Side":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "N/A")
QTY=$(echo "$PLAN_JSON" | grep -o '"Quantity":[0-9.]*' | head -1 | cut -d':' -f2 || echo "N/A")
TP_COUNT=$(echo "$PLAN_JSON" | grep -o '"Price":[0-9.]*' | wc -l | xargs)

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
INTENT_DATA=$(psql_exec "SELECT execution_id, mode, status FROM execution_intent WHERE plan_id = '$PLAN_ID';")

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
RECEIPT_COUNT=$(psql_exec "SELECT COUNT(*) FROM order_receipt WHERE execution_id = '$EXECUTION_ID';")

echo "   Total Receipts: $RECEIPT_COUNT"

if [ "$RECEIPT_COUNT" -eq 0 ]; then
  echo "❌ FAIL: No order receipts found for execution $EXECUTION_ID"
  exit 1
fi

# Count by order kind
echo ""
echo "   Receipt breakdown:"
RECEIPT_BREAKDOWN=$(psql_exec "SELECT order_kind, COUNT(*) FROM order_receipt WHERE execution_id = '$EXECUTION_ID' GROUP BY order_kind ORDER BY order_kind;")

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
HEARTBEAT_DATA=$(psql_exec "SELECT last_beat_utc, stale_threshold_seconds FROM execution_heartbeat WHERE service_name = 'execution-service';")

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
if [ "$ISSUES" -eq 0 ]; then
  echo "=========================================="
  echo "✅ PASS: M6.5 E2E Audit Chain Validated"
  echo "=========================================="
  exit 0
else
  echo "=========================================="
  echo "❌ FAIL: $ISSUES issue(s) found in audit chain"
  echo "=========================================="
  exit 1
fi
