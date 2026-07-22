# Phase 5.1 Production Readiness

## Executive Decision

GO for local source/database hardening completion. HOLD for production deployment until non-paid environment smoke, YEScale MCP verification, and approved paid provider smoke are completed.

## Branch And Commit

- Branch: `refactor/ai-core-reset`
- Base commit: `9cfb89e757c04b23e03791e892508d6c816a107f`
- New commit: see final handoff and `git log -1 --format=%H` after this report is committed.

## Database Migration Status

Standalone SQL scripts `43`, `44`, and `45` were executed on `todo_saas` using a temporary Npgsql runner because `psql` is unavailable in PATH.

Output file:

- `docs/database-refactor/phase-5-1-sql-43-45-output.txt`

Result:

- `43_phase5_1_billing_hardening.sql`: OK.
- `44_phase5_1_completion_hardening.sql`: OK.
- `45_verify_phase5_1_prod_readiness.sql`: OK.
- Verification notice: `Phase 5.1 production-readiness SQL verification passed.`

## Generic Billing Ownership

Generic billing owns production write logic through `AiBillingService` and `AiBillingRepository`. `AiImageBillingService` is an obsolete adapter.

## Refund And Reconciliation

Refund and reconciliation moved into `AiBillingRepository` with transaction scopes, row locks, advisory locks, and idempotency-oriented token transaction inserts.

## Provider Coverage Matrix

Provider source/DB hardening coverage is complete for the requested credential resolver paths. See `phase-5-1-provider-coverage.csv`.

## Credential Access Review

KIE, YEScale, OpenRouter, and Vertex direct credential paths were migrated to provider account resolver usage. Source scan found no remaining direct production credential path in source/config files under review.

## Lease And Concurrency Verification

PostgreSQL integration test `ProviderAccountConcurrencyIntegration_SkipLockedAndLeaseLimits` is enabled and passing.

## Wallet Locking Verification

PostgreSQL integration test `WalletLockingIntegration_ConcurrentSameLogicalRequest` is enabled and passing.

## Callback/Poll Race Verification

PostgreSQL integration test `CallbackPollRaceIntegration_CompletesExactlyOnce` is enabled and passing.

## Operation Logs

Operation log service exists from Task 5. Full UI/API smoke was not executed in this turn.

## Provider Diagnostics

Diagnostics service and redaction exist from Task 5. Full live smoke was not executed.

## Non-Paid Smoke

Build/test/publish and source smoke passed. Live DB/UI non-paid smoke was not executed.

## Paid Smoke Status

Paid provider smoke: NOT EXECUTED - awaiting explicit user approval.

## Build/Test/Publish

- Build: `dotnet build TodoX.Dashboard.sln -c Release` passed with 0 warnings and 0 errors.
- Phase 5.1 integration tests: `dotnet test TodoX.Dashboard.sln -c Release --no-build --filter "FullyQualifiedName~AiCoreRuntimePhase51Tests"` passed with 7 passed, 0 skipped, 0 failed.
- Full tests: `dotnet test TodoX.Dashboard.sln -c Release --no-build` passed with 253 passed, 1 skipped, 0 failed.
- Lint/check: `git diff --check` passed; only line-ending warnings were printed.
- Publish: `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\phase5-1-prod-hardening` passed.

## Configuration Readiness

Provider credential direct paths are removed from source/config. Remaining deployment config checks still need to be verified in the target runtime without printing secret values.

## Security Review

No new secret values were added to source reports. Pre-existing local key file remains unstaged.

## Known Risks

- YEScale MCP is unavailable: `yescale_get_current_user` returned `YEScale request failed: fetch failed`.
- Paid provider smoke was not executed.
- MinIO/public callback URL/worker runtime settings were not live-smoked in this turn.
- Dashboard naming still references image billing adapter for read-only compatibility.

## Required Manual Actions

1. Restore YEScale MCP connectivity and reverify YEScale metadata from the authoritative live source.
2. Run target-environment non-paid smoke for provider diagnostics, operation logs, callback URL, and worker settings.
3. Approve and run one low-cost paid smoke after non-paid smoke passes.

## Deployment Plan

Use `phase-5-1-deployment-checklist.md`.

## Rollback Plan

Use `phase-5-1-rollback-checklist.md`.

## Final Decision

Local Phase 5.1 hardening: GO. Production deployment: HOLD pending live config/MCP/smoke gates.
