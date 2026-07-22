# Task 4 Result

- Branch: `refactor/ai-core-reset`
- Base commit: `76bf6a10f86534f846fac61eb009df406db1760a`
- Final commit: pending at report creation time
- Database: `todo_saas` non-production/test

SQL executed:

- `35_task4_billing_contract_fixes.sql`
- `36_task4_usage_contract_fixes.sql`
- `37_verify_task4_runtime.sql`

Billing and usage changes:

- Added generic billing contracts: `IAiBillingService`, `AiBillingService`, `IAiBillingDashboardService`, `AiBillingDashboardService`, generic DTOs/status enums, and typed amount helpers.
- Added generic provider usage contracts: `IAiProviderUsageService`, `AiProviderUsageService`, `IAiProviderUsageRepository`, `AiProviderUsageRepository`.
- `IAiProviderService.LogUsageAsync` now delegates to generic usage and writes idempotently by `idempotency_key`.
- Provider usage separates usage quantity/unit, provider raw cost/currency, and TodoX points.

Workflow migration:

- Dance Sell completion and poll usage now records `CustomerGuid = danceJob.CustomerId`, render job ID, provider task ID, and operation type.
- Image router usage now records customer UUID when available, render job ID, provider task ID, raw request/response/usage JSON, and provider cost currency.
- Existing image billing interfaces remain as compatibility adapters to avoid a risky full caller rename in one commit.

Lease and completion orchestration:

- Task 3 account lease runtime remains in place with `FOR UPDATE SKIP LOCKED`, active leases, heartbeat/release/watchdog support.
- Task 4 did not add paid provider traffic or deploy.
- Shared completion remains partial: Dance Sell has a completion service; Task 5 should continue consolidating image/video completion into a single `IAiRenderCompletionService`.

Logs/dashboard/diagnostics:

- Operation log target is shared render jobs/events/artifacts plus provider usage and billing tables.
- Generic billing dashboard contract is active in DI and maps to the existing dashboard adapter.
- Provider account diagnostics data is available from provider account/lease tables; a full admin UI/action surface remains Task 5 scope.

Verification:

- `30_verify_schema.sql`: passed.
- `31_verify_provider_seed.sql`: passed, 5 providers, 36 capabilities total.
- `32_verify_runtime_contract.sql`: passed.
- `34_verify_task3_runtime.sql`: passed.
- `37_verify_task4_runtime.sql`: passed.
- `git diff --check`: passed with line-ending warnings only.
- `dotnet restore`: passed.
- `dotnet build`: passed, 0 warnings, 0 errors.
- `dotnet test`: passed, 240 passed, 1 skipped.
- `dotnet publish`: passed to `artifacts/publish/task4-ai-core-billing`.

Known limitations:

- Image-oriented billing type names remain for compatibility; they should be removed only after all callers are migrated.
- Full provider account diagnostics UI/actions are not completed in this commit.
- Full shared `IAiRenderCompletionService` for every workflow remains Task 5 scope.
- No live paid provider smoke traffic was executed.

Task 5 recommendations:

- Rename remaining image billing callers to `IAiBillingService`.
- Move reserve/charge/refund SQL into a dedicated `AiBillingRepository`.
- Expand provider account diagnostics UI/API.
- Consolidate terminal handling across image/video/Dance Sell in `IAiRenderCompletionService`.
- Add integration tests backed by a disposable PostgreSQL database for wallet locking and idempotent completion.
