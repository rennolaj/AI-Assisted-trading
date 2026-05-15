# ADR-016 — AI Security Posture: LLM API Key Management and Prompt Injection Protection

| Field | Value |
|-------|-------|
| **Status** | PROPOSED |
| **Milestone** | M18.8 |
| **Date** | 2025-01 |
| **Author** | Architecture Review |
| **Deciders** | Project Owner |
| **MCSB v2 Domains** | AI-1, AI-2 (Artificial Intelligence Security — MCSB v2 new domain) |
| **Depends on** | ADR-010 (Key Vault for API key storage), ADR-001 (LLM is advisory only — not decision gate) |

---

## Context

This system integrates two LLM providers:
1. **OpenAI API** (`gpt-4o-mini` or similar) — adjudication and analysis
2. **Local Ollama** (`llama3.1:latest`) — local alternative, same prompts

After M16 (ADR-001), the LLM is **advisory only** — it cannot cause a trade to execute. However, LLM misuse still carries risks:

**Financial risk (API cost):**
- The `openai_api_key` in `terraform.tfvars` is a live key with no usage cap set
- Compromised key = unbounded API spend
- Key is stored as plaintext on disk (gitignored but unencrypted)

**Security risk (prompt injection):**
- The adjudication prompt (`prompts/adjudicate-elliott.md`) includes external data: `candidateWave.symbol`, `candidateWave.waveLabel`, `ruleViolations[]` content
- Elliott wave analysis data comes from market data APIs (Kraken) — not user-controlled, but could theoretically be manipulated if the market data feed were poisoned
- MCSB v2 AI-2 ("Protect AI systems from adversarial attacks") covers prompt injection

**Audit and observability risk:**
- No logging of LLM request/response for audit or debugging purposes
- Cannot determine if LLM produced anomalous outputs over time
- Cannot enforce spend limits or detect runaway LLM calls

**MCSB v2 AI domain** (new in MCSB v2) covers:
- AI-1: Establish an AI security governance model
- AI-2: Protect AI systems from adversarial inputs (prompt injection, data poisoning)

---

## Decision

**Rotate the exposed OpenAI API key immediately. Store the new key in Azure Key Vault (ADR-010). Implement LLM call audit logging to Log Analytics (M18.5). Add prompt injection threat model documentation. Enforce per-call token budget limits in the gateway.**

After M16/ADR-001, the LLM cannot gate trades — this reduces the criticality of prompt injection significantly. The focus is on **cost control** and **audit trail**, not on preventing LLM-induced trade execution (which is already blocked by the deterministic gate).

---

## Immediate Actions (Pre-M18 Implementation)

1. **Rotate the OpenAI API key** — the key in `terraform.tfvars` has been visible to anyone with filesystem access to the development machine. Rotate it at https://platform.openai.com/api-keys before any M18 work begins.
2. **Set OpenAI usage limits** — at https://platform.openai.com/settings/organization/limits: set monthly hard limit to $10 (appropriate for a demo/MVP system).
3. **Move key to Key Vault** (M18.2 / ADR-010) — the new rotated key goes directly into Key Vault, never back into a file.

---

## LLM Audit Logging

All LLM calls must be logged with sufficient detail for security review and cost attribution. This does NOT require logging the full prompt or response (which could include sensitive financial data) — only metadata.

### Audit Record Structure

```csharp
// Add to the MEA gateway wrapper (ADR-003)
internal sealed record LlmAuditRecord(
    string CallId,              // Guid.CreateVersion7()
    string Provider,            // "openai" | "local"
    string Model,               // "gpt-4o-mini" | "llama3.1:latest"
    string UseCase,             // "adjudication" | "confluence" | "stop-loss" | "post-trade"
    string Symbol,              // Trading symbol (non-sensitive)
    int PromptTokens,           // Token count — from API response
    int CompletionTokens,       // Token count — from API response
    string Outcome,             // "ALLOW" | "REJECT" | "confluence:0.73" | error message
    TimeSpan Latency,           // End-to-end call duration
    bool IsAdvisory,            // Always true after ADR-001
    DateTimeOffset Timestamp    // from TimeProvider
);
```

Audit records are written to:
1. **Application logger** (`ILogger<T>` at `Information` level with structured data) → Log Analytics (M18.5)
2. **In future**: Optionally to a `llm_audit` table in PostgreSQL for long-term retention and trend analysis

### Token Budget Enforcement

```csharp
// In MEA gateway wrapper — enforce per-call token limit
private const int MaxPromptTokens = 4000;   // guard against runaway prompt construction
private const int MaxCompletionTokens = 500; // LLM responses should be brief structured JSON

var options = new ChatOptions
{
    MaxOutputTokens = MaxCompletionTokens,
    // Note: prompt token limit requires pre-call estimation or model-specific context limits
};
```

---

## Prompt Injection Threat Model

### Threat Surface Analysis

The LLM receives inputs from two paths:

**Path 1: Structured data from Elliott analysis (low risk)**
```
candidateWave.symbol         = "XBTUSD"              — from Kraken API (fixed format)
candidateWave.waveLabel      = "W3" | "W5" | ...    — from internal enum
candidateWave.direction      = "Long" | "Short"      — from internal enum
ruleViolations               = [] | ["rule text"]    — from internal string constants
```
The `ruleViolations` field contains strings from internal rule definitions. If rule text were user-controlled, it would be a high-risk injection point. In the current implementation, rule violations are generated by internal code (`ElliottWaveAnalyzer`) — **not from external user input**. Risk: LOW.

**Path 2: SignalSnapshot indicator values (very low risk)**
```
rsi = 58.3                   — numeric value from Kraken OHLC data
macd = 0.0012                — numeric value from Kraken OHLC data
```
Numeric values cannot contain prompt injection instructions. Risk: NEGLIGIBLE.

**Path 3: Local LLM with `ExtractJsonFromResponse()` (moderate risk)**
The local LLM gateway (`LocalLlmMcpGateway.cs`) parses model output that includes special tokens like `<|channel|>final<|message|>`. If a poisoned model file were installed, it could emit arbitrary JSON claiming `{"decision": "ALLOWLONGW3"}`. After ADR-001, this is advisory only and the deterministic gate would still validate the trade independently. Risk: MODERATE (pre-ADR-001) → LOW (post-ADR-001).

### Mitigations in Place (post-M16/M17)

1. **ADR-001 (deterministic gate)**: LLM cannot cause trade execution regardless of output — eliminates direct financial impact of prompt injection
2. **ADR-008 (structured output)**: `GetResponseAsync<T>()` enforces JSON schema on LLM output — unrecognized fields are ignored, injection cannot expand the response schema
3. **Enum-based decision**: LLM returns one of 5 known enum values — arbitrary text injection in the response cannot produce an unexpected decision

### Residual Risks

1. **Kraken market data poisoning** → unusual indicator values → LLM gives bad confluence advice → human makes poor manual decision. Mitigated by: confluence score is advisory, not executable.
2. **OpenAI API key compromise** → attacker uses key for unrelated requests → financial cost. Mitigated by: usage limits + Key Vault storage + audit logging detecting unusual call patterns.
3. **Local model file substitution** → attacker replaces Ollama model weights. Mitigated by: model integrity check on startup (Ollama SHA verification); non-root container (ADR-015) makes model file manipulation harder.

---

## Governance Documentation

Per MCSB v2 AI-1 ("Establish an AI security governance model"), the following must be documented:

| Item | Value |
|------|-------|
| AI system purpose | Advisory trade signal enrichment — NOT automated execution |
| Human oversight | Every trade plan reviewed by deterministic gate before execution |
| Data retention | LLM prompts/responses NOT stored (audit metadata only) |
| Model change process | Model changes require PR review + integration test pass |
| Bias/fairness scope | N/A — system provides market analysis, not decisions about people |
| Regulatory scope | Trading regulations (MiFID II, CFTC) — LLM is advisory tool only |

---

## Consequences

### Positive
- **Cost control**: Hard monthly limit on OpenAI prevents runaway spend from compromised key or bug
- **Audit trail**: Structured LLM audit log enables detection of anomalous call patterns
- **Key rotation**: Immediate rotation of exposed key eliminates active risk
- **MCSB compliance**: AI-1 (governance documented), AI-2 (prompt injection threat model documented)
- **Defense-in-depth**: Token budget limits prevent LLM calls from becoming excessively large

### Negative / Tradeoffs
- **Audit logging overhead**: ~100 bytes per LLM call log entry — negligible at trading server call rates
- **Key rotation downtime**: Rotating the OpenAI key requires a brief deployment to update Key Vault value (zero-downtime with Key Vault versioning)

### Neutral
- Prompt injection risk is inherently low in this system due to ADR-001 (deterministic gate) and enum-constrained LLM output

---

## Alternatives Considered

### A1: Azure OpenAI Service instead of OpenAI.com
- Azure OpenAI supports Managed Identity authentication (no API key)
- Provides enterprise SLA, VNet-accessible endpoint, content filtering
- Cost: same model pricing, but data residency in Azure region
- **Deferred to M19**: migration requires model availability check (Azure OpenAI lags OpenAI.com by weeks on new models) and application code changes to switch endpoint

### A2: Azure AI Content Safety for prompt injection detection
- Microsoft service that analyzes prompts for jailbreak attempts before sending to LLM
- $1/1000 text records — low cost
- **Deferred**: Low risk given ADR-001 deterministic gate; consider if LLM gains more autonomy in future milestones

### A3: Open source prompt firewall (Rebuff, LLM Guard)
- OSS tools that analyze prompts for injection patterns
- Self-hosted, no additional API cost
- **Rejected for now**: overkill given current threat model; consider if prompt inputs become user-controlled

---

## References
- MCSB v2 AI Security: https://learn.microsoft.com/en-us/security/benchmark/azure/mcsb-v2-ai-security
- OWASP Top 10 for LLM Applications: https://owasp.org/www-project-top-10-for-large-language-model-applications/
- Azure OpenAI security baseline: https://learn.microsoft.com/en-us/security/benchmark/azure/baselines/azure-openai-security-baseline
- OpenAI API key best practices: https://platform.openai.com/docs/guides/safety-best-practices
