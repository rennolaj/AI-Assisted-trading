# Get the ngrok public URL from a running ngrok container
# PowerShell version for Windows

$ErrorActionPreference = "Stop"

Write-Host "🔍 Checking ngrok status..." -ForegroundColor Cyan

# Check if ngrok container is running
$ngrokStatus = docker compose ps ngrok 2>&1 | Out-String
if ($ngrokStatus -notmatch "Up") {
    Write-Host "❌ ngrok container is not running" -ForegroundColor Red
    Write-Host ""
    Write-Host "Start it with:"
    Write-Host "  docker compose --profile ngrok up -d ngrok" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Or start all services with ngrok:"
    Write-Host "  docker compose --env-file .env.demo.local --profile ngrok up -d" -ForegroundColor Yellow
    exit 1
}

Write-Host "✅ ngrok container is running" -ForegroundColor Green
Write-Host ""

# Wait a moment for ngrok to establish tunnel
Start-Sleep -Seconds 2

# Get the ngrok URL
Write-Host "📡 Fetching ngrok tunnel URL..." -ForegroundColor Cyan

try {
    $response = Invoke-RestMethod -Uri "http://localhost:4040/api/tunnels" -Method Get -ErrorAction Stop
    
    if ($response.tunnels -and $response.tunnels.Count -gt 0) {
        $url = $response.tunnels[0].public_url
    } else {
        Write-Host "❌ No tunnels found" -ForegroundColor Red
        Write-Host ""
        Write-Host "Troubleshooting:"
        Write-Host "  1. Check if ngrok web UI is accessible: http://localhost:4040" -ForegroundColor Yellow
        Write-Host "  2. Check ngrok logs: docker compose logs ngrok" -ForegroundColor Yellow
        Write-Host "  3. Verify NGROK_AUTHTOKEN is set in your .env file" -ForegroundColor Yellow
        exit 1
    }
} catch {
    Write-Host "❌ Could not get ngrok URL" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Troubleshooting:"
    Write-Host "  1. Check if ngrok web UI is accessible: http://localhost:4040" -ForegroundColor Yellow
    Write-Host "  2. Check ngrok logs: docker compose logs ngrok" -ForegroundColor Yellow
    Write-Host "  3. Verify NGROK_AUTHTOKEN is set in your .env file" -ForegroundColor Yellow
    exit 1
}

Write-Host "✅ ngrok tunnel active" -ForegroundColor Green
Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host "Public URL: $url" -ForegroundColor White
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host ""

# Get webhook secret from env
$secret = ""
if (Test-Path ".env.demo.local") {
    $envContent = Get-Content ".env.demo.local" -ErrorAction SilentlyContinue
    $secretLine = $envContent | Where-Object { $_ -match "^TRADINGVIEW_WEBHOOK_SECRET=" }
    if ($secretLine) {
        $secret = $secretLine -replace "^TRADINGVIEW_WEBHOOK_SECRET=", ""
    }
} elseif (Test-Path ".env") {
    $envContent = Get-Content ".env" -ErrorAction SilentlyContinue
    $secretLine = $envContent | Where-Object { $_ -match "^TRADINGVIEW_WEBHOOK_SECRET=" }
    if ($secretLine) {
        $secret = $secretLine -replace "^TRADINGVIEW_WEBHOOK_SECRET=", ""
    }
}

if ($secret) {
    Write-Host "📋 TradingView Webhook URL:" -ForegroundColor Cyan
    Write-Host "$url/webhooks/tradingview/alert?secret=$secret" -ForegroundColor White
    Write-Host ""
} else {
    Write-Host "📋 TradingView Webhook URL:" -ForegroundColor Cyan
    Write-Host "$url/webhooks/tradingview/alert?secret=YOUR-WEBHOOK-SECRET" -ForegroundColor White
    Write-Host ""
    Write-Host "⚠️  Set TRADINGVIEW_WEBHOOK_SECRET in your .env file" -ForegroundColor Yellow
    Write-Host ""
}

Write-Host "🌐 ngrok Web UI: http://localhost:4040" -ForegroundColor Cyan
Write-Host ""
Write-Host "💡 Tips:" -ForegroundColor Cyan
Write-Host "  - This URL changes every restart (free tier)"
Write-Host "  - Upgrade to ngrok paid plan for reserved domains"
Write-Host "  - For production, use a real domain instead"
Write-Host ""
