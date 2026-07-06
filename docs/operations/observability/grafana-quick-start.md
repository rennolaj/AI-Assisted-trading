# Grafana Dashboard Quick Start Guide

## Starting the System

```bash
# Start all services including Grafana
docker compose up -d

# Verify services are running
docker compose ps

# Check if metrics are being scraped
curl http://localhost:8080/metrics | head -20
curl http://localhost:9464/metrics | head -20
```

## Accessing Grafana

1. **Open browser**: http://localhost:3000
2. **Login credentials**:
   - Username: `admin`
   - Password: `admin`
3. **Change password** (optional but recommended)

## Finding Your Dashboards

### Method 1: Via Sidebar
1. Click **Dashboards** (4-squares icon) in left sidebar
2. Click on **Trading System** folder
3. Select any dashboard:
   - Alert Processing Dashboard
   - Execution & Orders Dashboard
   - System Health Dashboard

### Method 2: Via Search
1. Press `/` (forward slash) or click search box
2. Type dashboard name (e.g., "alert")
3. Press Enter

### Method 3: Via Home
1. Click **Home** at top-left
2. Browse **Recently viewed dashboards** or **Starred dashboards**

## Dashboard Overview

### 📊 Alert Processing Dashboard
**What it shows:**
- Real-time alert ingestion rate
- Queue depth monitoring
- Alert acceptance vs rejection rates
- Processing latency (p50, p95, p99)
- Rejection reason breakdown

**Key metrics:**
- `alerts_received_total` - Alerts from TradingView
- `alerts_processed_total` - Processing outcomes
- `alert_processing_duration_seconds` - End-to-end latency
- `queue_depth` - Redis queue size

**When to use:**
- Troubleshooting alert pipeline
- Monitoring system load
- Identifying rejection patterns
- Performance tuning

---

### 📈 Execution & Orders Dashboard
**What it shows:**
- Order placement and fill statistics
- Active trade count
- Execution success rates
- Order type distribution (LIMIT, MARKET, STP, TAKE_PROFIT)
- Direction breakdown (LONG vs SHORT)
- Execution latency

**Key metrics:**
- `orders_placed_total` - Orders sent to exchange
- `orders_filled_total` - Successfully filled orders
- `active_trades` - Current open positions
- `executions_processed_total` - Execution outcomes
- `execution_duration_seconds` - Execution latency

**When to use:**
- Monitoring trading activity
- Analyzing order flow
- Tracking fill rates
- Measuring execution performance

---

### 🏥 System Health Dashboard
**What it shows:**
- System-wide error rates
- Reconciliation status
- Stop-loss and take-profit triggers
- Error breakdown by component
- API error rates by provider

**Key metrics:**
- `errors_total` - Application errors
- `api_errors_total` - External API failures
- `reconciliation_discrepancies` - State mismatches
- `stop_loss_triggered_total` - Risk management events
- `take_profit_hit_total` - Profit-taking events

**When to use:**
- Health monitoring
- Error investigation
- Reconciliation verification
- Risk management analysis

## Interactive Features

### Time Range Selection
- **Top-right corner**: Click time range (e.g., "Last 1 hour")
- **Quick ranges**: Last 5m, 15m, 1h, 6h, 24h, 7d, 30d
- **Custom range**: Select "Custom time range"
- **Relative time**: "now-1h" to "now"
- **Absolute time**: Pick specific dates

### Auto-Refresh
- **Top-right corner**: Click refresh dropdown
- **Options**: Off, 5s, 10s, 30s, 1m, 5m, 15m, 30m, 1h
- **Default**: 5 seconds (real-time monitoring)
- **Pause**: Click pause button to stop auto-refresh

### Panel Interactions
- **Zoom**: Click and drag on time series graph
- **Legend**: Click series name to show/hide
- **Tooltip**: Hover over graph for details
- **Full screen**: Click panel title → View → Full screen (Esc to exit)
- **Inspect**: Click panel title → Inspect → Query/Data/Panel JSON

### Filtering and Drilling Down
1. **Click on legend item**: Show only that series
2. **Shift + Click**: Add series to selection
3. **Click on pie slice**: Filter other panels (if linked)
4. **Use variables**: Add template variables for symbol, exchange, etc.

## Customizing Dashboards

### Edit a Panel
1. Click panel title → Edit
2. Modify query in **Query** tab
3. Adjust visualization in **Panel options**
4. Click **Apply** to save changes
5. Click **Save dashboard** (top-right) to persist

### Add a Panel
1. Click **Add panel** icon (top-right)
2. Choose panel type (Time series, Stat, Gauge, etc.)
3. Write Prometheus query
4. Configure visualization options
5. Click **Apply**
6. Save dashboard

### Example: Add "API Calls per Minute" Panel
```promql
# Query
rate(api_calls_total[1m]) * 60

# Panel type: Time series
# Unit: Requests/min
# Legend: {{provider}} - {{endpoint}}
```

## Creating Alerts

### In Grafana (Alert Rules)
1. Navigate to **Alerting** (bell icon) → **Alert rules**
2. Click **New alert rule**
3. Define query and condition
4. Set evaluation interval
5. Configure notification channels
6. Save alert

### Example Alert: High Queue Depth
```
Alert name: High Alert Queue Depth
Query: queue_depth
Condition: IS ABOVE 100
For: 10m
Message: Alert queue has {{$value}} items pending
```

## Sharing Dashboards

### Create Dashboard Link
1. Click **Share** icon (top-right)
2. Choose **Link** tab
3. Toggle options (current time range, theme)
4. Copy link
5. Share with team

### Create Snapshot
1. Click **Share** icon
2. Choose **Snapshot** tab
3. Set expiration time
4. Click **Publish to snapshots.raintank.io**
5. Share snapshot URL

### Export Dashboard
1. Click **Share** icon
2. Choose **Export** tab
3. Click **Save to file**
4. Share JSON file

## Troubleshooting

### "No data" in panels
**Check:**
1. Time range - is it too far in the past?
2. Are services generating metrics? Check endpoints:
   - API: http://localhost:8080/metrics
   - Worker: http://localhost:9464/metrics
3. Is Prometheus scraping? Check http://localhost:9090/targets
4. Do metrics exist in Prometheus? Query in http://localhost:9090

### Dashboard not loading
**Check:**
1. Grafana logs: `docker compose logs grafana`
2. Dashboard JSON syntax (must be valid)
3. Provisioning config: `docker compose exec grafana cat /etc/grafana/provisioning/dashboards/dashboards.yml`

### Slow dashboard performance
**Solutions:**
1. Reduce time range (e.g., 1h instead of 24h)
2. Increase refresh interval (e.g., 30s instead of 5s)
3. Simplify queries (use recording rules in Prometheus)
4. Limit number of series (use topk() or filtering)

### Metrics missing labels
**Check:**
1. Metric is being recorded with correct labels
2. Label names match in query (case-sensitive)
3. Check raw metric in Prometheus: http://localhost:9090/graph

## Best Practices

### For Real-Time Monitoring
- Use 5-10 second refresh
- Keep time range to last 15m-1h
- Use stat panels for key metrics
- Configure alerts for critical thresholds

### For Analysis and Debugging
- Use longer time ranges (6h-24h)
- Disable auto-refresh
- Use table panels for detailed data
- Export data for offline analysis

### For Presentations
- Use TV mode (click dashboard settings → View mode → TV)
- Set appropriate refresh for audience
- Use kiosk mode for fullscreen (append `?kiosk` to URL)
- Star important dashboards

## Useful Keyboard Shortcuts

- `/` or `s` - Open search
- `d` + `h` - Go to home
- `Esc` - Exit fullscreen or edit mode
- `t` + `z` - Zoom out time range
- `Ctrl/Cmd + S` - Save dashboard
- `?` - Show all shortcuts

## Next Steps

1. **Generate some test data**: Run the smoke test or seed trades
   ```bash
   ./scripts/smoke.sh
   ```

2. **Watch metrics in real-time**: Open all 3 dashboards in separate tabs

3. **Set up alerts**: Configure Grafana alerts or Prometheus alerting rules

4. **Customize for your needs**: Add panels, create new dashboards, adjust queries

5. **Share with team**: Export dashboards or create shared links

## Support

- **Metrics Reference**: See `docs/operations/observability/m7.3-metrics-guide.md`
- **Dashboard README**: See `config/grafana/dashboards/README.md`
- **Prometheus Queries**: See `docs/operations/observability/m7.3-metrics-guide.md` for examples
- **Grafana Docs**: https://grafana.com/docs/grafana/latest/

Happy monitoring! 📊
