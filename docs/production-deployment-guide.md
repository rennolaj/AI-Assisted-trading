# Production Deployment Guide

Complete step-by-step guide for deploying the AI-Assisted Trading system to production using Docker containers.

## Prerequisites

✅ **Required before starting:**
- Docker and Docker Compose installed
- Production credentials configured in `.env.prod.local`
- TradingView alerts configured (if using live alerts)
- Local LLM server running (if using `MCP_PROVIDER=local`)

## Step 1: Stop Existing Containers

Stop any running containers to ensure a clean slate:

```bash
docker compose down
```

**Expected output:**
```
[+] Running 7/7
 ✔ Container ai-assisted-grafana-1     Removed
 ✔ Container ai-assisted-prometheus-1  Removed
 ✔ Container ai-assisted-api-1        Removed
 ✔ Container ai-assisted-worker-1     Removed
 ✔ Container ai-assisted-postgres-1   Removed
 ✔ Container ai-assisted-redis-1      Removed
 ✔ Network ai-assisted_default        Removed
```

## Step 2: Build Fresh Images

Build Docker images with production configuration:

```bash
docker compose --env-file .env.prod.local build --no-cache
```

**What this does:**
- Builds API and Worker images from scratch (no cached layers)
- Uses production environment variables from `.env.prod.local`
- Compiles .NET application with Release configuration
- Takes ~2-3 minutes on first build

**Expected output:**
```
[+] Building 120.5s (25/25) FINISHED
 => [api internal] load build definition
 => [worker internal] load build definition
 ...
 => exporting to image
```

## Step 3: Start All Services

Start containers in detached mode:

```bash
docker compose --env-file .env.prod.local up -d
```

**What this does:**
- Starts 7 containers: postgres, redis, api, worker, prometheus, grafana, ngrok
- Creates Docker network for inter-container communication
- Mounts volumes for persistent data
- Takes ~10-15 seconds to start all containers

**Expected output:**
```
[+] Running 7/7
 ✔ Container ai-assisted-postgres-1    Started
 ✔ Container ai-assisted-redis-1       Started
 ✔ Container ai-assisted-api-1         Started
 ✔ Container ai-assisted-worker-1      Started
 ✔ Container ai-assisted-prometheus-1  Started
 ✔ Container ai-assisted-grafana-1     Started
 ✔ Container ai-assisted-ngrok-1       Started
```

## Step 4: Wait for Database Initialization

PostgreSQL needs time to initialize and create tables:

```bash
# Check PostgreSQL readiness
docker exec ai-assisted-postgres-1 pg_isready -U postgres

# Verify database exists
docker exec ai-assisted-postgres-1 psql -U postgres -c "\l" | grep ai-trading-db

# Count tables (should see 17 tables)
docker exec ai-assisted-postgres-1 psql -U postgres -d ai-trading-db -c "\dt" | wc -l
```

**Expected output:**
```
/var/run/postgresql:5432 - accepting connections
 ai-trading-db | postgres | UTF8     | ...
18  # (17 tables + header line)
```

**17 Required Tables:**
1. `alerts`
2. `alert_processing`
3. `open_trades`
4. `indicator_snapshots`
5. `elliott_candidates`
6. `trade_plan`
7. `execution_intent`
8. `order_receipt`
9. `fill_receipt`
10. `reconciliation_state`
11. `reconciliation_discrepancy`
12. `daily_risk`
13. `execution_heartbeat`
14. `system_state`
15. `kill_switch_audit`
16. `llm_adjudications`
17. `idempotency_keys`

⏱️ **Wait time:** Database initialization typically takes 5-10 seconds.

## Step 5: Verify Service Health

### Check Container Status
```bash
docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"
```

**Expected output:**
```
NAMES                        STATUS              PORTS
ai-assisted-ngrok-1         Up 30 seconds       0.0.0.0:4040->4040/tcp
ai-assisted-grafana-1       Up 30 seconds       0.0.0.0:3000->3000/tcp
ai-assisted-prometheus-1    Up 30 seconds       0.0.0.0:9090->9090/tcp
ai-assisted-worker-1        Up 30 seconds       0.0.0.0:9464->9464/tcp
ai-assisted-api-1           Up 30 seconds       0.0.0.0:8080->8080/tcp
ai-assisted-redis-1         Up 35 seconds       0.0.0.0:6379->6379/tcp
ai-assisted-postgres-1      Up 35 seconds       0.0.0.0:5432->5432/tcp
```

### Check Redis Connectivity
```bash
docker exec ai-assisted-redis-1 redis-cli PING
```

**Expected:** `PONG`

### Check API Health
```bash
curl -s http://localhost:8080/health | jq .
```

**Expected output:**
```json
{
  "status": "ok"
}
```

### Check API Dependencies
```bash
curl -s http://localhost:8080/health/dependencies | jq .
```

**Expected output:**
```json
{
  "postgres": "healthy",
  "redis": "healthy"
}
```

### Get ngrok Webhook URL
```bash
curl -s http://localhost:4040/api/tunnels | jq -r '.tunnels[0].public_url'
```

**Example output:**
```
https://raptureless-nathaniel-roastable.ngrok-free.dev
```

**Your webhook URL will be:**
```
https://YOUR-SUBDOMAIN.ngrok-free.dev/api/v1/tradingview/webhook
```

## Step 6: Test Alert Processing

Send a test alert to verify the complete pipeline:

```bash
curl -X POST http://localhost:8080/api/v1/tradingview/webhook \
  -H "Content-Type: application/json" \
  -d '{
    "secret": "YOUR_WEBHOOK_SECRET",
    "symbol": "BTCUSD.P",
    "exchange": "krakenfutures",
    "interval": "15",
    "direction": "LONG",
    "close": 95000
  }'
```

**Expected response:**
```json
{
  "status": "queued",
  "correlationId": "uuid-here"
}
```

### Monitor Worker Processing
```bash
docker logs ai-assisted-worker-1 --follow --tail 50
```

**Look for these log entries:**
```
info: Mvp.Trading.Worker.AlertWorker[0]
      Processing alert: BTCUSD.P LONG at 95000
info: Mvp.Trading.Worker.AlertWorker[0]
      Alert processed successfully
```

## Step 7: Monitor System

### View Live Logs

**Worker logs (alert processing):**
```bash
docker logs ai-assisted-worker-1 --follow --tail 50
```

**API logs:**
```bash
docker logs ai-assisted-api-1 --follow --tail 50
```

**All services:**
```bash
docker compose --env-file .env.prod.local logs -f
```

### Check Prometheus Metrics

**API metrics:**
```bash
curl -s http://localhost:8080/metrics | grep alerts_received_total
```

**Worker metrics:**
```bash
curl -s http://localhost:9464/metrics | grep alerts_processed_total
```

### Query Database State

**Recent alerts:**
```bash
docker exec ai-assisted-postgres-1 psql -U postgres -d ai-trading-db -c "
SELECT alert_id, symbol, direction, close_price, received_at 
FROM alerts 
ORDER BY received_at DESC 
LIMIT 10;"
```

**LLM adjudication statistics:**
```bash
docker exec ai-assisted-postgres-1 psql -U postgres -d ai-trading-db -c "
SELECT decision, COUNT(*) as count 
FROM llm_adjudications 
GROUP BY decision 
ORDER BY count DESC;"
```

**Open trades:**
```bash
docker exec ai-assisted-postgres-1 psql -U postgres -d ai-trading-db -c "
SELECT exchange_id, symbol, side, entry_price, invalidation_price 
FROM open_trades;"
```

## Port Mappings

| Service    | Container Port | Host Port | Purpose                          |
|------------|----------------|-----------|----------------------------------|
| Postgres   | 5432           | 5432      | Database connections             |
| Redis      | 6379           | 6379      | Queue and cache                  |
| API        | 8080           | 8080      | HTTP API + Webhooks              |
| Worker     | 9464           | 9464      | Prometheus metrics (worker)      |
| Prometheus | 9090           | 9090      | Metrics collection UI            |
| Grafana    | 3000           | 3000      | Dashboard visualization          |
| ngrok      | 4040           | 4040      | Tunnel inspection UI             |

## TradingView Webhook Configuration

### Step 1: Get Your ngrok URL
```bash
curl -s http://localhost:4040/api/tunnels | jq -r '.tunnels[0].public_url'
```

### Step 2: Configure Alert in TradingView

1. Open your chart with the Elliott Wave indicator
2. Click the "Alert" button (clock icon)
3. Set condition: "Elliott Wave MVP v1 - Any alert() function call"
4. In **Webhook URL**, enter:
   ```
   https://YOUR-SUBDOMAIN.ngrok-free.dev/api/v1/tradingview/webhook
   ```
5. In **Message**, paste:
   ```json
   {
     "secret": "{{strategy.alert_message}}",
     "symbol": "{{ticker}}",
     "exchange": "{{exchange}}",
     "interval": "{{interval}}",
     "direction": "{{strategy.order.action}}",
     "close": {{close}}
   }
   ```
6. Click "Create"

### Step 3: Verify Webhook Reception

Monitor worker logs to see incoming alerts:
```bash
docker logs ai-assisted-worker-1 --follow | grep "Processing alert"
```

## Environment Configuration

### Key Settings in `.env.prod.local`

**Kraken Futures (Production):**
```bash
KRAKEN_FUTURES_ENV=prod
KRAKEN_FUTURES_PROD_API_KEY=your_prod_key
KRAKEN_FUTURES_PROD_API_SECRET=your_prod_secret
```

**LLM Provider:**
```bash
# Option 1: OpenAI
MCP_PROVIDER=openai
OPENAI_API_KEY=your_openai_key

# Option 2: Local LLM (e.g., LM Studio)
MCP_PROVIDER=local
LOCAL_LLM_BASE_URL=http://host.docker.internal:1234/v1/
LOCAL_LLM_MODE=chat

# Option 3: Auto-fallback (OpenAI with local fallback on rate limits)
MCP_PROVIDER=auto
OPENAI_API_KEY=your_openai_key
LOCAL_LLM_BASE_URL=http://host.docker.internal:1234/v1/
```

**Note:** When using a local LLM server, `LOCAL_LLM_BASE_URL` should point to your LLM server endpoint. Common patterns:
- LM Studio on same host: `http://host.docker.internal:1234/v1/`
- LM Studio on different machine: `http://<your-llm-host>:<port>/v1/`
- Ollama: `http://host.docker.internal:11434/v1/`

**Kill Switch Protection:**
```bash
KILL_SWITCH_SECRET=your_secure_secret
```

**TradingView Webhook:**
```bash
TRADINGVIEW_WEBHOOK_SECRET=your_webhook_secret
```

## Troubleshooting

### Container Won't Start
```bash
# Check container logs
docker logs ai-assisted-worker-1

# Check all container status
docker compose --env-file .env.prod.local ps
```

### Database Connection Issues
```bash
# Verify Postgres is accepting connections
docker exec ai-assisted-postgres-1 pg_isready

# Check connection from API container
docker exec ai-assisted-api-1 nc -zv postgres 5432
```

### Redis Connection Issues
```bash
# Test Redis connectivity
docker exec ai-assisted-redis-1 redis-cli PING

# Check from API container
docker exec ai-assisted-api-1 nc -zv redis 6379
```

### LLM Provider Issues

**OpenAI 429 (Rate Limit):**
- Switch to `MCP_PROVIDER=local` or `MCP_PROVIDER=auto`
- Check API key quota in OpenAI dashboard

**Local LLM Connection Failed:**
- Verify LLM server is running and accessible
- Test connection: `curl http://<your-llm-host>:<port>/v1/models`
- Check `LOCAL_LLM_BASE_URL` in `.env.prod.local`
- For same-host LM Studio: Use `host.docker.internal` to access host machine from container

**LLM Decision Rejected:**
- Check worker logs for rejection reason
- Query database: `SELECT * FROM llm_adjudications WHERE decision='REJECT' ORDER BY time DESC LIMIT 5;`
- Review captured context in `validation_errors` column

### Alerts Not Processing
```bash
# Check Redis queue depth
docker exec ai-assisted-redis-1 redis-cli LLEN mvp:alerts

# Monitor worker logs
docker logs ai-assisted-worker-1 --follow

# Verify kill switch is not active
curl http://localhost:8080/api/killswitch/status
```

### ngrok Tunnel Issues
```bash
# Check ngrok status
docker logs ai-assisted-ngrok-1

# Verify tunnel is active
curl http://localhost:4040/api/tunnels

# Restart ngrok container
docker restart ai-assisted-ngrok-1
```

## Stopping the System

### Graceful Shutdown
```bash
docker compose --env-file .env.prod.local down
```

### Emergency Stop (Preserve Data)
```bash
# Stop processing but keep containers running
curl -X POST http://localhost:8080/api/killswitch/activate \
  -H "Content-Type: application/json" \
  -d '{
    "secret": "YOUR_KILL_SWITCH_SECRET",
    "level": "EMERGENCY_STOP",
    "reason": "Manual shutdown",
    "activatedBy": "operator"
  }'
```

### Complete Cleanup (Including Volumes)
```bash
docker compose --env-file .env.prod.local down -v
```

⚠️ **Warning:** The `-v` flag removes all persistent data including database contents.

## Next Steps

1. **Monitor for 2 Hours:** Watch logs and metrics to ensure stable operation
2. **Review LLM Decisions:** Query `llm_adjudications` table for decision quality
3. **Configure Grafana Dashboards:** Create custom visualizations at http://localhost:3000
4. **Set Up Alerts:** Configure Prometheus alerting rules for critical conditions
5. **Test Kill Switch:** Verify emergency controls work as expected
6. **Document ngrok URL:** Save your webhook URL for TradingView configuration

## Related Documentation

- [Environment Files Guide](environment-files.md) - Complete environment variable reference
- [Kill Switch Operations](m7.2-kill-switch-operations.md) - Emergency control procedures
- [Metrics Guide](m7.3-metrics-guide.md) - Complete metrics catalog
- [Local LLM Options](local-llm-options.md) - Supported LLM runtimes and models
- [Reconciliation System](m7-low-level-requirements.md) - Order state monitoring
