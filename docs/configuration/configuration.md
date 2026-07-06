# Configuration Reference

## TradingView

| Key | Default | Description |
|-----|---------|-------------|
| `TradingView:WebhookSecret` | — | Shared secret validated on every incoming TradingView webhook request |

## PostgreSQL

| Key | Default | Description |
|-----|---------|-------------|
| `Postgres:ConnectionString` | — | Npgsql connection string for the trading database |

## Redis

| Key | Default | Description |
|-----|---------|-------------|
| `Redis:ConnectionString` | — | StackExchange.Redis connection string |
| `Redis:AlertQueueKey` | `mvp:alerts` | Redis list key used as the alert processing queue |

## Kraken Futures

| Key | Default | Description |
|-----|---------|-------------|
| `KrakenFutures:Environment` | — | `demo` for sandbox or `prod` for live trading |
| `KrakenFutures:BaseUrl` | — | REST API base URL (overrides `config/kraken-futures.json`) |
| `KrakenFutures:AuthBaseUrl` | — | Authentication endpoint base URL |
| `KrakenFutures:WebSocketUrl` | — | WebSocket feed URL |
| `KrakenFutures:TestSymbol` | — | Symbol used during integration tests |
| `KrakenFutures:ApiKey` | — | Active API key (resolved from Demo or Prod key based on environment) |
| `KrakenFutures:ApiSecret` | — | Active API secret |
| `KrakenFutures:DemoApiKey` | — | API key for the demo/sandbox environment |
| `KrakenFutures:DemoApiSecret` | — | API secret for the demo/sandbox environment |
| `KrakenFutures:ProdApiKey` | — | API key for the production environment |
| `KrakenFutures:ProdApiSecret` | — | API secret for the production environment |
| `KrakenFutures:TimeoutSeconds` | — | HTTP request timeout for Kraken API calls |
| `KrakenFutures:Cache:InstrumentsTtlSeconds` | — | Cache TTL for instruments list |
| `KrakenFutures:Cache:TickersTtlSeconds` | — | Cache TTL for ticker data |
| `KrakenFutures:Cache:CandlesTtlSeconds` | — | Cache TTL for candle/OHLCV data |
| `KrakenFutures:RateLimit:MaxCostPerWindow` | — | Maximum accumulated rate-limit cost per window |
| `KrakenFutures:RateLimit:WindowSeconds` | — | Duration of the rate-limit sliding window in seconds |
| `KrakenFutures:RateLimit:InstrumentsCost` | — | Rate-limit cost charged per instruments request |
| `KrakenFutures:RateLimit:TickersCost` | — | Rate-limit cost charged per tickers request |
| `KrakenFutures:RateLimit:CandlesCost` | — | Rate-limit cost charged per candles request |

## LLM / MCP

| Key | Default | Description |
|-----|---------|-------------|
| `OpenAI:ApiKey` | — | OpenAI API key used when `McpProvider:Provider=openai` or `auto` |
| `OpenAI:BaseUrl` | — | OpenAI-compatible base URL (override for proxies or Azure OpenAI) |
| `OpenAI:Organization` | — | OpenAI organization ID |
| `OpenAI:Project` | — | OpenAI project ID |
| `McpProvider:Provider` | — | `openai`, `local`, or `auto` (OpenAI with local fallback on 429) |
| `McpProvider:FallbackOnOpenAi429` | — | When `auto`, enables automatic fallback to local LLM on HTTP 429 |
| `LocalLlm:BaseUrl` | — | Base URL of the local LLM server (e.g. `http://host.docker.internal:1234/v1/`) |
| `LocalLlm:ApiKey` | — | API key for the local LLM server (if required) |
| `LocalLlm:ResponsesPath` | — | Path segment for the responses endpoint |
| `LocalLlm:ChatCompletionsPath` | — | Path segment for the chat completions endpoint |
| `LocalLlm:Mode` | — | `chat` or `responses` — selects which endpoint style to use |
| `LocalLlm:UseResponseFormat` | — | Whether to request structured JSON response format |
| `LocalLlm:ModelOverride` | — | Override the model identifier sent to the local server |

## Elliott Wave

| Key | Default | Description |
|-----|---------|-------------|
| `Elliott:BaseTimeframe` | — | Base chart timeframe used for wave detection |
| `Elliott:Parameters:PivotMethod` | — | Algorithm used to identify swing pivots |
| `Elliott:Parameters:Depth` | — | Minimum bar depth for pivot detection |
| `Elliott:Parameters:DeviationPct` | — | Minimum percentage deviation required to confirm a pivot |
| `Elliott:Parameters:MaxCandidates` | — | Maximum number of wave-count candidates to evaluate |
| `Elliott:TickSizeFallback` | — | Default tick size when no symbol-specific value is available |
| `Elliott:TickSizeOverrides` | — | Per-symbol tick size overrides (e.g. `Elliott:TickSizeOverrides:BTCUSD.P=0.5`) |

## Worker

| Key | Default | Description |
|-----|---------|-------------|
| `Worker:PollIntervalMs` | — | Milliseconds between alert queue polling iterations in `AlertWorker` |
| `Reconciliation:IntervalSeconds` | `60` | Seconds between reconciliation runs in `ReconciliationWorker` |

## Kill Switch

| Key | Default | Description |
|-----|---------|-------------|
| `KillSwitchApi:Secret` | — | Required secret for the `/api/killswitch/activate` and `/deactivate` endpoints |

## Environment Files

See [Environment Files Guide](environment-files.md) for the full `.env.*` file reference and which values belong in each environment.

## Sensitive Values

Never commit real credentials. Use `.env.*.local` files (gitignored) for local secrets. In production, inject via environment variables or Azure Key Vault (see [M18 ADRs](../adr/ADR-000-index.md)).
