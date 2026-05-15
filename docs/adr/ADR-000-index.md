# Architecture Decision Records — Index

This directory contains Architecture Decision Records (ADRs) for the AI-Assisted Trading Server.

ADRs follow the [Michael Nygard format](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions).
Each ADR is numbered sequentially and never deleted — superseded ADRs are marked as such.

## Status Legend

| Status | Meaning |
|--------|---------|
| `PROPOSED` | Under discussion — not yet approved for implementation |
| `ACCEPTED` | Approved — implementation may begin |
| `SUPERSEDED BY ADR-XXX` | Replaced by a later decision |
| `DEPRECATED` | No longer applicable but kept for history |
| `REJECTED` | Considered and explicitly rejected |

## ADR Registry

| ID | Title | Status | Milestone |
|----|-------|--------|-----------|
| [ADR-001](ADR-001-deterministic-gate-llm-advisory-separation.md) | Separate Deterministic Gating from LLM Advisory Layer | PROPOSED | M16 / M17 |
| [ADR-002](ADR-002-signal-snapshot-as-primary-llm-context.md) | SignalSnapshot as Primary LLM Context | PROPOSED | M17 |
| [ADR-003](ADR-003-microsoft-extensions-ai-adoption.md) | Adopt Microsoft.Extensions.AI for LLM Provider Abstraction | PROPOSED | M17 |
| [ADR-004](ADR-004-llm-confluence-advisor.md) | LLM Confluence Advisor — Multi-Timeframe Indicator Scoring | PROPOSED | M17 |
| [ADR-005](ADR-005-llm-stop-loss-advisor.md) | LLM Stop-Loss Advisor — Substantive Stop Reasoning | PROPOSED | M17 |
| [ADR-006](ADR-006-post-trade-llm-review-loop.md) | Post-Trade LLM Review Loop | PROPOSED | M17 |
| [ADR-007](ADR-007-market-regime-classifier.md) | Market Regime Classifier as Deterministic Pre-Filter | PROPOSED | M17 |
| [ADR-008](ADR-008-structured-output-adoption.md) | Adopt Structured Output via Microsoft.Extensions.AI | PROPOSED | M17 |
| [ADR-009](ADR-009-managed-identity-keyless-auth.md) | Managed Identity and Keyless Authentication for Azure Services | PROPOSED | M18 |
| [ADR-010](ADR-010-azure-key-vault-secrets-management.md) | Azure Key Vault for Secrets Management | PROPOSED | M18 |
| [ADR-011](ADR-011-network-security-vnet-private-endpoints.md) | Network Security: VNet Integration and Private Endpoints | PROPOSED | M18 |
| [ADR-012](ADR-012-database-security-least-privilege.md) | Database Security: Least Privilege and Encryption Hardening | PROPOSED | M18 |
| [ADR-013](ADR-013-defender-for-cloud-monitoring.md) | Microsoft Defender for Cloud and Centralized Monitoring | PROPOSED | M18 |
| [ADR-014](ADR-014-devops-security-pipeline.md) | DevOps Security Pipeline: Secret Scanning, Vulnerability Gates | PROPOSED | M18 |
| [ADR-015](ADR-015-container-security-hardening.md) | Container Security Hardening | PROPOSED | M18 |
| [ADR-016](ADR-016-ai-security-posture.md) | AI Security Posture: LLM Key Management and Prompt Injection | PROPOSED | M18 |
| [ADR-017](ADR-017-backup-and-recovery.md) | Backup and Recovery Plan | PROPOSED | M18 |
| [ADR-018](ADR-018-git-history-secret-removal.md) | Git History Secret Removal and Prevention | PROPOSED — URGENT | M18 |

## Decision Dependency Graph

### M16 / M17 — LLM Architecture

```
ADR-001 (gate/advisory separation)
  └─ enables ADR-004 (confluence advisor — advisory only, post-gate)
  └─ enables ADR-005 (stop-loss advisor — advisory only)
  └─ enables ADR-006 (post-trade review — async, non-blocking)
  └─ lowers risk of ADR-016 (prompt injection impact eliminated)

ADR-003 (Microsoft.Extensions.AI)
  └─ enables ADR-008 (structured output — built into MEA)
  └─ enables ADR-004 (uses IChatClient)
  └─ enables ADR-005 (uses IChatClient)

ADR-002 (SignalSnapshot as primary context)
  └─ shapes ADR-004 prompt design
  └─ shapes ADR-005 prompt design

ADR-007 (market regime classifier)
  └─ feeds into ADR-004 prompt as context hint
  └─ feeds into ADR-005 prompt as context hint
```

### M18 — Security

```
ADR-009 (Managed Identity) — FOUNDATIONAL
  └─ required by ADR-010 (Key Vault RBAC uses Managed Identity)
  └─ required by ADR-011 (App Service VNet integration identity)
  └─ required by ADR-012 (PostgreSQL Entra ID auth)

ADR-010 (Key Vault) — FOUNDATIONAL
  └─ required by ADR-016 (OpenAI API key stored in Key Vault)
  └─ enables ADR-011 (Key Vault private endpoint in VNet)

ADR-011 (VNet / Private Endpoints)
  └─ required before ADR-012 (DB accessible only via VNet after hardening)
  └─ enables Key Vault network lockdown (ADR-010)

ADR-013 (Defender for Cloud / Monitoring)
  └─ requires ADR-009, ADR-010, ADR-011 to be in place first (CSPM score meaningful)
  └─ provides audit log sink for ADR-016 (LLM audit records)

ADR-014 (DevOps pipeline)
  └─ independent — can be implemented in parallel with ADR-009..ADR-013
  └─ Workload Identity Federation option requires ADR-009 first

ADR-015 (Container hardening)
  └─ independent — Dockerfile changes only
  └─ Health endpoint (/health) required for HEALTHCHECK instruction

ADR-016 (AI security)
  └─ requires ADR-010 (Key Vault for API key)
  └─ risk reduced by ADR-001 (deterministic gate eliminates prompt injection risk; not a hard implementation dependency)

ADR-017 (Backup/Recovery)
  └─ note: ADR-011 (VNet) affects developer access to restored servers, not the restore itself — no hard dependency
  └─ informational — mostly IaC configuration
```

## Recommended Implementation Order

```
Phase 1 (foundation):    ADR-009 → ADR-010 → ADR-011 (in order, sequential)
Phase 2 (hardening):     ADR-012, ADR-015, ADR-014 (parallel)
Phase 3 (observability): ADR-013, ADR-016, ADR-017 (parallel)
```

## Related Milestones

- **M16**: DeterministicElliottAdjudicator — implements the gate side of ADR-001
- **M17**: LLM Architecture Redesign — implements ADR-002 through ADR-008
- **M18**: Azure Security Posture — implements ADR-009 through ADR-017
- **M9.7**: LLM adjudication persistence — provides the observability layer all ADRs depend on

## Critical Pre-M18 Action

⚠️ **Rotate the OpenAI API key immediately.** The key stored in `infra/terraform/terraform.tfvars` (gitignored, but plaintext on disk) must be rotated before implementing M18. See ADR-016 for details.
