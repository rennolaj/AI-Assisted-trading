# ngrok with Docker Guide

This guide explains how to expose your containerized API to TradingView webhooks using ngrok.

## The Problem

When running the trading system in Docker containers, TradingView can't reach your local API because:
1. Containers are isolated from the external network
2. Your local machine likely doesn't have a public IP
3. Port forwarding and firewall rules can be complex

## The Solution

Use ngrok as a reverse proxy to create a secure tunnel from the internet to your Docker containers.

## Setup Options

### Option 1: ngrok in Docker (Recommended) ✅

Run ngrok as a Docker service alongside your API.

#### 1. Get ngrok Authtoken

1. Sign up at https://dashboard.ngrok.com/signup
2. Get your authtoken: https://dashboard.ngrok.com/get-started/your-authtoken
3. Copy the token (looks like: `2abc...xyz`)

#### 2. Add to Environment File

```bash
# Edit your .env.demo or .env.prod
echo "NGROK_AUTHTOKEN=your-actual-token-here" >> .env.demo.local
```

#### 3. Start Docker with ngrok Profile

```bash
# Start all services including ngrok
docker compose --env-file .env.demo.local --profile ngrok up -d

# Check ngrok is running
docker compose ps ngrok
```

#### 4. Get Your Public URL

Open ngrok's web UI in your browser:
```bash
open http://localhost:4040
```

Or get it via API:
```bash
curl -s http://localhost:4040/api/tunnels | python3 -c "
import sys, json
data = json.load(sys.stdin)
if data.get('tunnels'):
    print(data['tunnels'][0]['public_url'])
"
```

Example output: `https://abc123.ngrok-free.app`

#### 5. Configure TradingView Webhook

In TradingView alert:
```
Webhook URL: https://abc123.ngrok-free.app/webhooks/tradingview/alert?secret=your-webhook-secret
```

### Option 2: ngrok on Host Machine

Run ngrok directly on your host, pointing to the containerized API.

#### 1. Install ngrok

```bash
# macOS
brew install ngrok/ngrok/ngrok

# Linux
curl -s https://ngrok-agent.s3.amazonaws.com/ngrok.asc | \
  sudo tee /etc/apt/trusted.gpg.d/ngrok.asc >/dev/null && \
  echo "deb https://ngrok-agent.s3.amazonaws.com buster main" | \
  sudo tee /etc/apt/sources.list.d/ngrok.list && \
  sudo apt update && sudo apt install ngrok
```

#### 2. Configure ngrok

```bash
# Add your authtoken
ngrok config add-authtoken your-actual-token-here
```

#### 3. Start Docker (without ngrok)

```bash
# Start without ngrok profile
docker compose --env-file .env.demo.local up -d
```

#### 4. Start ngrok Tunnel

```bash
# Create tunnel to containerized API
ngrok http 8080
```

#### 5. Get Your Public URL

ngrok will display:
```
Forwarding  https://abc123.ngrok-free.app -> http://localhost:8080
```

Use this URL for TradingView webhooks.

### Option 3: Deploy to Cloud (Production)

For production, don't use ngrok. Deploy to a server with a real domain.

Popular options:
- **DigitalOcean Droplet** ($6/month)
- **AWS EC2** (free tier available)
- **Azure VM**
- **Google Cloud Compute Engine**

Configure with:
- Real domain name (e.g., `trading.yourdomain.com`)
- SSL certificate (Let's Encrypt)
- Firewall rules
- Reverse proxy (nginx/traefik)

## Docker Compose Profile Explanation

The ngrok service uses a Docker Compose **profile**:

```yaml
services:
  ngrok:
    image: ngrok/ngrok:latest
    profiles:
      - ngrok  # Only starts when explicitly requested
```

This means:
- **Without profile**: `docker compose up` → ngrok doesn't start
- **With profile**: `docker compose --profile ngrok up` → ngrok starts

This is useful because:
- You don't need ngrok for local development
- You can choose when to expose your API
- Production deployments won't accidentally start ngrok

## Usage Examples

### Start Everything with ngrok

```bash
# Demo with ngrok
docker compose --env-file .env.demo.local --profile ngrok up -d

# Check all services
docker compose ps
```

### Start Without ngrok (Local Development)

```bash
# Demo without ngrok
docker compose --env-file .env.demo.local up -d

# ngrok won't start
docker compose ps  # ngrok not in list
```

### Add ngrok to Running Stack

```bash
# Services already running without ngrok
docker compose --env-file .env.demo.local up -d

# Add ngrok to running stack
docker compose --env-file .env.demo.local --profile ngrok up -d ngrok
```

### Stop Only ngrok

```bash
# Stop ngrok but keep other services running
docker compose stop ngrok
```

### View ngrok Logs

```bash
# Watch ngrok connection logs
docker compose logs -f ngrok
```

## ngrok Free vs Paid

### Free Tier
- ✅ HTTPS tunnels
- ✅ Random URLs (changes on restart)
- ✅ Web UI at localhost:4040
- ⚠️ Limited to 1 tunnel
- ⚠️ URL changes every restart
- ⚠️ "Visit Site" warning page

### Paid Plans (Starting $8/month)
- ✅ Reserved domains (static URLs)
- ✅ No warning page
- ✅ More concurrent tunnels
- ✅ Better for production

## Troubleshooting

### ngrok Container Won't Start

**Problem:** `Error: Authtoken required`

**Solution:** Make sure `NGROK_AUTHTOKEN` is set:
```bash
# Check if set
grep NGROK_AUTHTOKEN .env.demo.local

# If missing, add it
echo "NGROK_AUTHTOKEN=your-token" >> .env.demo.local

# Restart
docker compose --profile ngrok down
docker compose --env-file .env.demo.local --profile ngrok up -d
```

### Can't Access ngrok Web UI

**Problem:** `localhost:4040` not responding

**Solution:** Check if ngrok container is running:
```bash
docker compose ps ngrok

# If not running with --profile ngrok
docker compose --env-file .env.demo.local --profile ngrok up -d ngrok
```

### TradingView "Webhook Failed"

**Problem:** TradingView can't reach your webhook

**Checklist:**
1. ✅ Is ngrok running? `docker compose ps ngrok`
2. ✅ Is API running? `docker compose ps api`
3. ✅ Is URL correct? Check http://localhost:4040
4. ✅ Is secret correct? `grep TRADINGVIEW_WEBHOOK_SECRET .env.demo.local`
5. ✅ Full URL format: `https://abc123.ngrok-free.app/webhooks/tradingview/alert?secret=your-secret`

### ngrok URL Changes After Restart

**Problem:** Have to reconfigure TradingView after every restart

**Solutions:**
1. **Don't stop Docker** - Keep containers running
2. **Use ngrok reserved domain** - Upgrade to paid plan
3. **Deploy to production** - Use real domain

### "Visit Site" Warning Page

**Problem:** ngrok shows warning page before forwarding

**This is normal for free tier.** Solutions:
1. Click "Visit Site" button once per session
2. Upgrade to paid plan (removes warning)
3. Use for development only, deploy to production

## Production Recommendations

### ❌ Don't Use for Production

ngrok free tier is **NOT** suitable for production because:
- URL changes on restart
- Warning page delays webhooks
- Rate limits apply
- Not designed for 24/7 trading

### ✅ Production Alternatives

1. **Deploy to Cloud Server**
   - Get a server with public IP
   - Configure real domain
   - Use Let's Encrypt for SSL

2. **ngrok Paid Plan**
   - Reserved domain (static URL)
   - No warning page
   - Better reliability
   - Still not ideal for trading

3. **VPS with Docker**
   - DigitalOcean, Linode, Vultr
   - Install Docker
   - Run docker-compose
   - Configure firewall

## Complete Example Workflow

### Initial Setup

```bash
# 1. Get ngrok authtoken from dashboard
open https://dashboard.ngrok.com/get-started/your-authtoken

# 2. Copy .env.demo to local file
cp .env.demo .env.demo.local

# 3. Edit configuration
nano .env.demo.local
# Set:
#   NGROK_AUTHTOKEN=2abc...xyz
#   TRADINGVIEW_WEBHOOK_SECRET=$(openssl rand -hex 32)
#   KRAKEN_FUTURES_DEMO_API_KEY=...
#   KRAKEN_FUTURES_DEMO_API_SECRET=...

# 4. Start Docker with ngrok
docker compose --env-file .env.demo.local --profile ngrok up -d

# 5. Wait for startup (30 seconds)
sleep 30

# 6. Check all services are running
docker compose ps

# 7. Open ngrok web UI
open http://localhost:4040

# 8. Get your public URL
curl -s http://localhost:4040/api/tunnels | \
  python3 -c "import sys, json; print(json.load(sys.stdin)['tunnels'][0]['public_url'])"
```

### Configure TradingView

```
1. Open TradingView
2. Create or edit alert
3. Set webhook URL to:
   https://YOUR-NGROK-URL/webhooks/tradingview/alert?secret=YOUR-WEBHOOK-SECRET
   
   Example:
   https://abc123.ngrok-free.app/webhooks/tradingview/alert?secret=a1b2c3d4e5f6...

4. Test by triggering alert
5. Check logs: docker compose logs -f api worker
```

### Monitoring

```bash
# Watch all logs
docker compose logs -f

# Watch only API
docker compose logs -f api

# Watch only ngrok
docker compose logs -f ngrok

# Check Grafana dashboards
open http://localhost:3000

# Check Prometheus metrics
open http://localhost:9090
```

### Shutdown

```bash
# Stop all services
docker compose --profile ngrok down

# Or keep data and just stop
docker compose --profile ngrok stop

# Restart later (same ngrok URL if paid plan)
docker compose --env-file .env.demo.local --profile ngrok up -d
```

## Security Considerations

### Webhook Secret

Your webhook secret is the **only** authentication for incoming alerts.

**Best Practices:**
```bash
# Generate strong secret
openssl rand -hex 32

# Different secrets for demo and prod
DEMO_SECRET=$(openssl rand -hex 32)
PROD_SECRET=$(openssl rand -hex 32)

# Never commit secrets to git
echo "*.local" >> .gitignore

# Rotate secrets regularly
# Update both .env.demo.local and TradingView
```

### ngrok Authtoken

Your ngrok authtoken allows creating tunnels on your account.

**Best Practices:**
- Don't commit to git
- Use separate tokens for dev/prod
- Regenerate if exposed
- Consider using ngrok API for automation

### API Exposure

When using ngrok, your API is publicly accessible.

**Mitigations:**
- Require webhook secret for all endpoints
- Implement rate limiting
- Monitor for abuse
- Use ngrok IP restrictions (paid feature)
- Keep ngrok running only when needed

## See Also

- [Environment Files](./environment-files.md) - Complete environment configuration guide
- [Command Reference](./command-reference.md) - All Docker commands
- [Grafana Status](./grafana-status.md) - Monitoring setup
