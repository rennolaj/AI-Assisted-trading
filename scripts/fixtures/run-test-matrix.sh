#!/usr/bin/env bash
# Run a batch of test cases from a test matrix CSV file
# CSV format: timestamp,symbol,direction,close_price,interval,notes

set -euo pipefail

TEST_MATRIX_FILE="${1:?Error: Test matrix CSV file required}"
WEBHOOK_SECRET="${2:-${TRADINGVIEW_WEBHOOK_SECRET:-}}"
API_URL="${3:-http://localhost:8080/api/v1/tradingview/webhook}"
DELAY_SECONDS="${4:-60}"  # Wait time between tests (default: 60s)

if [[ -z "$WEBHOOK_SECRET" ]]; then
  echo "Error: Webhook secret not provided. Set TRADINGVIEW_WEBHOOK_SECRET env var or pass as 2nd argument." >&2
  exit 1
fi

if [[ ! -f "$TEST_MATRIX_FILE" ]]; then
  echo "Error: Test matrix file not found: $TEST_MATRIX_FILE" >&2
  exit 1
fi

echo "========================================" >&2
echo "Test Matrix Execution" >&2
echo "========================================" >&2
echo "Matrix file: $TEST_MATRIX_FILE" >&2
echo "API URL: $API_URL" >&2
echo "Delay between tests: ${DELAY_SECONDS}s" >&2
echo "" >&2

# Count total tests (skip header)
TOTAL_TESTS=$(tail -n +2 "$TEST_MATRIX_FILE" | wc -l | tr -d ' ')
echo "Total test cases: $TOTAL_TESTS" >&2
echo "" >&2

TEST_NUM=0
PASSED=0
FAILED=0

# Read CSV file (skip header)
tail -n +2 "$TEST_MATRIX_FILE" | while IFS=',' read -r timestamp symbol direction close_price interval notes; do
  TEST_NUM=$((TEST_NUM + 1))
  
  echo "========================================" >&2
  echo "Test $TEST_NUM of $TOTAL_TESTS" >&2
  echo "========================================" >&2
  echo "Timestamp: $timestamp" >&2
  echo "Symbol: $symbol" >&2
  echo "Direction: $direction" >&2
  echo "Close: $close_price" >&2
  echo "Interval: $interval" >&2
  echo "Notes: $notes" >&2
  echo "" >&2
  
  # Inject the alert
  if ./scripts/fixtures/inject-historical-alert.sh \
      "$timestamp" "$symbol" "$direction" "$close_price" "$interval" \
      "krakenfutures" "$API_URL" "$WEBHOOK_SECRET"; then
    echo "✅ Test $TEST_NUM passed" >&2
    PASSED=$((PASSED + 1))
  else
    echo "❌ Test $TEST_NUM failed" >&2
    FAILED=$((FAILED + 1))
  fi
  
  # Wait before next test (unless it's the last one)
  if [[ $TEST_NUM -lt $TOTAL_TESTS ]]; then
    echo "" >&2
    echo "Waiting ${DELAY_SECONDS}s for processing to complete..." >&2
    sleep "$DELAY_SECONDS"
    echo "" >&2
  fi
done

echo "" >&2
echo "========================================" >&2
echo "Test Execution Summary" >&2
echo "========================================" >&2
echo "Total: $TOTAL_TESTS" >&2
echo "Passed: $PASSED" >&2
echo "Failed: $FAILED" >&2
echo "" >&2

if [[ $FAILED -gt 0 ]]; then
  exit 1
fi
