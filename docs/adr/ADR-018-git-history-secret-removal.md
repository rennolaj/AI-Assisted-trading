# ADR-018 — Git History Secret Removal and Prevention

| Field | Value |
|-------|-------|
| **Status** | PROPOSED — URGENT |
| **Milestone** | M18.10 |
| **Date** | 2025-01 |
| **Author** | Architecture Review |
| **Deciders** | Project Owner |
| **MCSB v2 Domains** | DS-2 (DevOps Security — source code secret hygiene), IM-8 |
| **Urgency** | HIGH — secret is currently readable in `git log -p` by anyone with repo access |

---

## Context

A TradingView webhook verification secret was committed to `.env.example` in two commits
and has been present in the public git history since commit `1c0df4e`:

```
Affected secret: TRADINGVIEW_WEBHOOK_SECRET=REDACTED_WEBHOOK_SECRET
Affected file:   .env.example
First appeared:  commit 1c0df4e4 — "feat(m6): add risk engine, execution flow, and status docs"
Removed from file: commit 2ef44d17 — but still readable in history
Total commits in history: 72
```

**All other secrets (OpenAI API key, Kraken API keys, ngrok token) are NOT in git history** — they are only in gitignored local files (`terraform.tfvars`, `parameters.local.json`).

The TradingView webhook secret is used to verify the HMAC signature on incoming TradingView alerts. If an attacker knows this secret, they can forge webhook requests that appear to come from TradingView, injecting fake trading signals. After M16 (deterministic gate), such injections would still be validated by the Elliott wave logic — but they could still cause alerts to enter the processing queue.

---

## Why History Rewriting is Necessary

Alternative options and why they are insufficient:

| Option | Why Insufficient |
|--------|-----------------|
| Delete the commit | Git does not support deleting individual commits — the history is a chain |
| `git revert` | Creates a new commit that undoes the change, but old commit still readable in history |
| Make repo private | Removes public access but the secret is still in history for all collaborators |
| Rotate the secret only | Correct first step, but old secret remains in history as an artifact |
| GitHub "Delete file" UI | Only removes from current branch HEAD, not from history |

**Correct approach**: Rewrite git history using `git filter-repo` (GitHub's officially recommended tool, successor to BFG Repo Cleaner). This physically removes the secret string from every commit in the history, then force-pushes to all remotes.

---

## Decision

**Rotate the TradingView webhook secret first. Then rewrite git history using `git filter-repo` to replace the secret value with `REDACTED` in all affected commits. Force-push to remote. Request GitHub cache purge. Enable GitHub Secret Push Protection to prevent recurrence.**

The order matters: **ROTATE FIRST, then rewrite**. Rewriting without rotating leaves the old secret active — anyone who downloaded the history before rewrite still has a valid secret.

---

## Step-by-Step Procedure

### Phase 1: Pre-Rewrite (Do Before Touching Git)

**Step 1.1 — Rotate the TradingView webhook secret**
```
1. Log in to TradingView
2. Go to: Alerts → Webhooks (or Pine Script webhook settings)
3. Generate a new webhook secret (random UUID or long hex string)
4. Update all active alerts to use the new secret
5. Update App Service config (or .env file) with the new secret value
6. Verify at least one webhook fires successfully with the new secret
7. Note the OLD secret value for use in Step 2.2 (redaction pattern)
```

**Step 1.2 — Notify all collaborators**

Before force-pushing, any collaborator who has cloned the repo will need to re-clone after the rewrite. Their local branches will be based on the old SHAs, which will no longer exist on the remote.

```
Message: "Warning: I am about to rewrite the git history to remove a committed secret.
After I push, you must: git fetch --all && git reset --hard origin/main
Or simply re-clone the repository. Your local commits (if any) that aren't pushed will need to be cherry-picked."
```

**Step 1.3 — Back up the current state**
```bash
# Create a full backup of the current repo state before rewriting
cd /Users/jrennola/Hobby/AI-Assisted
git bundle create /tmp/AI-Assisted-backup-before-rewrite.bundle --all
echo "Backup created: /tmp/AI-Assisted-backup-before-rewrite.bundle"
# Verify backup
git bundle verify /tmp/AI-Assisted-backup-before-rewrite.bundle
```

---

### Phase 2: Rewrite History

**Step 2.1 — Install git-filter-repo**
```bash
brew install git-filter-repo
# Verify
git filter-repo --version
```

**Step 2.2 — Create a replacements file**
```bash
# Create a file listing all strings to replace
# Format: literal==>replacement  (no regex needed for exact string match)
cat > /tmp/git-secrets-replace.txt << 'EOF'
REDACTED_WEBHOOK_SECRET==>REDACTED_WEBHOOK_SECRET
EOF
```

**Step 2.3 — Dry run first (non-destructive)**
```bash
cd /Users/jrennola/Hobby/AI-Assisted

# IMPORTANT: git filter-repo requires a clean repo (no uncommitted changes)
git status  # must show clean

# Dry run — shows what would change without modifying anything
git filter-repo --replace-text /tmp/git-secrets-replace.txt --dry-run 2>&1 | head -50
```

**Step 2.4 — Execute the rewrite**
```bash
# This PERMANENTLY rewrites all commits. Ensure backup exists (Step 1.3) before running.
git filter-repo --replace-text /tmp/git-secrets-replace.txt

# Verify the secret is gone from all history
git log --all -p | grep "1b0a08c4"
# Expected output: (empty — no matches)

# Verify .env.example in current HEAD looks correct
git show HEAD:.env.example | grep -i "webhook"
# Expected: TRADINGVIEW_WEBHOOK_SECRET=your-webhook-secret-here  (placeholder, not real value)
```

**Step 2.5 — Re-add the remote (git filter-repo removes it as a safety measure)**
```bash
git remote add origin git@github-personal:rennolaj/AI-Assisted-trading.git
git remote -v  # verify
```

---

### Phase 3: Push and Remote Cleanup

**Step 3.1 — Force push all branches**
```bash
# Force push main branch
git push origin main --force

# Force push all other branches (if any exist)
git push origin --force --all

# Force push all tags (if any contain the secret — unlikely but safe to include)
git push origin --force --tags
```

**Step 3.2 — Request GitHub garbage collection (critical)**

Even after force-push, GitHub caches dangling objects (unreachable commits) for up to 90 days. During this window, anyone who knows the old commit SHA can still access the old content.

Request cache purge via GitHub Support:
```
Go to: https://support.github.com/contact
Subject: "Request removal of cached git objects after history rewrite"
Body: "I have rewritten the git history of repository rennolaj/AI-Assisted-trading to remove 
an accidentally committed secret. I have force-pushed the rewritten history. Please run 
garbage collection to remove any dangling objects and cached references to the old history.
The affected repository is: https://github.com/rennolaj/AI-Assisted-trading"
```

**Important**: Do NOT make a private repository temporarily public to facilitate the cache purge request. If the repository is private, GitHub will still process the purge request — temporary public visibility is not required and would counterproductively expose the committed secret to anyone watching GitHub during that window.

**Step 3.3 — Verify on GitHub**
```bash
# After GitHub processes the purge, verify the secret is gone on the remote
# Check a specific old commit SHA that contained the secret
git fetch origin
# The old SHA (1c0df4e43dfec2565daf50052247d78cb4e5718f) should no longer exist
git cat-file -e 1c0df4e43dfec2565daf50052247d78cb4e5718f 2>/dev/null && echo "EXISTS (bad)" || echo "GONE (good)"
```

---

### Phase 4: Prevention — GitHub Secret Push Protection

**Step 4.1 — Enable GitHub Secret Scanning and Push Protection**
```
1. Go to: https://github.com/rennolaj/AI-Assisted-trading/settings/security_analysis
2. Enable: Secret scanning ✅
3. Enable: Push protection ✅ (blocks pushes that contain detected secrets)
4. Configure custom patterns:
   - Pattern name: "TradingView webhook secret"
   - Pattern: [0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}-[0-9a-f]{32}
   (matches UUID + hex suffix format used in this project)
```

GitHub Secret Push Protection supports detection of:
- OpenAI API keys (built-in pattern: `sk-proj-*`)
- Generic high-entropy strings (optional)
- Custom patterns (regex-based, for project-specific formats)

**Step 4.2 — Add pre-commit hook (local defense)**
```bash
# .git/hooks/pre-commit — install in repo
cat > .git/hooks/pre-commit << 'HOOK'
#!/bin/bash
# Block commits containing known secret patterns
PATTERNS=(
    "sk-proj-[A-Za-z0-9_-]{40,}"          # OpenAI API key
    "x5LHDu0t[A-Za-z0-9]+"               # Kraken prod API key prefix
    "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}-[0-9a-f]{32}"  # TradingView webhook format
)

for pattern in "${PATTERNS[@]}"; do
    if git diff --cached | grep -qP "$pattern"; then
        echo "ERROR: Possible secret detected in staged changes (pattern: $pattern)"
        echo "Run: git diff --cached | grep -P '$pattern' to inspect"
        echo "If this is a false positive, use: git commit --no-verify"
        exit 1
    fi
done
exit 0
HOOK
chmod +x .git/hooks/pre-commit
```

Note: `.git/hooks/` is not committed to the repository. Document this hook setup in README or CONTRIBUTING.md so other developers install it.

**Step 4.3 — Add trufflehog to CI (ADR-014 / M18.6)**

The CI pipeline proposed in ADR-014 includes Trufflehog secret scanning on every PR. This provides a server-side safety net that catches secrets even if the pre-commit hook is bypassed with `--no-verify`.

---

## Consequences

### Positive
- **Secret removed from history**: Old webhook secret no longer accessible via `git log`
- **Collaborator safety**: Anyone who re-clones after the purge cannot find the old secret
- **Prevention active**: Push protection + pre-commit hook creates defense-in-depth
- **MCSB DS-2 compliance**: Source code repository is free of committed credentials

### Negative / Tradeoffs
- **All commit SHAs change**: Every commit from `1c0df4e` onward gets a new SHA — all PR references, issue references that mention commit SHAs become stale
- **Force push required**: Breaks local clones — all collaborators must re-clone or hard reset
- **GitHub cache delay**: Even after force push, old content accessible for up to 90 days via direct SHA until GitHub purges it

### Neutral
- The TradingView webhook secret is not a financial credential — its compromise allows fake signal injection but not account access. The deterministic gate (M16) provides additional protection even if fake signals enter the queue.
- After rotation (Step 1.1), the old secret in history is effectively inert — rewriting is belt-and-suspenders hygiene

---

## If Repo Must Stay Functional During Rewrite

If the system is actively trading during the rewrite window:
1. Ensure new webhook secret is active in App Service config BEFORE rewriting
2. Rewrite can be done offline — App Service continues with new secret during git rewrite
3. Force push does not affect the running App Service containers

---

## References
- git filter-repo (official GitHub recommendation): https://github.com/newren/git-filter-repo
- GitHub: Removing sensitive data from a repository: https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository
- GitHub Secret Push Protection: https://docs.github.com/en/code-security/secret-scanning/push-protection-for-repositories-and-organizations
- MCSB v2 DevOps Security DS-2: https://learn.microsoft.com/en-us/security/benchmark/azure/mcsb-v2-devops-security
