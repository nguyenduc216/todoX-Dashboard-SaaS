# Task 3 Provider Runtime And Render Core Report

## Summary
- Branch: `refactor/ai-core-reset`
- Base commit: `df041b147e9ea2c9916b9eaaf5871099530fcf97`
- Database: `todo_saas` non-production/test
- Deploy/merge: not performed

## Database Verification
- `30_verify_schema.sql`: passed, 19 required tables exist.
- `31_verify_provider_seed.sql`: passed, 5 providers present.
- `32_verify_runtime_contract.sql`: passed, removed legacy tables absent.
- `33_task3_runtime_contract_fixes.sql`: executed, updated 1 KIE account.
- `34_verify_task3_runtime.sql`: passed all 11 checks.

## Capability Reconciliation
- Exported before reset: 35.
- Verified current total: 36.
- Provider counts: `image_ai_creative_render=4`, `kie=2`, `openrouter=8`, `yescale_task_image=21`, `yescale_task_video=1`.
- Accepted resolution: 36. The 37 count was a report/export mismatch; current DB verification is 36.

## KIE Account
- Account: `kie-default`.
- Credential reference: `KIE_API_KEY`.
- Secret storage: no secret value stored in DB or source.
- `max_concurrency`: 1.
- Rate limit: `rate_limit_requests=NULL`, `rate_limit_window_seconds=NULL`.
- Config marker: `{"rateLimitVerified": false, "rateLimitStatus": "provisional"}`.

## Provider Account Runtime
- Added provider account models and repository.
- Atomic claim uses one PostgreSQL transaction with `FOR UPDATE OF a SKIP LOCKED`.
- Active leases in `public.todox_ai_provider_account_lease` are the concurrency source of truth.
- Added heartbeat, release, expire/watchdog helpers, account success/failure, and balance snapshot updates.

## Credential Resolver And Redaction
- Added credential resolver that loads enabled account credential references.
- Resolver supports `credential_key` / `credential_config_name` and environment/config lookup.
- KIE resolves `KIE_API_KEY`.
- Added shared redaction utility for authorization, bearer, API keys, tokens, client secrets, and private keys.
- KIE JSON redaction now delegates to the shared redactor.

## Routing And Render Core
- Dance Sell routing no longer queries `public.todox_ai_feature_provider_route`.
- Route selection reads provider/capability rows from `public.todox_ai_provider_capability`.
- Default account metadata is selected from `public.todox_ai_provider_account`.
- Dance Sell operation adapter now uses `render.render_jobs`.
- Operation assets now use `render.render_artifacts`.
- Snapshot helper now appends `render.render_job_events` instead of writing dropped snapshots.

## Billing Compatibility
- Runtime billing table references were switched from image-only names to generic names:
  - `billing.ai_billing_records`
  - `billing.ai_provider_attempts`
- Full public interface rename from `IAiImageBillingService` to generic billing is left for Task 4.

## Legacy Reference Result
- Runtime code has zero references to removed legacy tables.
- Remaining references are only historical docs and explicit absence tests.
- See `task-3-legacy-reference-check.md/.csv`.

## Validation
- `git diff --check`: passed; Windows line-ending warnings only.
- `dotnet restore TodoX.Dashboard.sln`: passed.
- `dotnet build TodoX.Dashboard.sln -c Release --no-restore`: passed, 0 warnings, 0 errors.
- `dotnet test TodoX.Dashboard.sln -c Release --no-build`: passed, 235 passed, 1 skipped, 0 failed.
- `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/task3-ai-core-runtime`: passed.

## Known Limitations
- Billing class/interface names are still image-oriented adapters; Task 4 should rename and normalize public billing contracts.
- Dance Sell business table remains compact; richer input/output detail should continue to be staged through render inputs/artifacts in later workflow phases.
- No real KIE traffic was executed.
- Existing local dirty key file `TodoX.Web/keys/todox-vertex-sa.json` was not staged or committed.

## Task 4 Recommendations
- Rename `IAiImageBillingService` and related DTOs to generic AI billing names.
- Complete semantic column mapping for provider actual cost, reconciliation scheduling, and attempt metadata on generic billing records.
- Wire provider account lease claim into all provider submission handlers, not only the new runtime repository.
- Add integration tests against a disposable PostgreSQL schema for claim/lease concurrency.
- Finish admin diagnostics for provider accounts, credential metadata, active leases, render events, inputs, and artifacts.
