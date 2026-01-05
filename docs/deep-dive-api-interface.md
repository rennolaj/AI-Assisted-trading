# Deep Dive: API Interface Spec

## Why this doc
This captures the implementation-ready interfaces and DTO shapes that should be treated as source-of-truth when starting the MVP. It focuses on the clean sections extracted from the PDF and avoids the garbled fragments.

## Core Conventions
- All service-to-service calls return a Result envelope with an Error payload on failure.
- CorrelationId is a GUID per pipeline run; TradingView alerts must map to an IdempotencyKey.
- JSON over HTTP for internal services; MCP uses JSON-RPC over stdio or Streamable HTTP.

## Result and Error Envelope
- Result<T>: { Ok, Value, Error }
- Error: { Code, Message, Meta }

## WebhookIngress API
- Route: POST /webhooks/tradingview/{secret}
- Behavior: ACK within 3 seconds after enqueue
- DTO: AlertEvent
  - AlertId (Guid)
  - ReceivedAtUtc (DateTimeOffset)
  - Source: "tradingview"
  - IdempotencyKey (string)
  - TradingViewFields: { Ticker, Exchange, Interval, Close?, Volume? }
  - IntentFields: { DirectionHint, SymbolHint, Reason }
  - RawPayload (string)

## Queue Abstraction
- IEventQueue
  - EnqueueAsync<T>(topic, message, idempotencyKey, ct)
  - DequeueAsync<T>(topic, ct) -> async stream
  - AckAsync(receiptHandle, ct)
- QueuedMessage<T>: ReceiptHandle, Payload, EnqueuedAtUtc

## Kraken Futures Connector
- Market data (read-only)
  - GetInstrumentsAsync
  - GetTickersAsync
  - GetOhlcvAsync(symbol, timeframe, lookbackBars)
- Trading (executor only)
  - CheckV3ApiKeyAsync
  - SendOrderAsync
  - GetOpenOrdersAsync
  - CancelOrderAsync
  - CancelAllOrdersAfterAsync(timeoutSeconds)

## Indicator Engine
- ComputeAsync(timeframes, parameters) -> SignalSnapshot
- IndicatorParameters: RSI/StochRSI/MACD/VolumeRule

## Elliott Engine
- GenerateCandidatesAsync(baseTimeframe, parameters) -> ElliottCandidates
- ElliottParameters: PivotMethod, Depth, DeviationPct, MaxCandidates

## MCP Tools
- AdjudicateElliottAsync(input) -> LlmDecision
- SuggestStopLossAsync(input) -> StopLossSuggestion
- Inputs are bounded to Snapshot, Candidates, and RiskPolicy

## Risk Engine
- BuildPlan(AlertEvent, SignalSnapshot, ElliottCandidates, LlmDecision, RiskPolicy) -> TradePlan
- Enforces hard limits; LLM cannot override policy

## Immediate Implementation Slice (Suggested)
1) Contracts + Schemas (shared DTOs + JSON Schemas)
2) WebhookIngress + Queue stub (fast ACK + idempotency check)
3) Market Data connector (read-only Kraken) with rate-limit budget
4) Deterministic Indicator snapshot (fixtures for tests)

## Open Questions to Resolve Early
- Timeframes and IndicatorParameters defaults
- RiskPolicy hard limits and AllowedSides
- Queue choice (Redis Streams, RabbitMQ, SQS)
- Storage choice for audit chain (Postgres recommended)
