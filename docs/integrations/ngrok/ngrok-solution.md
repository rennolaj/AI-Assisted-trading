# ngrok Problem - SOLVED ✅

## The Problem

When running the trading system in Docker containers, TradingView webhooks couldn't reach the API because:
1. Containers run on internal Docker network
2. Local machine typically doesn't have public IP
3. Previously had to run ngrok manually on host machine

## The Solution

**ngrok now runs as a Docker service** alongside your API.

### What Was Changed

#### 1. docker-compose.yml
Added ngrok service with profile support:
```yaml
ngrok:
  image: ngrok/ngrok:latest
  command:
    - "http"
    - "api:8080"  # Uses Docker networking
    - "--authtoken=${NGROK_AUTHTOKEN}"
  ports:
    - "4040:4040"  # Web UI
  profiles:
    - ngrok  # Optional service
```

#### 2. Environment Files
Added `NGROK_AUTHTOKEN` to:
- `.env.example`
- `.env.demo`
- `.env.prod`

#### 3. Documentation
Created comprehensive guides:
- `docs/integrations/ngrok/ngrok-docker-guide.md` - Full guide (500+ lines)
- `docs/integrations/ngrok/ngrok-quickstart.md` - Quick reference
- `docs/configuration/environment-files.md` - Complete environment guide

#### 4. Helper Script
Created `scripts/get-ngrok-url.sh`:
- Checks if ngrok is running
- Retrieves public URL
- Shows complete webhook URL with secret
- Provides troubleshooting tips

## How to Use

### Step 1: Get ngrok Authtoken
1. Visit: https://dashboard.ngrok.com/signup
2. Get authtoken: https://dashboard.ngrok.com/get-started/your-authtoken
3. Copy the token (looks like `2abc...xyz`)

### Step 2: Configure Environment
```bash
# Copy template
cp .env.demo .env.demo.local

# Edit and add:
# - NGROK_AUTHTOKEN=your-token
# - TRADINGVIEW_WEBHOOK_SECRET=your-secret
# - KRAKEN_FUTURES_DEMO_API_KEY=...
# - KRAKEN_FUTURES_DEMO_API_SECRET=...
# - OPENAI_API_KEY=...
nano .env.demo.local
```

### Step 3: Start Docker with ngrok
```bash
# Start all services including ngrok
docker compose --env-file .env.demo.local --profile ngrok up -d
```

### Step 4: Get Webhook URL
```bash
# Use helper script (recommended)
./scripts/get-ngrok-url.sh

# Output example:
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# Public URL: https://abc123.ngrok-free.app
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# 
# 📋 TradingView Webhook URL:
# https://abc123.ngrok-free.app/webhooks/tradingview/your-secret
```

### Step 5: Configure TradingView
In your TradingView alert, use the webhook URL from step 4.

### Step 6: Test
Trigger an alert and check logs:
```bash
docker compose logs -f api worker
```

## Key Features

### Docker Profile Support
ngrok only starts when explicitly requested:

```bash
# Without ngrok (local dev only)
docker compose up -d

# With ngrok (for TradingView webhooks)
docker compose --profile ngrok up -d
```

### Helper Script
Quick access to webhook URL:
```bash
./scripts/get-ngrok-url.sh
```

### Web UI Access
Monitor ngrok status:
```bash
open http://localhost:4040
```

## What's Better Now

### Before ❌
```bash
# Had to run ngrok manually on host
ngrok http 8080  # Separate terminal

# Then start Docker
docker compose up -d

# Two separate processes to manage
```

### After ✅
```bash
# Everything in one command
docker compose --profile ngrok up -d

# Get URL with helper script
./scripts/get-ngrok-url.sh

# All integrated!
```

## Benefits

1. **Integrated** - ngrok managed by docker-compose
2. **Optional** - Uses profile, doesn't start unless needed
3. **Documented** - Comprehensive guides and quick references
4. **Scripted** - Helper script makes it easy
5. **Flexible** - Can still run ngrok on host if preferred

## Files Added/Modified

### Added
- ✅ `docs/integrations/ngrok/ngrok-docker-guide.md` (complete guide)
- ✅ `docs/integrations/ngrok/ngrok-quickstart.md` (quick reference)
- ✅ `docs/configuration/environment-files.md` (all environment config)
- ✅ `scripts/get-ngrok-url.sh` (helper script)

### Modified
- ✅ `docker-compose.yml` (added ngrok service)
- ✅ `.env.example` (added NGROK_AUTHTOKEN)
- ✅ `.env.demo` (added NGROK_AUTHTOKEN with instructions)
- ✅ `.env.prod` (added NGROK_AUTHTOKEN with warnings)
- ✅ `README.md` (updated with ngrok quick start)

## Commands Cheat Sheet

```bash
# Start with ngrok
docker compose --env-file .env.demo.local --profile ngrok up -d

# Start without ngrok
docker compose --env-file .env.demo.local up -d

# Add ngrok to running stack
docker compose --profile ngrok up -d ngrok

# Get webhook URL
./scripts/get-ngrok-url.sh

# Check ngrok status
docker compose ps ngrok

# View ngrok logs
docker compose logs -f ngrok

# Open web UI
open http://localhost:4040

# Stop ngrok
docker compose stop ngrok

# Stop everything
docker compose --profile ngrok down
```

## Important Notes

### Free vs Paid

**Free Tier** (Good for testing):
- ✅ HTTPS tunnel
- ✅ Web UI
- ⚠️ Random URL (changes on restart)
- ⚠️ "Visit Site" warning page

**Paid Tier** ($8/month - Better for production):
- ✅ Reserved domain (static URL)
- ✅ No warning page
- ✅ More reliable

### Production

For real production trading:
- ❌ Don't use ngrok free tier (URL changes)
- ✅ Deploy to cloud server with real domain
- ✅ Or use ngrok paid plan with reserved domain

### Security

When ngrok is running, your API is **publicly accessible**:
- ✅ Webhook secret required for all endpoints
- ✅ Can stop ngrok when not needed
- ✅ Monitor access via Grafana

## Next Steps

1. **For Development/Testing:**
   ```bash
   # Use ngrok in Docker
   docker compose --profile ngrok up -d
   ./scripts/get-ngrok-url.sh
   ```

2. **For Production:**
   - Deploy to DigitalOcean/AWS/Azure
   - Get real domain name
   - Use Let's Encrypt for SSL
   - Much more reliable for 24/7 trading

## Documentation Links

- [ngrok Quick Start](ngrok-quickstart.md) - Quick reference
- [ngrok Docker Guide](ngrok-docker-guide.md) - Complete guide
- [Environment Files](../../configuration/environment-files.md) - All configuration
- [Command Reference](../../development/command-reference.md) - All Docker commands

## Problem Status: SOLVED ✅

The ngrok integration is complete and ready to use. You can now run your entire trading system in Docker with webhook access from TradingView.
