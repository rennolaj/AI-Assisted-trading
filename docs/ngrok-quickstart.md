# ngrok Docker Integration - Quick Reference

## Problem Solved ✅

**Before:** Had to run ngrok manually on host machine to expose webhooks to TradingView

**After:** ngrok runs as a Docker service alongside your API

## Quick Start

### 1. Get ngrok Authtoken

Visit: https://dashboard.ngrok.com/get-started/your-authtoken

### 2. Add to Environment

```bash
# Add to your .env.demo.local or .env.prod.local
echo "NGROK_AUTHTOKEN=your-actual-token" >> .env.demo.local
```

### 3. Start with ngrok

```bash
# Start all services including ngrok
docker compose --env-file .env.demo.local --profile ngrok up -d
```

### 4. Get Your Webhook URL

```bash
# Option 1: Use helper script (recommended)
./scripts/get-ngrok-url.sh

# Option 2: Open web UI
open http://localhost:4040

# Option 3: Manual API call
curl -s http://localhost:4040/api/tunnels | \
  python3 -c "import sys, json; print(json.load(sys.stdin)['tunnels'][0]['public_url'])"
```

### 5. Configure TradingView

Use the URL from step 4 in your TradingView alert:
```
https://abc123.ngrok-free.app/webhooks/tradingview/your-webhook-secret
```

## Commands

```bash
# Start everything with ngrok
docker compose --env-file .env.demo.local --profile ngrok up -d

# Start everything without ngrok (local dev only)
docker compose --env-file .env.demo.local up -d

# Add ngrok to already running stack
docker compose --env-file .env.demo.local --profile ngrok up -d ngrok

# Stop only ngrok
docker compose stop ngrok

# View ngrok logs
docker compose logs -f ngrok

# Get webhook URL
./scripts/get-ngrok-url.sh

# Check ngrok status
docker compose ps ngrok
```

## How It Works

### Docker Compose Profile

The ngrok service uses a **profile** so it only starts when explicitly requested:

```yaml
services:
  ngrok:
    image: ngrok/ngrok:latest
    profiles:
      - ngrok  # Only starts with --profile ngrok flag
```

This means:
- `docker compose up` → ngrok **does NOT** start
- `docker compose --profile ngrok up` → ngrok **starts**

### Networking

ngrok connects to your API using Docker's internal networking:

```yaml
command:
  - "http"
  - "api:8080"  # Uses Docker service name, not localhost
```

The flow:
```
TradingView → ngrok tunnel → ngrok container → api container
   Internet      Public       Docker Network   Docker Network
```

## Files Modified

1. **docker-compose.yml** - Added ngrok service
2. **.env.demo** - Added NGROK_AUTHTOKEN
3. **.env.prod** - Added NGROK_AUTHTOKEN (with warnings)
4. **.env.example** - Added NGROK_AUTHTOKEN
5. **scripts/get-ngrok-url.sh** - Helper script to get webhook URL

## Environment Variables

```bash
# Required for ngrok service
NGROK_AUTHTOKEN=your-token-from-dashboard
```

Get from: https://dashboard.ngrok.com/get-started/your-authtoken

## Free vs Paid

### Free Tier (Good for Development)
- ✅ Works great for testing
- ✅ HTTPS tunnel
- ✅ Web UI at localhost:4040
- ⚠️ Random URL (changes on restart)
- ⚠️ "Visit Site" warning page

### Paid Tier ($8/month - Better for Production)
- ✅ Reserved domain (static URL)
- ✅ No warning page
- ✅ More concurrent tunnels
- ✅ Better reliability

## Security Notes

### ⚠️ Important

When ngrok is running, your API is **publicly accessible** on the internet.

**Protections in place:**
- Webhook secret required for all alert endpoints
- Only specific endpoints exposed
- Can stop ngrok when not needed

**Best practices:**
- Use strong webhook secret: `openssl rand -hex 32`
- Stop ngrok when not testing: `docker compose stop ngrok`
- Monitor access via Grafana dashboards
- For production, use real domain instead

## Troubleshooting

### ngrok won't start

```bash
# Check logs
docker compose logs ngrok

# Common issue: missing authtoken
# Solution: Add to .env file
echo "NGROK_AUTHTOKEN=your-token" >> .env.demo.local
docker compose --profile ngrok up -d ngrok
```

### Can't get URL

```bash
# Wait a few seconds for ngrok to connect
sleep 5

# Try helper script
./scripts/get-ngrok-url.sh

# Or check web UI
open http://localhost:4040
```

### TradingView can't reach webhook

**Checklist:**
1. ✅ ngrok running? `docker compose ps ngrok`
2. ✅ API running? `docker compose ps api`  
3. ✅ Correct URL? `./scripts/get-ngrok-url.sh`
4. ✅ Correct secret? Check .env.demo.local
5. ✅ Full URL: `https://xxx.ngrok-free.app/webhooks/tradingview/xxx`

### URL changes after restart

**This is normal for free tier.**

Solutions:
1. Keep Docker running (don't restart)
2. Upgrade to ngrok paid plan (reserved domain)
3. For production, use real domain

## Production Notes

### ❌ Not Recommended for Production

ngrok free tier is not suitable for real trading:
- URL changes on restart
- Rate limits
- Warning page delays
- Not designed for 24/7 operation

### ✅ Production Alternatives

1. **Deploy to Cloud** (Recommended)
   - DigitalOcean, AWS, Azure, GCP
   - Get real domain name
   - Use Let's Encrypt for SSL
   - Much more reliable

2. **ngrok Paid Plan** (OK for small scale)
   - Reserved domain (doesn't change)
   - No warning page
   - Better reliability
   - Still not ideal for critical trading

## Example Workflow

```bash
# 1. Setup (one time)
cp .env.demo .env.demo.local
nano .env.demo.local  # Add credentials + NGROK_AUTHTOKEN

# 2. Start Docker with ngrok
docker compose --env-file .env.demo.local --profile ngrok up -d

# 3. Get webhook URL
./scripts/get-ngrok-url.sh

# 4. Copy URL to TradingView alert

# 5. Test by triggering alert

# 6. Monitor
docker compose logs -f api worker
open http://localhost:3000  # Grafana

# 7. When done testing, stop ngrok
docker compose stop ngrok

# Or stop everything
docker compose --profile ngrok down
```

## See Also

- [Full ngrok Docker Guide](./ngrok-docker-guide.md) - Comprehensive documentation
- [Environment Files](./environment-files.md) - Configuration guide
- [Command Reference](./command-reference.md) - All Docker commands
