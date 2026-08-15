# Core API Platform Task 01 Report

## 1. Summary

Implemented Phase 1 Core Platform billing and canonical job lifecycle foundation on `feature/core-api-platform`. The Core layer now supports shared service catalog projection, create/get/list/cancel/retry job operations, transport-neutral caller scoping, idempotency hardening, and a provider-neutral billing facade over `render.render_jobs`.

## 2. Architecture decisions

- `render.render_jobs` remains the canonical job store; no parallel API/Zalo/Partner job table was created.
- `ICoreBillingService` is the only billing dependency used by `CoreJobApplicationService`.
- Customer-facing paid jobs start as `draft/current_step=billing` and become `queued` only after reservation succeeds.
- External idempotency keys are scoped by channel, customer, client and service, then hashed into `logical_request_id`.
- Broad access requires `Channel=system` plus `IsTrustedInternal=true`.
- Public `/api/v1` endpoints are thin transport wrappers and remain disabled by default through `CoreApi:Enabled=false`.

## 3. Changed files

- `TodoX.Web/Services/Platform/CoreApiCallerResolver.cs`
- `TodoX.Web/Services/Platform/CoreApiEndpointExtensions.cs`
- `TodoX.Web/Services/Platform/CoreJobApplicationService.cs`
- `TodoX.Web/Services/Platform/CorePlatformContracts.cs`
- `TodoX.Web/Services/Platform/CorePlatformServiceCollectionExtensions.cs`
- `TodoX.Web/Services/Platform/CoreServiceCatalogService.cs`
- `TodoX.Web/appsettings.json`
- `TodoX.Web.Tests/CoreApiCallerResolverTests.cs`
- `TodoX.Web.Tests/CorePlatformContractTests.cs`
- `database/manual/core-api-platform/01_verify_core_schema.sql`

## 4. New files

- `TodoX.Web/Services/Platform/CoreBillingService.cs`
- `TodoX.Web.Tests/CorePlatformLifecycleSourceTests.cs`
- `database/manual/core-api-platform/03_add_core_job_current_step.sql`
- `docs/core-platform/core-billing-ownership.md`
- `docs/core-platform/reports/core-api-platform-task-01-report.md`

## 5. Database impact

No SQL was executed and no production database was modified. A standalone manual script was added for review: `database/manual/core-api-platform/03_add_core_job_current_step.sql`. It adds/backfills `render.render_jobs.current_step`, which Core job lifecycle code now reads/writes.

## 6. API contract

When `CoreApi:Enabled=true`, `/api/v1` exposes:

- `GET /api/v1/services`
- `GET /api/v1/services/{serviceCode}`
- `POST /api/v1/jobs`
- `GET /api/v1/jobs`
- `GET /api/v1/jobs/{jobId}`
- `POST /api/v1/jobs/{jobId}/cancel`
- `POST /api/v1/jobs/{jobId}/retry`

All API routes resolve caller identity through `ICoreApiCallerResolver`. Create/retry accept `Idempotency-Key` header or body key. Insufficient balance returns HTTP 402 with a safe job id.

## 7. Billing lifecycle

Estimate uses `IServiceSellPriceResolver`. Reserve moves customer balance into `locked_balance` and sets `point_status=pending`. Complete releases locked balance and records a debit transaction exactly once. Cancel releases pending reservations. Retry releases/refunds source billing before creating a new job. Trusted internal system jobs use `point_status=not_required`.

## 8. Idempotency strategy

External channels (`zalo`, `telegram`, `partner`, `api`) require an idempotency key. The logical id is SHA-256 over channel, customer, client, service and key. Create uses a PostgreSQL advisory transaction lock before duplicate lookup/insert, and duplicate requests return the existing job rather than reserve again.

## 9. Security/access scope

Core accepts identity only through `CoreRequestContext`. Customer callers can only see their own customer jobs. `user_id` alone never broadens access. System-wide access is allowed only for trusted internal system contexts.

## 10. Tests

Added and updated contract/source tests for channel normalization, external idempotency requirements, identity scoping, execution router routing/duplicate rejection, caller resolver unauthenticated handling, trusted system handling, customer job access, cancel terminal guards, retry correlation, API route boundary, billing lifecycle state guards, catalog price/form projection, and manual schema script presence.

Command:

`dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release`

Result: passed, `497` passed, `0` failed, `0` skipped.

## 11. Build result

Build command:

`dotnet build TodoX.Dashboard.sln -c Release --no-restore`

Result: passed with `0 warnings` and `0 errors`.

Changed-file formatter command:

`dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore --include <task C# files>`

Result: passed.

Whole-solution formatter result: failed on pre-existing whitespace issues outside this task in `AccountRepository.cs`, `AuditRepository.cs`, `ChibiAvatarService.Generate.cs`, settings repositories, `SocialPageRepository.cs`, and `WalletService.cs`. Those unrelated files were not modified.

Publish command:

`dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard`

Result: passed. Output: `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`.

## 12. Remaining risks

- Core execution adapters for Timelapse/RVideo/RDance are intentionally not connected in this phase, so queued Core jobs still require later adapter work.
- Environments must review/run the manual `current_step` SQL before enabling Core API where the column is missing.
- The facade currently uses generic billable input fields; service-specific adapter mappings are a later task.
- Database concurrency behavior is protected by SQL/advisory-lock source contracts, but no live PostgreSQL integration database was available for destructive lifecycle tests in this task.

## 13. Exact next recommended task

Implement the first real Core execution adapter for one service, preferably Timelapse, behind `ICoreJobExecutionAdapter` without changing the existing Timelapse workflow behavior.
