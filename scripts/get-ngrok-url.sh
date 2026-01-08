#!/usr/bin/env bash
# Get the ngrok public URL from a running ngrok container

set -euo pipefail

echo "🔍 Checking ngrok status..."

# Check if ngrok container is running
if ! docker compose ps ngrok | grep -q "Up"; then
    echo "❌ ngrok container is not running"
    echo ""
    echo "Start it with:"
    echo "  docker compose --profile ngrok up -d ngrok"
    echo ""
    echo "Or start all services with ngrok:"
    echo "  docker compose --env-file .env.demo.local --profile ngrok up -d"
    exit 1
fi

echo "✅ ngrok container is running"
echo ""

# Wait a moment for ngrok to establish tunnel
sleep 2

# Get the ngrok URL
echo "📡 Fetching ngrok tunnel URL..."
URL=$(curl -s http://localhost:4040/api/tunnels 2>/dev/null | python3 -c "
import sys, json
try:
    data = json.load(sys.stdin)
    if data.get('tunnels') and len(data['tunnels']) > 0:
        print(data['tunnels'][0]['public_url'])
    else:
        print('ERROR: No tunnels found')
        sys.exit(1)
except Exception as e:
    print(f'ERROR: {e}')
    sys.exit(1)
" 2>/dev/null)

if [ $? -ne 0 ] || [ -z "$URL" ]; then
    echo "❌ Could not get ngrok URL"
    echo ""
    echo "Troubleshooting:"
    echo "  1. Check if ngrok web UI is accessible: open http://localhost:4040"
    echo "  2. Check ngrok logs: docker compose logs ngrok"
    echo "  3. Verify NGROK_AUTHTOKEN is set in your .env file"
    exit 1
fi

echo "✅ ngrok tunnel active"
echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "Public URL: $URL"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""

# Get webhook secret from env
SECRET=""
if [ -f ".env.demo.local" ]; then
    SECRET=$(grep "^TRADINGVIEW_WEBHOOK_SECRET=" .env.demo.local 2>/dev/null | cut -d= -f2 || echo "")
elif [ -f ".env" ]; then
    SECRET=$(grep "^TRADINGVIEW_WEBHOOK_SECRET=" .env 2>/dev/null | cut -d= -f2 || echo "")
fi

if [ -n "$SECRET" ]; then
    echo "📋 TradingView Webhook URL:"
    echo "$URL/webhooks/tradingview/$SECRET"
    echo ""
else
    echo "📋 TradingView Webhook URL:"
    echo "$URL/webhooks/tradingview/YOUR-WEBHOOK-SECRET"
    echo ""
    echo "⚠️  Set TRADINGVIEW_WEBHOOK_SECRET in your .env file"
    echo ""
fi

echo "🌐 ngrok Web UI: http://localhost:4040"
echo ""
echo "💡 Tips:"
echo "  - This URL changes every restart (free tier)"
echo "  - Upgrade to ngrok paid plan for reserved domains"
echo "  - For production, use a real domain instead"
echo ""
