# Construction Video Core Task 03 Report

## 1. Summary

Implemented the `CONSTRUCTION_VIDEO` Core execution adapter on `feature/construction-video-core`. Canonical Core jobs now dispatch through the existing Timelapse engine and complete through the Core job/billing lifecycle without rewriting the legacy Dashboard path.

## 2. Branch created

- Branch: `feature/construction-video-core`
- Base commit: `a4a9de4a11be2171b3da2b0e48864b4d1a05bcf4`
- No merge to `main` was performed.

## 3. Timelapse audit

The existing flow is:

`/jobs/timelapse/new` -> `TimelapseJobService.CreateDraftAsync` -> legacy `render.render_jobs` row (`job_type=timelapse`) -> `TimelapseWorkflowService` -> image/video workers -> `TimelapseProviderRuntime` -> 79AI -> `TimelapseFinalizerRuntime` -> FFmpeg finalizer.

## 4. Legacy entry point

The preserved Dashboard entry point is `/jobs/timelapse/new`. It continues to create and run legacy Timelapse jobs directly; Task 03 does not redirect or remove it.

## 5. Legacy job identifier

The legacy execution identifier is the UUID in `render.render_jobs.id` for the row whose `job_type` is `timelapse`.

## 6. Adapter design

`ConstructionTimelapseAdapter` implements `ICoreJobExecutionAdapter` for `CONSTRUCTION_VIDEO`. It delegates creation and workflow start to `ConstructionTimelapseExecutionBridge` and returns `CoreExecutionResult.Deferred` with:

- `system=todox`
- `adapter=construction_timelapse`
- `external_execution_id=<legacy Timelapse job UUID>`

## 7. Input mapping

The adapter maps existing Timelapse-compatible fields only:

- profile: `profileCode`, `profile_code`, `category`
- scenes: `sceneCount`, `scene_count` (`3`, `4`, `5`, `6`)
- quality: `fast`/`standard`, `professional`/`premium`
- ratio: `16:9`/`16_9`, `9:16`/`9_16`
- source media: direct media-id aliases, `source_image`, or a reference with `original_image`, `input_image`, or `image`

It fixes runtime scene duration to the existing six-second Timelapse contract and validates active, tenant-scoped, customer-owned image media.

## 8. Core-to-legacy correlation

The deterministic Core job UUID is stored as `CoreJobId` in the legacy Timelapse snapshot (`render.render_jobs.input_json`). Redispatch is idempotent through a PostgreSQL advisory lock plus lookup on `input_json->>'coreJobId'`.

## 9. Progress bridge

Meaningful milestones are bridged only:

- `20`: `image_generation`
- `45`: `images_ready`
- `60`: `video_generation`
- `85`: `post_processing`
- `95`: `finalizing`
- `100`: Core completion

Terminal Core jobs are ignored. The Core progress update now has an atomic increasing-progress guard, so concurrent worker callbacks cannot reduce progress.

## 10. Completion bridge

After the legacy finalizer saves the final video media, the bridge calls `ICoreJobCompletionService.CompleteAsync` with a transport-neutral video output containing URL, `video/mp4`, media id, object key, legacy job UUID, and `CONSTRUCTION_VIDEO`.

Completion errors are retried idempotently by the idle finalizer worker reconciliation path.

## 11. Failure bridge

Only a terminal legacy Timelapse failure can fail the Core job. Submit failures before a provider task id release the reservation; failures after a provider task exists and finalizer failures keep the charge. The reconciliation path also retries terminal Core failure callbacks after transient errors.

## 12. Retry behavior

Legacy technical execution retries continue to use the same legacy and Core job correlation. The Core adapter does not create a new Core job for polling, scene, or finalizer retries. Core business retry remains the responsibility of `CoreJobApplicationService.RetryAsync`.

## 13. Billing behavior

The Core job remains billing authority. The linked legacy job is created with `point_status=not_required`, so it cannot independently charge the customer. Core completion/failure uses existing idempotent billing settlement.

## 14. Changed files

- `TodoX.Web/Models/Timelapse/TimelapseModels.cs`
- `TodoX.Web/Program.cs`
- `TodoX.Web/Services/Platform/CoreJobCompletionService.cs`
- `TodoX.Web/Services/Timelapse/ConstructionTimelapseAdapter.cs`
- `TodoX.Web/Services/Timelapse/TimelapseCoreLifecycleBridge.cs`
- `TodoX.Web/Services/Timelapse/TimelapseFinalizerRuntime.cs`
- `TodoX.Web/Services/Timelapse/TimelapseProviderRuntime.cs`
- `TodoX.Web/Services/Timelapse/TimelapseProviderWorkers.cs`
- `TodoX.Web.Tests/ConstructionTimelapseCoreTests.cs`
- `TodoX.Web.Tests/CorePlatformLifecycleSourceTests.cs`

## 15. Database impact

No migration, schema change, new table, or production SQL execution was performed. The implementation reuses existing `render.render_jobs`, existing Timelapse tables, JSON fields, and render job events.

## 16. Existing Timelapse behavior impact

The existing Dashboard route, prompt generation, scene graph, scene ratios, provider/model selection, 79AI request contract, retries, and finalizer behavior remain intact. Existing scene mappings remain:

- `3`: `0,35,70,100`
- `4`: `0,25,50,75,100`
- `5`: `0,20,40,60,80,100`
- `6`: `0,25,40,55,70,85,100`

## 17. Tests

Added `ConstructionTimelapseCoreTests` for adapter routing, deferred correlation, input aliases, scene rules, media reference role normalization, transport-neutral output, billing failure policies, existing scene mappings, no public completion/failure endpoint, and no new job table.

Command:

`dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release --no-restore`

Result: passed, `536` passed, `0` failed, `0` skipped.

## 18. Build

Formatter/lint:

`dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore --include <changed C# files>`

Result: passed.

Build:

`dotnet build TodoX.Dashboard.sln -c Release --no-restore`

Result: passed, `0` warnings, `0` errors.

Publish:

`dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard`

Result: passed. Local output: `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`.

## 19. Remaining risks

- Validation is unit/source-contract based; no PostgreSQL integration environment was used for advisory lock, JSON correlation, or billing concurrency verification.
- `CoreJobCompletionService` gained an atomic progress monotonicity guard and preserves a milestone already reported before deferred marking. This is a generic Core baseline correctness fix and should be reviewed for backport to `feature/core-api-platform`; no backport or merge was performed here.
- Staging should exercise one successful job and each terminal failure policy with the production-like worker configuration.

## 20. Is construction Core adapter ready for staging smoke test? YES/NO

YES. It is ready for a controlled staging smoke test. No production deployment, restart, merge, or SQL execution was performed.

## 21. Final commit SHA

The immutable final delivery commit SHA is reported in the delivery response after this report is committed and pushed. A Git commit cannot contain its own final hash without changing that hash.
