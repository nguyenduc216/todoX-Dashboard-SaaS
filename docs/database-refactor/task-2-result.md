# Task 2 AI Core Database Reset Report

## 1. Scope
- Branch: `refactor/ai-core-reset`
- Database: `todo_saas`
- Environment gate: verified non-production before any destructive reset work
- Commit target: separate Task 2 commit only
- No deploy, no merge to `main`

## 2. Backup and rollback
- Backup folder: `D:\todoX\db-backups\ai-core-reset`
- Dump: `backup-before-ai-core-reset-20260722-1413.dump`
- Schema backup: `backup-before-ai-core-reset-schema-20260722-1413.sql`
- SHA256 dump: `124E5E3721A15B66E8A381D7B51F979B0FBD8F07154056AC6B3229AF3BA7D492`
- SHA256 schema: `0A05FFB5BA531CC62DA665F253F56E47B97EC89ECE45435E0DB9F3283AB69687`
- Rollback command:
  `pg_restore -h <host> -p 5432 -U postgres -d todo_saas --clean --if-exists D:\todoX\db-backups\ai-core-reset\backup-before-ai-core-reset-20260722-1413.dump`

## 3. Pre-checks
- Confirmed branch is `refactor/ai-core-reset`
- Confirmed database is `todo_saas`
- Confirmed non-production context
- Confirmed worker and render job activity were not active
- Confirmed backup integrity by SHA256
- Confirmed provider/model export state before reset

## 4. SQL execution order
Executed in dependency order with verification after the core reset:
1. inventory
2. clear test data
3. drop legacy tables
4. normalize provider
5. create provider accounts
6. create provider account credentials
7. create provider account leases
8. create provider balance ledger
9. rebuild render core
10. rebuild provider usage
11. rebuild AI billing
12. rebuild Dance Sell
13. indexes and constraints
14. seed preserved providers
15. seed provider accounts
16. seed Dance Sell models
17. verify schema
18. verify provider seed
19. verify runtime contract

## 5. Before / after
- Providers preserved: 5
- Capabilities exported before reset: 35
- Final capabilities after reset: 36
- Reason for the delta: the KIE reference-image model required by Dance Sell was added, and the legacy duplicate KIE motion-control row was consolidated out

## 6. Key structural changes
- Provider structure now supports multiple accounts and per-account credentials
- Account leasing is modeled for concurrent threads
- Render core is shared and normalized
- Usage logging is generalized to credit, token, and second units
- Billing is generalized for AI image and video flows
- Dance Sell is kept compact and task-specific
- KIE seed uses credential reference `KIE_API_KEY` only; no secret was stored

## 7. Tables used
- Provider tables: provider, provider_capability, provider_account, provider_account_credential, provider_account_lease
- Render tables: render_job, render_job_step, render_job_event, render_job_input, render_job_artifact
- Usage and billing: ai_provider_usage, ai_billing records, ai_provider_attempts, balance ledger
- Dance Sell tables: dance_sell_job and related compact business tables
- System/config tables: provider metadata and seeding tables

## 8. Verification
- Verified schema objects, constraints, indexes, orphan rows, and secret leakage
- Verified no legacy image-only billing tables remain
- Verified KIE provider and both Dance Sell KIE models are enabled
- Verified provider account reference and lease fields
- Verified orphan checks returned zero

## 9. Output artifacts
- `providers-before-after.csv`
- `capabilities-before-after.csv`
- `providers-after.csv`
- `capabilities-after.csv`
- `provider-accounts-after.csv`
- `row-counts-before-reset.csv`
- `row-counts-after-reset.csv`
- `database-object-after.csv`
- `indexes-after.csv`
- `constraints-after.csv`
- `created-objects.csv`
- `dropped-objects.csv`
- `sql-execution-log.csv`
- `task-2-runtime-breakage-map.csv`
- `task-2-runtime-breakage-map.md`
- `task-2-verification-output.txt`

## 10. Notes
- The first seed pass for preserved providers failed because an existing capability constraint rejected `credits`; that was corrected and rerun successfully
- Runtime breakage remains for later Task 3 cleanup, but schema reset and validation passed
- No deploy or production merge was performed
