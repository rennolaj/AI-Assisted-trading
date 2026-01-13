#!/usr/bin/env bash
# Full E2E Smoke Test Orchestration Script
# Runs a complete dataflow validation using fixture data, test matrix, and all existing scripts

set -euo pipefail

# --- CONFIG ---
FIXTURE_DIR="tests/fixtures/historical"
REQUIRED_FIXTURES=(pf_xbtusd_1m.json pf_xbtusd_5m.json pf_xbtusd_15m.json pf_xbtusd_30m.json pf_xbtusd_1h.json pf_xbtusd_4h.json)
TEST_MATRIX="docs/m9.2-test-matrix-20260108-11.csv" # <-- Change as needed
ENV_FILE=".env.smoke.fixtures"

# --- 1. Verify Fixture Data ---
echo "🔍 Verifying fixture data..."
for f in "${REQUIRED_FIXTURES[@]}"; do
  if [[ ! -f "$FIXTURE_DIR/$f" ]]; then
    echo "❌ Missing fixture: $FIXTURE_DIR/$f"
    exit 1
  fi
done
echo "✅ All required fixture files present."

# --- 2. Start All Services ---
echo "🚀 Starting all services (API, worker, db, redis, ngrok)..."
docker compose --env-file "$ENV_FILE" up -d --build
sleep 5
docker compose --env-file "$ENV_FILE" --profile ngrok up -d ngrok
sleep 10
echo "✅ Services started."


# --- 3. Extract Webhook Secret ---
WEBHOOK_SECRET=$(grep TRADINGVIEW_WEBHOOK_SECRET .env.smoke.fixtures | grep -v '^#' | cut -d= -f2- | tr -d '\r\n')
if [[ -z "$WEBHOOK_SECRET" ]]; then
  echo "❌ Could not extract TRADINGVIEW_WEBHOOK_SECRET from .env.smoke.fixtures"
  exit 1
fi

# --- 4. Run Test Matrix ---
echo "📤 Injecting all alerts from test matrix..."
./scripts/fixtures/run-test-matrix.sh "$TEST_MATRIX" "$WEBHOOK_SECRET"
echo "⏳ Waiting for all alerts to be processed (60s)..."
sleep 60

# --- 4. Capture LLM Decisions ---
echo "📥 Capturing LLM decisions for all test alerts..."
# Extract correlation IDs from the test matrix run (assuming script logs them)
CORR_IDS=$(docker exec ai-assisted-postgres-1 psql -U postgres -d ai-trading-db -t -c "SELECT correlation_id FROM llm_adjudications WHERE adjudicated_at_utc >= NOW() - INTERVAL '30 minutes';" | grep -Eo '[a-f0-9\-]+' | sort | uniq)
for cid in $CORR_IDS; do
  ./scripts/fixtures/capture-llm-decision.sh "$cid" || true
done
echo "✅ LLM decisions captured."

# --- 5. Summarize Results ---
echo "📊 Summarizing results..."
SUMMARY=$(docker exec ai-assisted-postgres-1 psql -U postgres -d ai-trading-db -t -c "SELECT decision, COUNT(*) FROM llm_adjudications WHERE adjudicated_at_utc >= NOW() - INTERVAL '30 minutes' GROUP BY decision;")
echo "$SUMMARY"

# --- 6. Generate Markdown Report ---
REPORT=docs/smoke-e2e-results-$(date +%Y%m%d-%H%M).md
echo "# Smoke E2E Test Results - $(date)" > "$REPORT"
echo "\n## Test Matrix: $TEST_MATRIX" >> "$REPORT"
echo "\n### LLM Decision Summary" >> "$REPORT"
echo '\n```' >> "$REPORT"
echo "$SUMMARY" >> "$REPORT"
echo '\n```' >> "$REPORT"
echo "\n### Details" >> "$REPORT"
docker exec ai-assisted-postgres-1 psql -U postgres -d ai-trading-db -c "SELECT * FROM llm_adjudications WHERE adjudicated_at_utc >= NOW() - INTERVAL '30 minutes';" >> "$REPORT"
echo "\n✅ Smoke E2E test complete. Results saved to $REPORT"
