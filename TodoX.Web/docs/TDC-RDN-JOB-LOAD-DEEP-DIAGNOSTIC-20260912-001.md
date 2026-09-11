# TDC-RDN-JOB-LOAD-DEEP-DIAGNOSTIC-20260912-001

Target job: `bdb30808-6098-48ab-a47c-fbd6fe29aaa7`

Runtime verification: NOT PERFORMED. No production/customer smoke test was run. No production database read/write was performed by this task.

## Scope

Changed only the RDance job DTO repository mapping and regression tests around that mapping/detail-load contract.

Protected areas not modified: 79AI, KIE, upload provider behavior, render provider payloads, billing, points charging, pricing rules, duration derivation, RVideo, Timelapse, Create flow, database schema, migrations.

## Root Cause

`DanceSellRepository.GetByIdAsync()` built `DanceSellJobDto` from `SelectSql`, but `SelectSql` read `character_orientation AS CharacterOrientation` while the current runtime write/update path uses the `orientation` column. On production schema where `orientation` is the runtime column, this makes the primary RDance detail load fail inside the repository query before post-load steps can run.

Exact file and method:

- `Services/DanceSell/DanceSellRepository.cs`
- `GetByIdAsync(Guid id, CancellationToken ct)`
- shared `SelectSql`

Exact failing statement/condition before the fix:

```sql
motion_video_url AS MotionVideoUrl, mode AS Mode, character_orientation AS CharacterOrientation
```

This select is executed by:

```text
RDanceJobDetail.OnParametersSetAsync()
  -> ReloadAsync()
     -> DanceSell.GetAsync(JobId, AuthState.CurrentUser)
        -> RequireOwnedJobAsync()
           -> DanceSellRepository.GetByIdAsync()
              -> SelectSql + " WHERE id=@id;"
```

## Why The Target Job Failed

The supplied evidence proves the row exists and identity matches:

- `USER_MATCH = true`
- `TENANT_MATCH = true`
- `CUSTOMER_MATCH = true`
- `status = draft`
- `render_job_id = NULL`
- `source_stage_status = ready`
- `prepared_reference_status = approved`
- `request_json.durationSeconds = 10`
- `request_json.serviceCode = FASHION_VIDEO`
- `request_json.serviceId = abcf33cd-6b69-4c8d-9daa-e84d92fc80ec`

Therefore the failure is not ownership, tenant mismatch, or missing row. The failing operation is the repository DTO projection reading the wrong orientation column during the primary `DanceSell.GetAsync()` load.

Before `TDC-RDN-FIX-JOB-LOAD-ERROR-MASKING-20260911-002`, this primary/reload exception could be surfaced as the generic not-found text. Current branch already separates primary load from post-load refresh errors; this task fixes the underlying repository query mismatch.

## Before Flow

```text
ReloadAsync()
  -> ProviderCatalog.GetDefaultRouteAsync(reference image), caught as readiness warning
  -> ProviderCatalog.GetDefaultRouteAsync(motion video), caught as readiness warning
  -> DanceSell.GetAsync()
       -> repository SelectSql references character_orientation
       -> query fails on orientation-only runtime schema
       -> primary load fails
  -> post-load steps never reached
```

## After Flow

```text
ReloadAsync()
  -> ProviderCatalog readiness checks
  -> DanceSell.GetAsync()
       -> repository SelectSql references orientation AS CharacterOrientation
       -> valid draft job can be mapped
  -> RenderJobs.GetAsync only when RenderJobId has a value
  -> ListReferenceVersionsAsync(_job.Id)
  -> RefreshEstimateAsync()
  -> ContinueAutoFinishAsync()
```

## Awaited Operation Audit

| Step | Method | Expected input for target | Possible exception | Current catch | Current UI result |
|------|--------|---------------------------|--------------------|---------------|-------------------|
| A | `ProviderCatalog.GetDefaultRouteAsync(ReferenceImage)` | operation `reference_image` | catalog route missing/misconfigured | local try/catch in `ReloadAsync` | sets reference readiness error; does not clear `_job` |
| B | `ProviderCatalog.GetDefaultRouteAsync(MotionVideo)` | operation `motion_video` | catalog route missing/misconfigured | local try/catch in `ReloadAsync` | sets motion readiness error; does not clear `_job` |
| C | `DanceSell.GetAsync(JobId, user)` | existing authorized job id | missing row, unauthorized, SQL projection failure | primary-load catch | missing/unauthorized classified; other errors as detail-load error |
| D | `RenderJobs.GetAsync(renderJobId)` | skipped because `RenderJobId = NULL` | render job lookup/db error if id exists | post-load catch | detail refresh error; `_job` retained |
| E | `DanceRepo.ListReferenceVersionsAsync(_job.Id)` | target job id | db/query error | post-load catch | detail refresh error; `_job` retained |
| F | `RefreshEstimateAsync()` | mapped job, motion route | latest operation, cost, pricing, wallet lookup error | post-load catch | detail refresh error; `_job` retained |
| G | `ContinueAutoFinishAsync()` | draft/approved/autofinish job | approval flow errors only when reference status is `ready` | post-load catch | detail refresh error; `_job` retained |

## RenderJobId NULL

Handled correctly in current branch:

```csharp
_renderJob = _job.RenderJobId is Guid renderJobId
    ? await RenderJobs.GetAsync(renderJobId)
    : null;
```

No `Guid.Empty`, `.Value`, or render job lookup is used in `ReloadAsync` for a null `render_job_id`.

## DTO / Database Comparison

Expected target values and current mapping after the fix:

| Field | Source | Result |
|-------|--------|--------|
| `Id` | `id AS Id` | preserved |
| `CustomerId` | `customer_id AS CustomerId` | preserved |
| `TenantId` | `tenant_id AS TenantId` | preserved |
| `UserId` | `user_id AS UserId` | preserved |
| `Status` | `status AS Status` | `draft` preserved |
| `RenderJobId` | `render_job_id AS RenderJobId` | null preserved |
| `MotionVideoMediaId` | `motion_video_media_id AS MotionVideoMediaId` | preserved |
| `MotionVideoUrl` | `motion_video_url AS MotionVideoUrl` | preserved |
| `MotionSourceType` | `motion_source_type AS MotionSourceType` | preserved |
| `SourceStageStatus` | `source_stage_status AS SourceStageStatus` | `ready` preserved |
| `PreparedReferenceMediaId` | `prepared_reference_media_id AS PreparedReferenceMediaId` | preserved |
| `PreparedReferenceUrl` | `prepared_reference_url AS PreparedReferenceUrl` | preserved |
| `PreparedReferenceStatus` | `prepared_reference_status AS PreparedReferenceStatus` | `approved` preserved |
| `RequestJson` | `request_json::text AS RequestJson` | preserved |
| `AutoFinish` | `(request_json->>'autoFinish')::boolean` | true preserved |
| `Ratio` | `request_json->>'ratio'` with default | `9:16` preserved |
| `CharacterOrientation` | `orientation AS CharacterOrientation` | fixed |

`DanceSellJobDto` does not expose separate `ServiceId`, `ServiceCode`, or `DurationSeconds` properties. Those values are intentionally carried in `RequestJson`.

## Request JSON

The target `request_json` fields are preserved by `request_json::text AS RequestJson`.

`RDanceJobDetail.ResolveMotionDurationSeconds()` reads:

```text
durationSeconds, duration_seconds, videoDurationSeconds, video_duration_seconds
```

`DanceSellCustomerPricing` reads:

```text
serviceId/service_id
serviceCode/service_code
```

So `durationSeconds = 10`, `serviceCode = FASHION_VIDEO`, and `serviceId = abcf33cd-6b69-4c8d-9daa-e84d92fc80ec` are preserved after the repository row can be loaded.

## Reference Version

`ListReferenceVersionsAsync(_job.Id)` queries:

```sql
FROM dance_sell.dance_sell_reference_versions
WHERE dance_sell_job_id=@danceSellJobId
ORDER BY version_no DESC
```

The supplied evidence says a selected ready reference version exists. This step is post-load and was not the root cause.

## Estimate / Pricing

`RefreshEstimateAsync()` runs after `_job` is loaded. It reads the latest motion operation, cost estimate, point estimate, and wallet balance. It can throw, but current branch catches it as a post-load refresh error and keeps `_job`.

It did not cause the primary not-found symptom for the target job because the proven query mismatch happens earlier in `DanceSell.GetAsync()`.

## AutoFinish

For the target state:

```text
status = draft
autoFinish = true
prepared_reference_status = approved
source_stage_status = ready
render_job_id = NULL
```

`ContinueAutoFinishAsync()` returns without queueing because it only performs automatic approval when `PreparedReferenceStatus == ready` and `_latestReference.Status == ready`. It does not create a render job for already-approved draft jobs in the detail-load path. This step was not the root cause.

## Logic Changed

Changed repository DTO projections from:

```sql
character_orientation AS CharacterOrientation
```

to:

```sql
orientation AS CharacterOrientation
```

Applied in:

- `CreateDraftAsync` `RETURNING`
- `CreateAsync` `RETURNING`
- shared `SelectSql`

No behavior was changed in `/create-video`, provider routing, model selection, payload fields, endpoints, retry policy, pricing policy, billing policy, or render orchestration.

## Tests

Passed:

```text
dotnet test Tests\TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RDanceCustomerStatusAndPointsRegressionTests"
PASS - Failed: 0, Passed: 28, Skipped: 0, Total: 28
```

```text
dotnet test ..\TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~DanceSellRepositoryTests"
PASS - Failed: 0, Passed: 14, Skipped: 0, Total: 14
```

Wider RDance focused filters were also run and found pre-existing source-assert failures unrelated to this fix:

```text
dotnet test Tests\TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RDance"
FAIL - Failed: 3, Passed: 48, Total: 51
```

Failures:

- `RDanceReferencePromptRegressionTests.DanceSellServicePrefersPersistedImagePromptAndRegeneratesVersions`
- `RDanceReferencePromptRegressionTests.ManualRetryCreatesFreshMotionAttemptAndRebindsRenderInput`
- `RDanceReferencePromptRegressionTests.ManualRetryReusesOnlyVerifiedAssetsWithCurrentMediaIdentity`

```text
dotnet test ..\TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~DanceSellRepositoryTests|FullyQualifiedName~RDance"
FAIL - Failed: 10, Passed: 47, Total: 57
```

Failures are existing RDance UI/source assertion drift in `RDanceFashionDemoPageTests` and `RDanceStagedBillingRegressionTests`; they are not caused by the repository mapping diff.

## Build

```text
dotnet build TodoX.Web.csproj -c Release --no-restore
PASS - 45 warnings, 0 errors
```

Warnings are generated Razor nullable warnings in `AiProviders_razor.g.cs`.

## Publish

```text
dotnet publish TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\todox-dashboard
PASS
```

Output exists:

```text
D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\TodoX.Web\artifacts\publish\todox-dashboard
```

## git diff --check

```text
git diff --check
PASS
```

Only line-ending warnings were printed.

## Files Changed

- `Services/DanceSell/DanceSellRepository.cs`
- `Tests/RDanceCustomerStatusAndPointsRegressionTests.cs`
- `..\TodoX.Web.Tests\DanceSellRepositoryTests.cs`
- `docs/TDC-RDN-JOB-LOAD-DEEP-DIAGNOSTIC-20260912-001.md`

## Commit / Push

Pending at report creation. Commit and push should include only the files above and should exclude publish artifacts.
