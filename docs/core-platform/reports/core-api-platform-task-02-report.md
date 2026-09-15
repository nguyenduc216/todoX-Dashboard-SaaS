# Core API Platform Task 02 Report

## 1. Summary

Implemented the asynchronous Core execution lifecycle on `feature/core-api-platform` from base commit `89b43da9f40d20134585d08270d90cec3cddcc00`. Core adapters can now report synchronous completion or deferred external execution without the shared `RenderJobWorker` completing long-running jobs prematurely.

## 2. Root lifecycle issue fixed

`RenderJobWorker` still preserves its existing behavior for legacy handlers. `CoreServiceJobHandler` now prevents the worker's generic completion step by throwing the existing `RenderJobDeferredException` after Core has explicitly handled either result:

- Deferred execution is recorded and left non-terminal.
- Synchronous completion is finalized through the Core completion and billing services.

No shared worker contract or non-Core handler behavior was changed.

## 3. Execution adapter contract

`ICoreJobExecutionAdapter.DispatchAsync` now returns `CoreExecutionResult`.

Supported dispositions:

- `Completed`: execution has truly finished and may include transport-neutral output.
- `Deferred`: work was accepted by an external runtime and includes execution system, external execution id, optional adapter, message and metadata.

Exceptions remain the failure mechanism for dispatch errors.

## 4. Deferred execution behavior

Deferred Core jobs:

- Remain `status=rendering`.
- Move to `current_step=external_execution`.
- Retain their pending point reservation.
- Store execution correlation in `render.render_jobs.options.execution`.
- Clear `lock_owner` and `lock_until`.
- Write a `CORE_JOB_DEFERRED` event.
- Are not changed back to `queued`, so the worker does not immediately reclaim them.

## 5. Completion service

Added the internal `ICoreJobCompletionService` and `CoreJobCompletionService` boundary with:

- `MarkDeferredAsync`
- `MarkProgressAsync`
- `CompleteAsync`
- `FailAsync`

Calls require an internally constructible `CoreExecutionAuthority`. No public callback endpoint was added. True completion writes output, sets `status/current_step=completed`, sets progress to `100`, settles billing and writes completion events in the billing transaction.

## 6. Failure policy

Added explicit trusted-internal failure billing policies:

- `ReleaseReservation`
- `KeepCharge`
- `RefundCharge`

The default on `CoreJobFailRequest` is `ReleaseReservation`. Public clients cannot submit this policy because no public complete/fail API route exists.

## 7. Retry semantics

Technical retry continues to use `RenderJobService.ScheduleRetryAsync(job.Id, ...)`, preserving the same canonical job and reservation.

Business/user retry still creates a new canonical job linked by `retry_of_job_id`, but now releases the source only when `point_status=pending`. A charged source job is not automatically refunded.

## 8. Billing behavior

- Deferred execution leaves `point_status=pending` and wallet locked balance intact.
- Completion of a pending paid job releases locked balance and records one debit.
- Repeated completion returns the existing completed/charged result without charging again.
- Pre-charge terminal failure releases the reservation unless trusted code explicitly chooses `KeepCharge`.
- A charged failure keeps the charge by default.
- `RefundCharge` performs a trusted explicit refund once.
- Repeated failure returns the existing failed settlement without another release/refund.
- Output update, job completion and billing settlement are performed in one database transaction.

## 9. Progress behavior

Progress updates validate the inclusive `0..100` range. They update `current_step`, progress and an event only for non-terminal Core jobs. The `post_processing` step also moves status to `post_processing`. Completed, failed and cancelled rows are not overwritten.

## 10. Execution correlation

Deferred correlation is stored under `options.execution` with:

- `system`
- `external_execution_id`
- `adapter`
- `metadata`

The existing GET job projection now exposes this as optional `CoreJobView.Execution`. No Timelapse-specific correlation schema was introduced.

## 11. Changed files

- `TodoX.Web/Services/Platform/CorePlatformContracts.cs`
- `TodoX.Web/Services/Platform/CoreExecutionRouter.cs`
- `TodoX.Web/Services/Platform/CoreServiceJobHandler.cs`
- `TodoX.Web/Services/Platform/CoreJobCompletionService.cs`
- `TodoX.Web/Services/Platform/CoreBillingService.cs`
- `TodoX.Web/Services/Platform/CoreJobApplicationService.cs`
- `TodoX.Web/Services/Platform/CorePlatformServiceCollectionExtensions.cs`
- `TodoX.Web.Tests/CorePlatformContractTests.cs`
- `TodoX.Web.Tests/CoreExecutionLifecycleTests.cs`
- `TodoX.Web.Tests/CorePlatformLifecycleSourceTests.cs`
- `docs/core-platform/reports/core-api-platform-task-02-report.md`

No file under `TodoX.Web/Services/Timelapse` and no Timelapse Razor page was changed.

## 12. Database impact

No SQL or migration was created, modified or executed. The implementation reuses existing `render.render_jobs` fields, `options` JSON, billing tables and render job events.

## 13. API impact

No public endpoint was added. Existing API v1 routes remain unchanged. `CoreJobView` gained a backward-compatible optional `Execution` projection. `CoreApi:Enabled` remains `false` by default.

## 14. Tests

Command:

`dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release --no-restore`

Result: passed, `517` passed, `0` failed, `0` skipped.

Coverage added for Completed/Deferred dispatch, worker completion suppression, progress validation, pending-versus-charged business retry, terminal guards, output/progress/event completion contracts, explicit failure policies, correlation projection, same-job technical retry and absence of public completion/failure routes.

Timelapse freeze verification:

`git diff --name-only | rg "TodoX.Web/Services/Timelapse|Timelapse.*razor"`

Result: no matches.

## 15. Build

Formatter/lint command:

`dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore --include <changed C# files>`

Result: passed.

Build command:

`dotnet build TodoX.Dashboard.sln -c Release --no-restore`

Result: passed with `0 warnings` and `0 errors`.

Publish command:

`dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard`

Result: passed. Local output:

`D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

No deployment or production service restart was performed.

## 16. Remaining risks

- No live PostgreSQL integration database was used for wallet concurrency and callback replay tests; transaction and idempotency behavior is covered by unit/source-contract tests.
- Real Timelapse, RVideo and RDance adapters remain intentionally outside Task 02.
- A future trusted callback transport will require its own strong internal authentication before it may call the completion service.
- Core API remains disabled until its separate enablement and deployment review.

## 17. Is Core ready to become baseline? YES/NO

YES. Task 02 establishes the Core baseline v1 lifecycle required before service-specific adapters are introduced. This does not mean Core API is enabled or that a production adapter has been connected.

## 18. Final commit SHA

The immutable final commit SHA is recorded in the delivery response after this report and its implementation are committed and pushed together.
