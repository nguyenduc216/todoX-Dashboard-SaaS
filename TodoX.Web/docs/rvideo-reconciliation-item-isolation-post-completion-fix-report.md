# RVIDEO Reconciliation Item Isolation And Post-Completion Fix

## Production symptom

Project 11 had successful 79AI scene-video provider tasks, but one failed reconciliation item could abort the rest of the claimed billing batch. Scene 48 recovered local media and completed its scene-video version, yet billing remained `pending_reconciliation`.

## Scene 48 recovery evidence

The observed event sequence was:

1. `RVIDEO_VIDEO_RECOVERY_BEGIN`
2. `RVIDEO_VIDEO_PERSIST_BEGIN`
3. `RVIDEO_VIDEO_PERSIST_REUSED`
4. `SCENE_VIDEO_READY`

The missing events were `RVIDEO_VIDEO_BILLING_COMPLETED` and `RVIDEO_VIDEO_RECOVERY_COMPLETED`. Scene 48 had `status=completed`, populated media fields, and `charged_points=173`. This confirms the voice-audio Dapper binding fix was working and places the failure after `SCENE_VIDEO_READY`, before billing completion.

## Root causes

The previous completion order was:

`persist provider output -> complete scene version -> SCENE_VIDEO_READY -> finalizer -> lifecycle sync -> billing -> recovery bookkeeping`.

The reconciliation worker also awaited each claimed item directly, so an unexpected exception exited the batch loop.

## Changes

- `AiImageBillingReconciliationWorker.ReconcileOnceAsync` now isolates every claimed item with per-item exception handling, cancellation propagation, structured `AI_IMAGE_RECONCILIATION_ITEM_FAILED` logging, and rescheduling of the exact logical request.
- 79AI recovery now resolves the actual `scene.SceneIndex` through `VideoRenderRepository.GetSceneAsync`; it no longer sends `SceneIndex: 0`.
- `RVideoSceneVideoCompletionService.CompleteProviderVideoAsync` now completes billing immediately after durable media and scene-version persistence, before finalizer/lifecycle work.
- Finalizer, lifecycle synchronization, and recovered render-job bookkeeping are isolated independently. Failures emit diagnostic events/logs: `RVIDEO_VIDEO_FINALIZER_FAILED`, `RVIDEO_VIDEO_LIFECYCLE_SYNC_FAILED`, and `RVIDEO_VIDEO_RENDER_JOB_RECOVERY_MARK_FAILED`.
- Successful scene-video completion clears `scene_video_versions.error_code` and `error_message`; successful billing clears stale reconciliation error/lock/scheduling fields, including the zero-cost completion path.
- The manual project 11 re-arm script now joins billing and scene versions by `logical_request_id` and `provider_task_id`, not `billing_logical_request_id`. It was audited but not executed against production.

## New completion order

1. Persist or reuse provider media.
2. Complete `scene_video_versions` and synchronize selected scene media.
3. Emit `SCENE_VIDEO_READY`.
4. Complete billing.
5. Emit `RVIDEO_VIDEO_BILLING_COMPLETED`.
6. Attempt finalizer/mux and isolate failures.
7. Attempt lifecycle synchronization and isolate failures.
8. Mark recovered render job complete when applicable and isolate bookkeeping failures.
9. Emit `RVIDEO_VIDEO_RECOVERY_COMPLETED`.

Billing remains after local persistence and scene-version completion, so no charge is finalized before the provider result is durable locally. Existing provider task IDs remain authoritative; recovery polls them and does not submit `/create-video`, call `SubmitAsync`, reserve points again, or create new logical requests.

## Tests

Added focused regression assertions for:

- reconciliation item isolation and rescheduling;
- billing-before-finalizer/lifecycle ordering;
- post-completion diagnostic isolation;
- stale success error cleanup;
- correct scene-index resolution;
- canonical manual re-arm identity joins.

Commands and results:

- `dotnet restore TodoX.Dashboard.sln`: passed.
- `dotnet test TodoX.Web\\Tests\\TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "RVideoRuntimeSqlTests"`: passed, 37/37.
- `dotnet test TodoX.Web\\Tests\\TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "Billing|Reconciliation|Finalizer|RenderJob"`: passed, 27/27.
- `dotnet test TodoX.Web\\Tests\\TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "RVideoProviderPollingRegressionTests|RVideoVideoHotfixTests"`: 69/70 passed; one unrelated pre-existing shared timelapse renderer assertion failed.
- Full Phase1B suite: 220 passed, 8 unrelated pre-existing failures.
- `dotnet test TodoX.Web.Tests\\TodoX.Web.Tests.csproj -c Release --no-restore`: 776 passed, 6 unrelated pre-existing failures.
- `dotnet test TodoX.Dashboard.sln -c Release --no-restore`: 776 passed, 6 unrelated pre-existing failures.

## Build and publish

- `dotnet build TodoX.Dashboard.sln -c Release --no-restore`: passed, 0 warnings, 0 errors.
- `dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore --include ...modified C# files`: passed.
- `git diff --check`: passed.
- `dotnet publish TodoX.Web\\TodoX.Web.csproj -c Release --no-restore -o artifacts\\publish\\todox-dashboard`: passed.
- Publish output: `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`.

## Changed files

- `TodoX.Web/Services/AiProviders/AiImageBillingReconciliationWorker.cs`
- `TodoX.Web/Services/AiProviders/AiImageBillingService.cs`
- `TodoX.Web/Services/VideoRender/RVideoSceneVideoCompletionService.cs`
- `TodoX.Web/Services/VideoRender/SceneMediaVersioningService.cs`
- `TodoX.Web/Tests/RVideoRuntimeSqlTests.cs`
- `database/manual/rvideo-project-11-video-reconciliation-rearm.sql`
- `TodoX.Web/docs/rvideo-reconciliation-item-isolation-post-completion-fix-report.md`

## Git

- Implementation commit SHA: `0b859e29f25b4fee806f531bf98f2d314f00990c`.
- Branch: `integration/rdance-on-construction-video-core`.
- Push result: implementation commit pushed successfully; this report correction is pushed in the follow-up documentation commit.
