# Grafana Dashboards

This directory contains pre-configured Grafana dashboards for the MVP Trading system.

## Available Dashboards

### 1. Alert Processing Dashboard
**UID**: `alert-processing`

Monitors the alert ingestion and processing pipeline.

**Panels:**
- **Alerts Received (1h)**: Total alerts received in the last hour
- **Queue Depth**: Current number of alerts in Redis queue
- **Alert Rate**: Alerts per second by symbol
- **Alert Processing Outcomes**: Pie chart showing accepted vs rejected breakdown
- **Processing Duration Percentiles**: p50, p95, p99 latency gauges
- **Alert Success Rate**: Percentage of alerts that result in execution
- **Rejection Breakdown**: Stacked area chart of rejection reasons over time

**Use Cases:**
- Monitor alert ingestion rate and queue health
- Track alert acceptance/rejection rates
- Identify processing bottlenecks
- Debug rejection patterns

---

### 2. Execution & Orders Dashboard
**UID**: `execution-orders`

Tracks order execution, placement, and fills.

**Panels:**
- **Orders Placed (1h)**: Total orders placed in the last hour
- **Orders Filled (1h)**: Total orders filled in the last hour
- **Active Trades**: Current number of open positions
- **Execution Success Rate**: Percentage of successful executions
- **Orders by Direction**: Pie chart of LONG vs SHORT orders
- **Orders by Type**: Pie chart of LIMIT, MARKET, STP, TAKE_PROFIT orders
- **Execution Outcomes**: Pie chart of filled, placed, rejected outcomes
- **Order Placement Rate**: Orders per second by type and direction
- **Order Fill Rate**: Fills per second by symbol and direction
- **Execution Duration**: p95, p99, and average execution time

**Use Cases:**
- Monitor order flow and execution performance
- Track fill rates and slippage
- Analyze execution latency
- Verify order type distribution

---

### 3. System Health Dashboard
**UID**: `system-health`

Monitors system errors, reconciliation, and risk management.

**Panels:**
- **Total Errors (1h)**: Total system errors in the last hour
- **Reconciliation Discrepancies**: Current unresolved discrepancies
- **Stop Losses (1h)**: Total stop-loss triggers in the last hour
- **Take Profits Hit (1h)**: Total take-profit hits in the last hour
- **Error Rate by Component**: Errors per second by component (AlertWorker, ExecutionService, etc.)
- **API Error Rate by Provider**: API errors per second by provider (kraken, openai, etc.)
- **Top 10 Error Types**: Table of most common exceptions
- **Reconciliation Discrepancies Over Time**: Time series of discrepancy count
- **Stop Loss Trigger Rate**: Stop losses per second by symbol
- **Active Trades Over Time**: Historical view of open positions

**Use Cases:**
- Monitor system health and error rates
- Identify failing components
- Track reconciliation status
- Analyze stop-loss and take-profit effectiveness

---

## Dashboard Provisioning

Dashboards are automatically loaded into Grafana on startup via the provisioning system.

**Configuration files:**
- `config/grafana/provisioning/dashboards/dashboards.yml` - Provisioning config
- `config/grafana/dashboards/*.json` - Dashboard JSON definitions

**Provisioning settings:**
- Folder: `Trading System`
- Update interval: 10 seconds
- UI updates: Allowed (dashboards can be edited in Grafana UI)
- Deletion: Allowed

---

## Accessing Dashboards

1. Start the system:
   ```bash
   docker compose up -d
   ```

2. Open Grafana:
   ```
   http://localhost:3000
   ```

3. Login with default credentials:
   - Username: `admin`
   - Password: `admin`

4. Navigate to:
   - **Home** → **Dashboards** → **Trading System** folder
   - Or use the search: press `/` and type dashboard name

---

## Customizing Dashboards

### In Grafana UI
1. Open a dashboard
2. Click the gear icon (⚙️) → **Settings**
3. Make your changes
4. Click **Save dashboard**
5. Changes are persisted in the Grafana volume

### Exporting Dashboards
1. Open a dashboard
2. Click the share icon → **Export**
3. Select **Save to file**
4. Copy the JSON to `config/grafana/dashboards/`

### Importing Dashboards
1. Click **+** → **Import dashboard**
2. Upload JSON file or paste JSON
3. Select Prometheus datasource
4. Click **Import**

---

## Common Queries

### Alert Processing
```promql
# Alert success rate
sum(rate(alerts_processed_total{outcome="accepted"}[5m])) / sum(rate(alerts_processed_total[5m]))

# Average processing time
rate(alert_processing_duration_seconds_sum[5m]) / rate(alert_processing_duration_seconds_count[5m])

# Queue depth alert (triggers when > 100)
queue_depth > 100
```

### Execution & Orders
```promql
# Order fill rate
sum(increase(orders_filled_total[1h])) / sum(increase(orders_placed_total[1h]))

# Execution latency p99
histogram_quantile(0.99, rate(execution_duration_seconds_bucket{stage="total"}[5m]))

# Active positions
active_trades
```

### System Health
```promql
# Total error rate
sum(rate(errors_total[5m]))

# Errors by component
sum by (component) (rate(errors_total[5m]))

# Reconciliation health
reconciliation_discrepancies == 0
```

---

## Alerting Rules

Example Prometheus alerting rules to add to `config/prometheus.yml`:

```yaml
groups:
  - name: trading_alerts
    interval: 30s
    rules:
      - alert: HighErrorRate
        expr: sum(rate(errors_total[5m])) > 1
        for: 5m
        annotations:
          summary: "High error rate detected"
          description: "Error rate is {{ $value }} errors/sec"

      - alert: HighQueueDepth
        expr: queue_depth > 100
        for: 10m
        annotations:
          summary: "Alert queue depth is high"
          description: "Queue depth is {{ $value }} alerts"

      - alert: ReconciliationDiscrepancy
        expr: reconciliation_discrepancies > 0
        for: 15m
        annotations:
          summary: "Reconciliation discrepancies detected"
          description: "{{ $value }} unresolved discrepancies"

      - alert: LowExecutionSuccessRate
        expr: sum(rate(executions_processed_total{outcome=~"filled|placed"}[5m])) / sum(rate(executions_processed_total[5m])) < 0.8
        for: 10m
        annotations:
          summary: "Execution success rate below 80%"
          description: "Success rate is {{ $value | humanizePercentage }}"
```

---

## Refresh Rates

All dashboards are configured with:
- **Auto-refresh**: 5 seconds
- **Time range**: Last 1 hour (adjustable)

To change refresh rate:
1. Open dashboard
2. Click the refresh dropdown (top-right)
3. Select desired interval

---

## Troubleshooting

### Dashboard not appearing
1. Check Grafana logs:
   ```bash
   docker compose logs grafana
   ```
2. Verify provisioning path is mounted:
   ```bash
   docker compose exec grafana ls -la /etc/grafana/provisioning/dashboards
   ```
3. Check dashboard JSON syntax (must be valid JSON)

### Metrics not showing
1. Verify Prometheus is scraping:
   - Open http://localhost:9090/targets
   - Both `mvp-trading-api` and `mvp-trading-worker` should be UP
2. Check if metrics exist in Prometheus:
   - Open http://localhost:9090/graph
   - Query: `alerts_received_total`
3. Verify datasource in Grafana:
   - **Configuration** → **Data sources** → **Prometheus**
   - Click **Test** button

### Panel shows "No data"
1. Check time range (top-right corner)
2. Verify metric name in panel query
3. Check if metric has data in Prometheus
4. Ensure label filters match actual labels

---

## Related Documentation
- `docs/m7.3-metrics-guide.md` - Complete metrics reference
- `docs/m7.3-implementation-plan.md` - Implementation details
- `README.md` - Observability section
