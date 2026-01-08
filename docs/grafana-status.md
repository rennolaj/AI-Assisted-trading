# Grafana Configuration Status and Pending Tasks

## Current Status ❌ NOT FULLY WORKING

The Grafana dashboards are **not properly configured** in the Docker deployment. When you open Grafana, you won't see any dashboards.

---

## Problems Identified

### 1. ❌ Dashboard Mount Path Conflict
**Issue:** Docker-compose has conflicting volume mounts for Grafana dashboards.

**Current Configuration (WRONG):**
```yaml
volumes:
  - ./config/grafana/provisioning/datasources:/etc/grafana/provisioning/datasources:ro
  - ./config/grafana/provisioning/dashboards:/etc/grafana/provisioning/dashboards:ro
  - ./config/grafana/dashboards:/etc/grafana/provisioning/dashboards:ro  # ❌ OVERWRITES PREVIOUS LINE
  - grafana-data:/var/lib/grafana
```

**Problem:** The third mount overwrites the second mount, so the `dashboards.yml` provisioning file gets replaced by the JSON files.

**Expected Result:** Dashboards won't load because the provisioning configuration is missing.

### 2. ❌ Incorrect Path in dashboards.yml
**Issue:** The `dashboards.yml` points to the wrong path for JSON files.

**Current Configuration:**
```yaml
options:
  path: /etc/grafana/provisioning/dashboards  # Points to itself (wrong)
```

**Should Be:**
```yaml
options:
  path: /var/lib/grafana/dashboards  # Points to where JSON files are mounted
```

---

## Available Metrics (From Code)

The system exposes these metrics via Prometheus at `http://localhost:8080/metrics`:

### Counters (Total Counts)
1. **`trading_alerts_received_total`** - Total alerts received
   - Labels: `exchange`, `symbol`
2. **`trading_alerts_processed_total`** - Total alerts processed
   - Labels: `outcome`
3. **`trading_executions_processed_total`** - Total executions processed
   - Labels: `outcome`
4. **`trading_orders_placed_total`** - Total orders placed
   - Labels: `direction`, `order_type`
5. **`trading_orders_cancelled_total`** - Total orders cancelled
   - Labels: `reason`
6. **`trading_orders_filled_total`** - Total orders filled
   - Labels: `symbol`, `direction`
7. **`trading_stop_loss_triggered_total`** - Total stop-loss triggers
   - Labels: `symbol`
8. **`trading_take_profit_hit_total`** - Total take-profit hits
   - Labels: `symbol`, `target`
9. **`trading_errors_total`** - Total errors
   - Labels: `component`, `error_type`
10. **`trading_api_errors_total`** - Total API errors
    - Labels: `provider`, `endpoint`, `error_code`

### Histograms (Duration Measurements)
11. **`trading_alert_processing_duration_seconds`** - Alert processing duration
12. **`trading_execution_duration_seconds`** - Execution duration
    - Labels: `stage`

### Gauges (Current State)
13. **`trading_active_trades`** - Current active trades count
14. **`trading_queue_depth`** - Current queue depth
15. **`trading_reconciliation_discrepancies`** - Current unresolved discrepancies

---

## Existing Dashboards (Need Fixing)

### Dashboard 1: System Health (`system-health.json`)
**Panels:**
- Total Errors (1h)
- Reconciliation Discrepancies
- Stop Losses (1h)
- Take Profits Hit (1h)
- Error Rate by Component
- API Error Rate by Provider
- Top 10 Error Types
- Reconciliation Discrepancies Over Time
- Stop Loss Trigger Rate
- Active Trades Over Time

**Status:** ✅ JSON exists, ❌ Not loading in Grafana

### Dashboard 2: Alert Processing (`alert-processing.json`)
**Panels:**
- Alerts Received (1h)
- Queue Depth
- Alert Rate (per second)
- Alert Processing Outcomes
- Processing Duration Percentiles
- Alert Success Rate
- Rejection Breakdown

**Status:** ✅ JSON exists, ❌ Not loading in Grafana

### Dashboard 3: Execution & Orders (`execution-orders.json`)
**Panels:**
- Orders Placed (1h)
- Orders Filled (1h)
- Active Trades
- Execution Success Rate
- Orders by Direction
- Orders by Type
- Execution Outcomes
- Order Placement Rate
- Order Fill Rate
- Execution Duration

**Status:** ✅ JSON exists, ❌ Not loading in Grafana

---

## What Needs to Be Fixed

### Priority 1: Fix Docker Volume Mounts ⚠️

**File:** `docker-compose.yml`

**Change from:**
```yaml
grafana:
  volumes:
    - ./config/grafana/provisioning/datasources:/etc/grafana/provisioning/datasources:ro
    - ./config/grafana/provisioning/dashboards:/etc/grafana/provisioning/dashboards:ro
    - ./config/grafana/dashboards:/etc/grafana/provisioning/dashboards:ro  # ❌ REMOVE
    - grafana-data:/var/lib/grafana
```

**Change to:**
```yaml
grafana:
  volumes:
    - ./config/grafana/provisioning/datasources:/etc/grafana/provisioning/datasources:ro
    - ./config/grafana/provisioning/dashboards:/etc/grafana/provisioning/dashboards:ro
    - ./config/grafana/dashboards:/var/lib/grafana/dashboards:ro  # ✅ NEW PATH
    - grafana-data:/var/lib/grafana
```

### Priority 2: Fix Dashboard Provisioning Path ⚠️

**File:** `config/grafana/provisioning/dashboards/dashboards.yml`

**Change from:**
```yaml
options:
  path: /etc/grafana/provisioning/dashboards  # ❌ WRONG
```

**Change to:**
```yaml
options:
  path: /var/lib/grafana/dashboards  # ✅ CORRECT
```

### Priority 3: Add Overview Dashboard (Optional) 💡

Create a new `overview.json` dashboard that shows:
- All key metrics on one page
- System status at a glance
- Quick navigation to detailed dashboards

---

## How to Verify After Fix

### 1. Rebuild and Start Grafana
```bash
docker compose down
docker compose up --build -d grafana
```

### 2. Access Grafana
- URL: http://localhost:3000
- Username: `admin`
- Password: `admin` (from docker-compose.yml)

### 3. Check Dashboards
Navigate to: **Dashboards → Browse → Trading System** folder

You should see:
- ✅ Alert Processing
- ✅ Execution & Orders
- ✅ System Health

### 4. Verify Metrics Are Loading
- Open any dashboard
- Panels should show data (not "No data")
- If no data appears, check:
  - Prometheus is running: http://localhost:9090
  - Metrics endpoint: http://localhost:8080/metrics
  - System has processed some alerts

---

## Test Data Generation

To see metrics in dashboards, you need to process alerts:

```bash
# Run smoke test to generate test data
./scripts/smoke.sh

# Or send test webhook
curl -X POST http://localhost:8080/webhooks/tradingview/alert \
  -H "Content-Type: application/json" \
  -H "X-Webhook-Secret: your-secret" \
  -d '{"exchange":"kraken","symbol":"PF_XBTUSD","action":"LONG",...}'
```

---

## Summary

### What Works ✅
- Prometheus datasource configured
- 3 dashboard JSON files created with proper queries
- 15 metrics exposed by the application
- Metrics scraping by Prometheus

### What's Broken ❌
- Dashboard JSON files not loading in Grafana
- Volume mount conflict in docker-compose.yml
- Wrong path in dashboards.yml provisioning file

### What's Needed 🔧
1. Fix docker-compose.yml volume mounts (1 line change)
2. Fix dashboards.yml path (1 line change)
3. Restart Grafana container
4. Verify dashboards appear in UI

**Estimated Fix Time:** 2 minutes
**Testing Time:** 5 minutes with smoke test

---

## After Fix - Expected Result

When you open Grafana at http://localhost:3000 after the fix:

1. **Login:** admin / admin
2. **Navigate:** Dashboards → Browse
3. **See:** "Trading System" folder with 3 dashboards
4. **Open:** Any dashboard shows live metrics
5. **Verify:** All panels load data from Prometheus

The system will be fully operational for monitoring trading activity!
