# Command Reference Manual

Quick reference for all common development, testing, deployment, and monitoring operations.

> **Windows Users:** PowerShell versions of all scripts are available. See [scripts/WINDOWS.md](../scripts/WINDOWS.md) for detailed Windows instructions.

---

## Table of Contents
1. [Development Workflow](#development-workflow)
2. [Docker Operations](#docker-operations)
3. [Environment Switching](#environment-switching)
4. [Testing](#testing)
5. [Smoke Testing](#smoke-testing)
6. [Monitoring & Observability](#monitoring--observability)
7. [Database Operations](#database-operations)
8. [Troubleshooting](#troubleshooting)
9. [Windows Support](#windows-support)

---

## Development Workflow

### Initial Setup

**Linux/macOS:**
```bash
# Install .NET SDK 10.x
brew install --cask dotnet-sdk

# Restore dependencies
./scripts/restore.sh

# Build all projects
./scripts/build.sh

# Run all tests
./scripts/test.sh
```

**Windows (PowerShell):**
```powershell
# Install .NET SDK 10.x from https://dotnet.microsoft.com/download/dotnet/10.0
# Or via Chocolatey: choco install dotnet-sdk

# Restore dependencies
.\scripts\restore.ps1

# Build all projects
.\scripts\build.ps1

# Run all tests
.\scripts\test.ps1
```

### Build Commands

**Linux/macOS:**
```bash
# Full rebuild
./scripts/build.sh

# Build specific project
dotnet build src/Mvp.Trading.Api/Mvp.Trading.Api.csproj

# Clean build
dotnet clean && ./scripts/build.sh

# Build with specific configuration
dotnet build --configuration Release
```

**Windows (PowerShell):**
```powershell
# Full rebuild
.\scripts\build.ps1

# Build specific project
dotnet build src\Mvp.Trading.Api\Mvp.Trading.Api.csproj

# Clean build
dotnet clean; .\scripts\build.ps1

# Build with specific configuration
dotnet build --configuration Release
```

### Running Locally (Without Docker)
```bash
# Run API
dotnet run --project src/Mvp.Trading.Api/Mvp.Trading.Api.csproj

# Run Worker
dotnet run --project src/Mvp.Trading.Worker/Mvp.Trading.Worker.csproj

# Run with specific environment
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Mvp.Trading.Api/Mvp.Trading.Api.csproj
```

---

## Multi-Agent (AO)

```bash
# Start AO
ao start

# Validate AO + PR flow readiness (YAML/auth/env fail-fast checks)
./scripts/agents/check-ao-pr-flow-readiness.sh --project AI-Assisted

# Bootstrap worktrees + sync contract
./scripts/agents/bootstrap-feature.sh --scope <feature-scope-id> --base main --with-tmux --force

# Run one AO coordinated pass
./scripts/agents/run-feature-once-ao.sh --scope <feature-scope-id>

# Optional: bypass preflight (not recommended)
./scripts/agents/run-feature-once-ao.sh --scope <feature-scope-id> --skip-readiness-check

# Inspect outcomes
ao status
ao session ls
cat /tmp/multi-agent-sync/<feature-scope-id>/outbox/orchestrator.md
```

---

## Docker Operations

### Starting Services

```bash
# Start all services (background)
docker compose up -d

# Start all services with build (background)
docker compose up --build -d

# Start all services (foreground with logs)
docker compose up --build

# Start specific services
docker compose up -d api worker
docker compose up -d postgres redis

# Start with specific env file
docker compose --env-file .env.smoke up -d
```

### Stopping Services

```bash
# Stop all services
docker compose down

# Stop and remove volumes (clean slate)
docker compose down -v

# Stop specific service
docker compose stop api
docker compose stop worker
```

### Managing Services

```bash
# Restart all services
docker compose restart

# Restart specific service
docker compose restart api
docker compose restart worker
docker compose restart prometheus

# View running services
docker compose ps

# View all containers (including stopped)
docker compose ps -a
```

### Viewing Logs

```bash
# Tail logs from all services
docker compose logs -f

# Tail logs from specific service
docker compose logs -f api
docker compose logs -f worker

# View last N lines
docker compose logs --tail=100 api

# View logs with timestamps
docker compose logs -f --timestamps api

# View logs from multiple services
docker compose logs -f api worker
```

### Rebuilding

```bash
# Rebuild specific service
docker compose build api
docker compose up -d api

# Force rebuild without cache
docker compose build --no-cache api

# Rebuild everything
docker compose build --no-cache
docker compose up -d
```

### Cleanup

```bash
# Remove stopped containers
docker compose rm

# Remove all containers, networks, volumes
docker compose down -v

# Clean up Docker system (careful!)
docker system prune -a --volumes
```

---

## Environment Switching

### Simulated Environment (Safe Testing)
```bash
# Switch execution config to simulated mode
./scripts/switch-env.sh simulated

# Update .env file
echo "KRAKEN_FUTURES_ENV=demo" >> .env

# Rebuild containers
docker compose up --build -d
```

### Demo Environment (Kraken Sandbox)
```bash
# Switch execution config to demo mode
./scripts/switch-env.sh demo

# Update .env file
echo "KRAKEN_FUTURES_ENV=demo" >> .env

# Add demo credentials to .env
echo "KRAKEN_FUTURES_DEMO_API_KEY=your-demo-key" >> .env
echo "KRAKEN_FUTURES_DEMO_API_SECRET=your-demo-secret" >> .env

# Rebuild containers
docker compose up --build -d
```

### Production Environment (Real Trading - Use Caution!)
```bash
# Switch execution config to production (requires confirmation)
CONFIRM=yes ./scripts/switch-env.sh prod

# Update .env file
echo "KRAKEN_FUTURES_ENV=prod" >> .env

# Add production credentials to .env (SECURE THESE!)
echo "KRAKEN_FUTURES_PROD_API_KEY=your-prod-key" >> .env
echo "KRAKEN_FUTURES_PROD_API_SECRET=your-prod-secret" >> .env

# Rebuild containers
docker compose up --build -d
```

**⚠️ IMPORTANT**: Always rebuild containers after switching environments. The `config/execution.json` file is copied into the Docker image at build time.

---

## Testing

### Unit Tests

```bash
# Run all tests
./scripts/test.sh

# Run tests with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run specific test project
dotnet test tests/Mvp.Trading.Elliott.Tests/Mvp.Trading.Elliott.Tests.csproj

# Run specific test
dotnet test --filter "FullyQualifiedName~ElliottEngineTests.GenerateCandidates"

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Integration Tests

```bash
# Set up test environment variables
export KRAKEN_FUTURES_ENV=demo
export KRAKEN_FUTURES_DEMO_API_KEY=your-demo-key
export KRAKEN_FUTURES_DEMO_API_SECRET=your-demo-secret

# Run integration tests
dotnet test tests/Mvp.Trading.Integrations.Kraken.Tests/

# Run execution integration tests
dotnet test tests/Mvp.Trading.Execution.Tests/
```

---

## Smoke Testing

### Prerequisites Setup

1. Create `.env.smoke` file (not committed to git):
```bash
cat > .env.smoke << 'EOF'
TRADINGVIEW_WEBHOOK_SECRET=your-secret-here
SYMBOL_HINT=BTCUSD.P
TICKER=BTCUSD.P
EXCHANGE=krakenfutures
INTERVAL=1
KRAKEN_FUTURES_ENV=demo
KRAKEN_FUTURES_DEMO_API_KEY=your-demo-key
KRAKEN_FUTURES_DEMO_API_SECRET=your-demo-secret
SLEEP_SECONDS=3
SMOKE_TIMEOUT_SECONDS=300
NGROK_AUTOSTART=1
EOF
```

2. Install ngrok:
```bash
brew install ngrok
```

### Running Smoke Tests

```bash
# Full smoke test (auto-starts ngrok, runs test, cleans up)
./scripts/smoke.sh

# Start services manually with smoke config
docker compose --env-file .env.smoke up -d --build api worker

# Check if services are healthy
curl http://localhost:8080/health
curl http://localhost:8080/health/dependencies

# View smoke test logs
docker compose logs -f api worker

# Stop smoke test services
docker compose down
```

### Manual Webhook Testing

```bash
# Get ngrok URL (if running manually)
curl http://localhost:4040/api/tunnels

# Send test webhook
curl -X POST http://localhost:8080/webhooks/tradingview/your-secret-here \
  -H "Content-Type: application/json" \
  -d '{
    "symbol": "BTCUSD.P",
    "exchange": "krakenfutures",
    "interval": "1",
    "action": "BUY",
    "price": 50000
  }'
```

---

## Monitoring & Observability

### Accessing Monitoring Tools

```bash
# Start monitoring stack
docker compose up -d prometheus grafana

# Access Prometheus UI
open http://localhost:9090

# Access Grafana UI
open http://localhost:3000
# Default credentials: admin / admin
```

### Metrics Endpoints

```bash
# Check API metrics
curl http://localhost:8080/metrics

# Check Worker metrics (if running)
curl http://localhost:9464/metrics

# Check specific metric
curl -s http://localhost:8080/metrics | grep trading_alerts_received_total

# Check all trading metrics
curl -s http://localhost:8080/metrics | grep "^trading_"
```

### Prometheus Queries

```bash
# Query Prometheus API
curl 'http://localhost:9090/api/v1/query?query=trading_alerts_received_total'

# Query with time range
curl 'http://localhost:9090/api/v1/query_range?query=rate(trading_alerts_received_total[5m])&start=2024-01-01T00:00:00Z&end=2024-01-01T01:00:00Z&step=15s'

# Check alert rules
curl http://localhost:9090/api/v1/rules

# Check active alerts
curl http://localhost:9090/api/v1/alerts
```

### Grafana Dashboards

Available dashboards (auto-provisioned):
- **Alert Processing**: Queue depth, processing rates, outcomes
- **Execution & Orders**: Order placement, fills, execution duration
- **System Health**: Errors, reconciliation, stop-losses

```bash
# List all dashboards via API
curl -u admin:admin http://localhost:3000/api/search?type=dash-db

# Export dashboard
curl -u admin:admin http://localhost:3000/api/dashboards/uid/alert-processing -o dashboard-backup.json
```

### Health Checks

```bash
# API health check
curl http://localhost:8080/health

# Detailed dependency health
curl http://localhost:8080/health/dependencies | jq

# Check specific dependency
curl http://localhost:8080/health/dependencies | jq '.entries.postgres'
curl http://localhost:8080/health/dependencies | jq '.entries.redis'
```

---

## Database Operations

### PostgreSQL Access

```bash
# Connect to PostgreSQL container
docker compose exec postgres psql -U mvptrading

# Run SQL from command line
docker compose exec postgres psql -U mvptrading -c "SELECT COUNT(*) FROM execution_intents;"

# Dump database
docker compose exec postgres pg_dump -U mvptrading mvptrading > backup.sql

# Restore database
cat backup.sql | docker compose exec -T postgres psql -U mvptrading mvptrading
```

### Redis Access

```bash
# Connect to Redis container
docker compose exec redis redis-cli

# Check Redis keys
docker compose exec redis redis-cli KEYS "*"

# Get specific key
docker compose exec redis redis-cli GET "some-key"

# Flush all Redis data (CAREFUL!)
docker compose exec redis redis-cli FLUSHALL
```

### Database Initialization

```bash
# Run init script manually
docker compose exec postgres psql -U mvptrading -f /docker-entrypoint-initdb.d/init.sql

# Check if tables exist
docker compose exec postgres psql -U mvptrading -c "\dt"

# View table schema
docker compose exec postgres psql -U mvptrading -c "\d+ execution_intents"
```

---

## Troubleshooting

### Common Issues

#### Services Won't Start
```bash
# Check service logs
docker compose logs api
docker compose logs worker

# Check container status
docker compose ps

# Rebuild with no cache
docker compose down -v
docker compose build --no-cache
docker compose up -d
```

#### Port Conflicts
```bash
# Check what's using a port
lsof -i :8080
lsof -i :5432

# Kill process using port
kill $(lsof -t -i :8080)
```

#### Database Connection Issues
```bash
# Check if Postgres is running
docker compose ps postgres

# Check Postgres logs
docker compose logs postgres

# Restart Postgres
docker compose restart postgres

# Connect manually to verify
docker compose exec postgres psql -U mvptrading -c "SELECT 1;"
```

#### Redis Connection Issues
```bash
# Check if Redis is running
docker compose ps redis

# Check Redis logs
docker compose logs redis

# Test Redis connection
docker compose exec redis redis-cli PING
```

#### Metrics Not Showing
```bash
# Verify metrics endpoint
curl http://localhost:8080/metrics

# Check Prometheus targets
curl http://localhost:9090/api/v1/targets | jq '.data.activeTargets'

# Restart Prometheus
docker compose restart prometheus

# Check Prometheus logs
docker compose logs prometheus
```

#### Worker Not Processing
```bash
# Check worker logs
docker compose logs -f worker

# Check Redis queue
docker compose exec redis redis-cli LLEN "alert-queue"

# Check worker dependencies
curl http://localhost:8080/health/dependencies

# Restart worker
docker compose restart worker
```

### Debug Mode

```bash
# Run with debug logging
ASPNETCORE_ENVIRONMENT=Development docker compose up --build

# Enable specific log level
docker compose exec api printenv | grep LOG
docker compose exec -e Logging__LogLevel__Default=Debug api
```

### Network Inspection

```bash
# List Docker networks
docker network ls

# Inspect trading network
docker network inspect ai-assisted_default

# Test connectivity between containers
docker compose exec api ping postgres
docker compose exec worker ping redis
```

---

## Quick Reference Tables

### Service Ports

| Service | Port | URL |
|---------|------|-----|
| API | 8080 | http://localhost:8080 |
| Worker Metrics | 9464 | http://localhost:9464/metrics |
| PostgreSQL | 5432 | localhost:5432 |
| Redis | 6379 | localhost:6379 |
| Prometheus | 9090 | http://localhost:9090 |
| Grafana | 3000 | http://localhost:3000 |

### Key Files

| File | Purpose |
|------|---------|
| `.env` | Production/demo environment variables |
| `.env.smoke` | Smoke test configuration (not committed) |
| `config/execution.json` | Execution mode configuration |
| `config/kraken-futures.json` | Kraken API endpoints |
| `config/prometheus.yml` | Prometheus scrape configuration |
| `config/prometheus-alerts.yml` | Alert rules |
| `docker-compose.yml` | Service orchestration |

### Script Overview

| Script | Purpose |
|--------|---------|
| `scripts/restore.sh` | Restore NuGet packages |
| `scripts/build.sh` | Build all projects |
| `scripts/test.sh` | Run all unit tests |
| `scripts/smoke.sh` | Run smoke tests with ngrok |
| `scripts/switch-env.sh` | Switch execution environment |

---

## Common Workflows

### Full Development Cycle
```bash
# 1. Pull latest changes
git pull

# 2. Restore and build
./scripts/restore.sh
./scripts/build.sh

# 3. Run tests
./scripts/test.sh

# 4. Start services
docker compose up --build -d

# 5. Check health
curl http://localhost:8080/health

# 6. View logs
docker compose logs -f api worker
```

### Quick Deploy & Test
```bash
# Deploy
docker compose up --build -d

# Wait for services
sleep 10

# Health check
curl http://localhost:8080/health/dependencies | jq '.status'

# View metrics
curl http://localhost:8080/metrics | grep "^trading_"

# Check dashboards
open http://localhost:3000
```

### Emergency Stop
```bash
# Stop all services immediately
docker compose down

# Stop and clean everything
docker compose down -v

# Kill any stuck processes
pkill -f "Mvp.Trading"
```

---

## Windows Support

All development and build scripts are available in PowerShell versions for Windows environments.

### Available Windows Scripts

| Bash Script | PowerShell Equivalent | Purpose |
|-------------|----------------------|---------|
| `./scripts/dotnet.sh` | `.\scripts\dotnet.ps1` | .NET CLI wrapper |
| `./scripts/restore.sh` | `.\scripts\restore.ps1` | Restore NuGet packages |
| `./scripts/build.sh` | `.\scripts\build.ps1` | Build solution |
| `./scripts/test.sh` | `.\scripts\test.ps1` | Run all tests |
| `./scripts/dev/bootstrap.sh` | `.\scripts\dev\bootstrap.ps1` | Setup dev environment |

### Windows Setup Quick Start

```powershell
# 1. Install prerequisites
choco install dotnet-sdk postgresql redis-64

# 2. Bootstrap environment
.\scripts\dev\bootstrap.ps1

# 3. Build and test
.\scripts\restore.ps1
.\scripts\build.ps1
.\scripts\test.ps1

# 4. Run with Docker
docker compose up --build -d
```

### Docker on Windows

Docker Desktop supports both Linux and Windows containers:

```powershell
# Build with Docker (uses Linux containers by default)
docker compose build

# Run services
docker compose up -d

# View logs
docker compose logs -f api worker
```

**Note:** The Dockerfiles automatically detect the platform and use the appropriate scripts (bash for Linux, PowerShell for Windows).

### Detailed Windows Instructions

For comprehensive Windows-specific documentation, troubleshooting, and best practices, see:

📖 **[scripts/WINDOWS.md](../scripts/WINDOWS.md)**

This includes:
- Complete prerequisites and installation steps
- PowerShell script usage and examples
- Windows-specific configuration
- Common issues and solutions
- Path separator differences
- Environment variable syntax

---

## Best Practices

1. **Always rebuild after config changes**: `docker compose up --build -d`
2. **Check health before testing**: `curl http://localhost:8080/health`
3. **Use simulated mode for development**: `./scripts/switch-env.sh simulated`
4. **Monitor logs during deployment**: `docker compose logs -f`
5. **Back up before production**: `pg_dump` and config file backup
6. **Use `.env.smoke` for smoke tests**: Never commit credentials
7. **Verify environment**: Check `config/execution.json` before trading
8. **Clean up resources**: `docker compose down -v` when done testing
9. **Windows users**: Use PowerShell versions of scripts (see [scripts/WINDOWS.md](../scripts/WINDOWS.md))
10. **Cross-platform**: Docker works the same on Linux, macOS, and Windows

---

## Emergency Contacts & Resources

- **Documentation**: `/docs` directory
- **Windows Guide**: [scripts/WINDOWS.md](../scripts/WINDOWS.md)
- **Kraken API Docs**: https://docs.futures.kraken.com/
- **Prometheus Docs**: https://prometheus.io/docs/
- **Grafana Docs**: https://grafana.com/docs/
- **OpenTelemetry Docs**: https://opentelemetry.io/docs/

---

*Last Updated: January 2026*
*For questions or issues, refer to project README.md and docs/ directory*
