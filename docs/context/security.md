# Security Context

Load this for Azure security posture, secrets, network isolation, database access, AI security, backup, or git hygiene work.

## Source Documents

- `docs/milestones/m18-security/m18-technical-requirements.md`
- `docs/adr/ADR-009-managed-identity-keyless-auth.md`
- `docs/adr/ADR-010-azure-key-vault-secrets-management.md`
- `docs/adr/ADR-011-network-security-vnet-private-endpoints.md`
- `docs/adr/ADR-012-database-security-least-privilege.md`
- `docs/adr/ADR-013-defender-for-cloud-monitoring.md`
- `docs/adr/ADR-014-devops-security-pipeline.md`
- `docs/adr/ADR-015-container-security-hardening.md`
- `docs/adr/ADR-016-ai-security-posture.md`
- `docs/adr/ADR-017-backup-and-recovery.md`
- `docs/adr/ADR-018-git-history-secret-removal.md`

## Boundary

`INFRA_FREEZE` applies. Do not change Terraform or Bicep unless the repository policy changes.
