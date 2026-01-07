# Mvp.Trading.Execution.Tests

Integration tests for the Kraken Futures trading provider.

## Test Types

### Integration Tests (`KrakenFuturesTradingIntegrationTests.cs`)
Tests actual order placement against Kraken Futures Demo API:
- ✅ API key validation (`CheckApiKey_WithValidCredentials_ReturnsSuccess`)
- ✅ Limit order placement and cancellation (`SendOrder_LimitOrder_ReturnsOrderAck`, `CancelOrder_WithValidOrderId_ReturnsSuccess`)
- ✅ Get open orders (`GetOpenOrders_ReturnsOrderList`)
- ✅ Dead-man's switch configuration (`CancelAllOrdersAfter_WithValidTimeout_ReturnsSuccess`, `CancelAllOrdersAfter_DisableWithZero_ReturnsSuccess`)

**⚠️ Requires Kraken Futures Demo API credentials**

## Running Integration Tests

Integration tests are **disabled by default** and require API credentials.

### 1. Set Environment Variables

```bash
export KRAKEN_FUTURES_TRADING_TESTS=1
export KRAKEN_FUTURES_API_KEY="your-demo-api-key"
export KRAKEN_FUTURES_API_SECRET="your-demo-api-secret"
export KRAKEN_FUTURES_BASE_URL="https://demo-futures.kraken.com/derivatives"  # Optional
export KRAKEN_FUTURES_AUTH_BASE_URL="https://demo-futures.kraken.com/derivatives"  # Optional
export KRAKEN_FUTURES_TEST_SYMBOL="PF_XBTUSD"  # Optional, defaults to BTC futures
```

### 2. Run Integration Tests

```bash
dotnet test --filter "FullyQualifiedName~KrakenFuturesTradingIntegrationTests"
```

### 3. Getting Demo API Credentials

1. Sign up at [Kraken Futures Demo](https://demo-futures.kraken.com/)
2. Create API keys with trading permissions
3. Use the demo environment URLs (not production)

**Note**: Demo accounts use paper money and won't affect real funds.

## Test Coverage

### ✅ Covered Scenarios
- ✅ API key validation
- ✅ Limit order placement
- ✅ Order cancellation
- ✅ Get open orders
- ✅ Dead-man's switch (cancel all orders after timeout)

### 🔜 Future Test Coverage (M7 - Chaos Testing)
- Network failure scenarios (integration with Toxiproxy)
- Stop-loss and take-profit order placement
- Partial fills handling
- Order rejection scenarios (invalid symbols, insufficient margin)
- Rate limit exhaustion
- Exchange API timeouts
- Worker crash recovery
- Multi-order orchestration (entry + stop + take-profits)
- Reconciliation loop validation

## Running All Tests

To run all tests in the solution (integration tests will skip without credentials):

```bash
dotnet test
```

## CI/CD Integration

Integration tests are skipped in CI unless credentials are configured:

```yaml
# Example .github/workflows/test.yml (if using GitHub Actions)
- name: Run All Tests
  run: dotnet test
  # Integration tests automatically skip without credentials

# Optional: Run integration tests in scheduled job with credentials
- name: Run Integration Tests (scheduled)
  if: github.event_name == 'schedule'
  run: dotnet test
  env:
    KRAKEN_FUTURES_TRADING_TESTS: '1'
    KRAKEN_FUTURES_API_KEY: ${{ secrets.KRAKEN_FUTURES_API_KEY }}
    KRAKEN_FUTURES_API_SECRET: ${{ secrets.KRAKEN_FUTURES_API_SECRET }}
```

## Safety Considerations

All integration tests:
- Use Kraken **Demo** environment by default
- Place orders far from market price to avoid fills
- Cancel all orders after each test (cleanup in `finally` blocks)
- Use minimum position sizes
- Include test-specific client order IDs for tracking

**Never run these tests against production API endpoints.**
