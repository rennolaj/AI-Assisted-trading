# Environment Configuration Files

This document explains the different environment files and how to use them with Docker.

## Available Environment Files

| File | Purpose | When to Use |
|------|---------|-------------|
| `.env.example` | Template with all variables | Reference/starting point |
| `.env` | Local development | Running locally without Docker |
| `.env.demo` | Demo trading | Docker deployment with Kraken demo account |
| `.env.prod` | Production trading | Docker deployment with real money ⚠️ |
| `.env.smoke` | Smoke testing | Running automated tests with ngrok |

## Quick Start

### Demo Trading (Recommended for Testing)

```bash
# 1. Copy demo template
cp .env.demo .env.demo.local

# 2. Edit with your credentials
nano .env.demo.local

# 3. Set your values:
#    - TRADINGVIEW_WEBHOOK_SECRET (generate with: openssl rand -hex 32)
#    - KRAKEN_FUTURES_DEMO_API_KEY (from https://demo-futures.kraken.com/)
#    - KRAKEN_FUTURES_DEMO_API_SECRET
#    - OPENAI_API_KEY (or configure local LLM)
#    - NGROK_AUTHTOKEN (from https://dashboard.ngrok.com/ - optional)

# 4. Start Docker containers (with ngrok for TradingView webhooks)
docker compose --env-file .env.demo.local --profile ngrok up --build -d

# 5. Get your ngrok webhook URL
open http://localhost:4040

# 6. Check logs
docker compose logs -f api worker
```

**Note:** For webhook access from TradingView, see [ngrok Docker Guide](./ngrok-docker-guide.md).

### Production Trading ⚠️

```bash
# 1. ⚠️  ONLY after thorough demo testing
cp .env.prod .env.prod.local

# 2. Edit with production credentials
nano .env.prod.local

# 3. Set production values:
#    - TRADINGVIEW_WEBHOOK_SECRET (DIFFERENT from demo!)
#    - POSTGRES_CONNECTION_STRING (use strong password)
#    - KRAKEN_FUTURES_PROD_API_KEY (from https://futures.kraken.com/)
#    - KRAKEN_FUTURES_PROD_API_SECRET
#    - OPENAI_API_KEY (production key with billing limits)

# 4. Verify all settings
cat .env.prod.local

# 5. Start with production config
docker compose --env-file .env.prod.local up --build -d
```

## File Details

### `.env.example` - Template Reference

Contains all available configuration variables with descriptions and examples. Use this as a reference when creating your own environment files.

**Never commit this with real credentials!**

### `.env` - Local Development

For running the application locally (outside Docker) on your development machine.

**Key differences from Docker configs:**
- Uses `localhost` instead of Docker service names
- May connect to local LLM directly
- Good for debugging and development

**Never commit this file!** (Already in `.gitignore`)

### `.env.demo` - Demo Trading

Complete configuration for running in Docker with Kraken Futures **demo account**.

**Features:**
- Uses demo.kraken.com endpoints
- Paper money only (no real funds at risk)
- All Docker service names configured
- Safe for testing strategies

**To use:**
1. Copy to `.env.demo.local` (gitignored)
2. Add your demo API credentials
3. Run: `docker compose --env-file .env.demo.local up -d`

### `.env.prod` - Production Trading ⚠️

Configuration for **REAL TRADING** with **REAL MONEY**.

**Critical Requirements:**
- ✅ Test thoroughly in demo first
- ✅ Use strong, unique secrets
- ✅ Use production OpenAI key (not dev/test key)
- ✅ Set up monitoring and alerts
- ✅ Start with small position sizes
- ✅ Have kill switches configured

**To use:**
1. Copy to `.env.prod.local` (gitignored)
2. Add production credentials
3. Review execution.json settings
4. Run: `docker compose --env-file .env.prod.local up -d`

**Never commit this file with real credentials!**

### `.env.smoke` - Automated Testing

Special configuration for running smoke tests with ngrok.

**Features:**
- Configures ngrok URL for webhook testing
- Uses demo credentials
- Includes test-specific timeouts
- Works with `./scripts/smoke.sh`

**To use:**
1. Start ngrok: `ngrok http 8080`
2. Update `BASE_URL` in `.env.smoke` with your ngrok URL
3. Run: `./scripts/smoke.sh`

## Environment Variables Reference

### TradingView Webhook

```bash
TRADINGVIEW_WEBHOOK_SECRET=your-secret-here
```

Generate a strong secret:
```bash
openssl rand -hex 32
```

Configure in TradingView alert URL:
```
https://your-domain.com/webhooks/tradingview/your-secret-here
```

**For local/Docker deployment:** Use ngrok to expose your API. See [ngrok Docker Guide](./ngrok-docker-guide.md).

### ngrok (Optional - for webhook access)

```bash
NGROK_AUTHTOKEN=your-ngrok-authtoken
```

Get your authtoken from: https://dashboard.ngrok.com/get-started/your-authtoken

**Usage:**
```bash
# Start Docker with ngrok
docker compose --env-file .env.demo.local --profile ngrok up -d

# Get your public webhook URL
open http://localhost:4040
```

See [ngrok Docker Guide](./ngrok-docker-guide.md) for complete setup instructions.

### Database (Docker)

```bash
# PostgreSQL
POSTGRES_CONNECTION_STRING=Host=postgres;Port=5432;Database=mvp_trading;Username=postgres;Password=postgres

# Redis
REDIS_CONNECTION_STRING=redis:6379
REDIS_ALERT_QUEUE_KEY=mvp:alerts
```

**Note:** Use Docker service names (`postgres`, `redis`) not `localhost`.

### Kraken Futures

```bash
# Demo Account
KRAKEN_FUTURES_ENV=demo
KRAKEN_FUTURES_DEMO_API_KEY=your-demo-key
KRAKEN_FUTURES_DEMO_API_SECRET=your-demo-secret

# Production Account ⚠️
KRAKEN_FUTURES_ENV=prod
KRAKEN_FUTURES_PROD_API_KEY=your-prod-key
KRAKEN_FUTURES_PROD_API_SECRET=your-prod-secret
```

Get credentials:
- Demo: https://demo-futures.kraken.com/
- Production: https://futures.kraken.com/

### LLM Provider

**OpenAI (Recommended for Production):**
```bash
MCP_PROVIDER=openai
OPENAI_API_KEY=sk-your-api-key
```

**Local LLM (Ollama/LM Studio):**
```bash
MCP_PROVIDER=local
LOCAL_LLM_BASE_URL=http://host.docker.internal:11434/v1/
LOCAL_LLM_MODE=chat
LOCAL_LLM_USE_RESPONSE_FORMAT=false
```

**Note:** Use `host.docker.internal` to access services on host machine from Docker.

### Market Data

```bash
# Live data from Kraken
MARKETDATA_MODE=kraken

# Test data from fixtures
MARKETDATA_MODE=fixtures
MARKETDATA_FIXTURE_PATH=fixtures/kraken-futures
```

## Docker Compose Usage

### Default (uses `.env` if present)
```bash
docker compose up -d
```

### With specific env file
```bash
# Demo
docker compose --env-file .env.demo.local up -d

# Production
docker compose --env-file .env.prod.local up -d

# Smoke test
docker compose --env-file .env.smoke up -d
```

### View configuration
```bash
# Show resolved environment variables
docker compose --env-file .env.demo.local config

# Check specific service env
docker compose exec api printenv | grep KRAKEN
```

## Security Best Practices

### ❌ Never Commit

- `.env`
- `.env.demo.local`
- `.env.prod.local`  
- Any file with real credentials

### ✅ Always Commit

- `.env.example` (template only)
- `.env.demo` (template with placeholders)
- `.env.prod` (template with placeholders)

### 🔒 Protect Secrets

```bash
# Set restrictive permissions
chmod 600 .env.demo.local
chmod 600 .env.prod.local

# Never log secrets
# Never include secrets in error messages
# Rotate secrets regularly
```

### 🚨 Production Checklist

Before deploying to production:

- [ ] Tested thoroughly in demo for at least 2 weeks
- [ ] Reviewed all configuration files
- [ ] Generated unique webhook secrets (different from demo)
- [ ] Used strong database passwords
- [ ] Configured production OpenAI key with billing limits
- [ ] Verified KRAKEN_FUTURES_ENV=prod
- [ ] Reviewed execution.json settings
- [ ] Set up monitoring and alerting
- [ ] Configured kill switches
- [ ] Started with minimal position sizes
- [ ] Have backup/rollback plan ready

## Troubleshooting

### Can't connect to PostgreSQL

**Problem:** `Host not found: localhost`

**Solution:** Use Docker service name in connection string:
```bash
# ❌ Wrong
POSTGRES_CONNECTION_STRING=Host=localhost;...

# ✅ Correct (Docker)
POSTGRES_CONNECTION_STRING=Host=postgres;...
```

### Can't connect to LLM on host

**Problem:** `Connection refused to localhost:11434`

**Solution:** Use `host.docker.internal`:
```bash
# ❌ Wrong
LOCAL_LLM_BASE_URL=http://localhost:11434/v1/

# ✅ Correct (Docker accessing host)
LOCAL_LLM_BASE_URL=http://host.docker.internal:11434/v1/
```

### Webhook not receiving alerts

**Problem:** TradingView webhooks not reaching container

**Solutions:**
1. **Use ngrok** - See [ngrok Docker Guide](./ngrok-docker-guide.md)
   ```bash
   docker compose --env-file .env.demo.local --profile ngrok up -d
   open http://localhost:4040  # Get your public URL
   ```
2. Deploy to server with public IP
3. Configure firewall/port forwarding
4. Verify webhook secret matches

### Wrong Kraken environment

**Problem:** Orders not executing or using wrong account

**Solution:** Check `KRAKEN_FUTURES_ENV`:
```bash
# Demo trading
KRAKEN_FUTURES_ENV=demo
KRAKEN_FUTURES_DEMO_API_KEY=...

# Production trading ⚠️
KRAKEN_FUTURES_ENV=prod
KRAKEN_FUTURES_PROD_API_KEY=...
```

## See Also

- [ngrok Docker Guide](./ngrok-docker-guide.md) - Expose webhooks for TradingView
- [Command Reference](docs/command-reference.md) - All commands for development and deployment
- [Environment Switching](docs/environment-switching.md) - Safe environment management
- [Smoke Testing](scripts/smoke.sh) - Automated testing guide
- [Grafana Status](docs/grafana-status.md) - Monitoring setup
