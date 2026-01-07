#!/usr/bin/env bash
set -e

ENV=$1

if [[ -z "$ENV" ]]; then
    echo "Usage: ./scripts/switch-env.sh [simulated|demo|prod]"
    echo ""
    echo "Current environment:"
    if [[ -f config/execution.json ]]; then
        MODE=$(grep -o '"mode"[[:space:]]*:[[:space:]]*"[^"]*"' config/execution.json | cut -d'"' -f4)
        echo "  Mode: $MODE"
    else
        echo "  No config/execution.json found"
    fi
    exit 1
fi

case "$ENV" in
    simulated)
        echo "🔄 Switching to SIMULATED environment..."
        cp config/execution.simulated.json config/execution.json
        echo "✅ Switched to SIMULATED mode"
        echo "   - No real API calls"
        echo "   - All orders simulated"
        ;;
    demo)
        echo "🔄 Switching to DEMO environment..."
        cp config/execution.demo.json config/execution.json
        echo "✅ Switched to KRAKEN_DEMO mode"
        echo "   - Using Kraken demo API"
        echo "   - Paper money only"
        echo "   - Verify API credentials in .env"
        ;;
    prod)
        echo "⚠️  WARNING: Switching to PRODUCTION environment!"
        echo "   This will trade with REAL MONEY."
        read -p "   Type 'CONFIRM' to proceed: " CONFIRM
        if [[ "$CONFIRM" != "CONFIRM" ]]; then
            echo "❌ Cancelled"
            exit 1
        fi
        cp config/execution.prod.json config/execution.json
        echo "✅ Switched to KRAKEN_LIVE mode"
        echo "   ⚠️  PRODUCTION MODE - REAL MONEY AT RISK"
        echo "   - Using production Kraken API"
        echo "   - Real trades, real money"
        echo "   - Update production API credentials in .env"
        ;;
    *)
        echo "❌ Invalid environment: $ENV"
        echo "   Valid options: simulated, demo, prod"
        exit 1
        ;;
esac

echo ""
echo "Current config/execution.json:"
cat config/execution.json | grep -E '(mode|BaseUrl|warning|note)' || true
