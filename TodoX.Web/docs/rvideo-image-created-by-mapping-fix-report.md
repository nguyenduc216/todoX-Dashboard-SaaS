# RVIDEO Scene Image Created-By Mapping Fix

## Production symptom

Persisted RVIDEO `render_scene_image` child jobs retried after successful 79AI submission but failed while loading the billing reservation. The failure was Dapper `DataException` parsing the `created_by` column as `Guid?`, leaving the scene image versions queued even though the provider task IDs already existed.

## Root cause and scope

`billing.ai_image_billing_records.created_by` is `text` in the billing schema. `AiImageBillingService` directly hydrated that column into `BillingRecord.CreatedBy` (`Guid?`) in both:

- `GetReservationAsync`
- `GetRecordForUpdateAsync`, used by `ReserveAsync`, `CompleteAsync`, and pending-reconciliation processing

`ClaimReconciliationBatchAsync` does not select `created_by` and therefore does not hydrate this field.

## Mapping strategy

Both billing-record queries now hydrate a string-valued `BillingRecordRow.CreatedBy`. `AiImageBillingCreatedByParser.Normalize` uses `Guid.TryParse` at the one conversion boundary:

- Valid UUID text maps to the matching `Guid`.
- `NULL`, empty, and whitespace values map to `null`.
- Invalid legacy text such as `n8n` and `legacy-user` maps to `null` without a Dapper conversion failure.

No PostgreSQL cast, schema migration, or production data rewrite was added. Valid `created_by` continues to flow into the normal debit audit insert; legacy invalid values become a null audit actor, matching the nullable ledger column contract.

## Recovery behavior

The scene-image work item still reads the persisted `scene_image_versions.provider_task_id`, passes it to the image router, and the 79AI provider path uses that task for polling. The fix does not create a new billing record, reserve points again, change prices or wallet math, reset attempts, enqueue replacement jobs, or add a new provider submission path.

With the hydration failure removed, existing queued RVIDEO image jobs can load their reservations, reuse their existing 79AI task IDs, poll/finalize the provider result, persist/select the completed scene image version, and continue the normal scene-video auto-chain.

## Changed files

- `Services/AiProviders/AiImageBillingService.cs`
- `Tests/RVideoProviderPollingRegressionTests.cs`
- `docs/rvideo-image-created-by-mapping-fix-report.md`

## Validation

- `dotnet restore`: passed; all projects already up to date.
- `dotnet build -c Release --no-restore`: passed, 0 warnings and 0 errors.
- `dotnet test Tests\TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RVideoProviderPollingRegressionTests"`: passed, 62/62.
- `dotnet format ..\TodoX.Dashboard.sln whitespace --verify-no-changes --no-restore --include TodoX.Web\Services\AiProviders\AiImageBillingService.cs TodoX.Web\Tests\RVideoProviderPollingRegressionTests.cs`: passed.
- `git diff --check`: passed.
- `dotnet publish TodoX.Web.csproj -c Release --no-restore -o ..\artifacts\publish\todox-dashboard`: passed.

The full `Tests\TodoX.Web.Phase1B.Tests.csproj` run remains blocked by 9 unrelated existing failures: two missing RVIDEO SQL fixture files, one Timelapse UI assertion, four scene prompt metadata assertions, and two RDance source assertions. The focused RVIDEO regression suite passes.

## Database and deployment

No database migration, SQL execution, production deployment, or service restart was performed. Publish output was produced at `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`.

## Git handoff

The implementation, tests, and this report are committed and pushed to `integration/rdance-on-construction-video-core`. The final commit SHA is reported in the delivery message and remote verification output.
