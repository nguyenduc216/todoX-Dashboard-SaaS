# RVIDEO 79AI Video Recovery, Storage, and Reconciliation Fix

Date: 2026-08-27

## Scope

- Starting HEAD: `86bab64f979da9ba342411ba65f02dbb1ea1946f`
- Branch: `integration/rdance-on-construction-video-core`
- Final implementation commit: `a20717f697d0b79f95644e8b1541a312b60543c5` (`fix(rvideo): complete tenant-safe 79ai recovery lifecycle`).
- Push status: `a20717f697d0b79f95644e8b1541a312b60543c5` was successfully pushed to `origin/integration/rdance-on-construction-video-core`.

## Root Causes

1. The immutable scene-video storage key did not include `tenantId`.
2. Immutable media recovery could use an object-key lookup without tenant scoping, and physical-file-only states were not an explicit recoverable inconsistency.
3. Reconciliation completed recovered scene-video versions with zero charged points instead of the persisted billing-record snapshot.
4. Normal provider-success and reconciliation-success paths had separate completion behavior, so recovery could leave finalization, lifecycle, billing, or render-job state incomplete.

## Implementation

### Tenant-safe immutable media

`SceneMediaStorageKeys.SceneVideoOutput` now uses:

```text
rvideo/{tenantId:N}/project-{projectId}/scene-{sceneId}/video/{videoVersionId:N}.mp4
```

The same tenant and `SceneVideoVersion.Id` always resolve to the same key. A different tenant or version resolves to a different key.

`IMediaFileService.GetByObjectKeyAsync(Guid tenantId, string objectKey, ...)` performs:

```sql
WHERE tenant_id = @tenantId
  AND object_key = @objectKey
```

`SaveAtObjectKeyAsync` and `DownloadAndSaveBinaryAtObjectKeyAsync` now reuse an existing physical file only when the matching tenant media row is active and has the expected category and MIME type. A physical file without that tenant row is retained and fails with `RVIDEO_MEDIA_FILE_WITHOUT_DB_RECORD`; it is never overwritten or rebound to another tenant. Binary downloads short-circuit before downloading when the immutable media already exists.

### Shared provider-success path

Added `IRVideoSceneVideoCompletionService` and `RVideoSceneVideoCompletionService`, registered in `Program.cs`. Both `SceneVideoWorkerHandler` and `AiImageBillingReconciliationWorker` use `CompleteProviderVideoAsync`.

The shared order is:

1. Resolve the tenant-safe immutable key.
2. Persist or reuse tenant-safe media.
3. Complete `SceneVideoVersion` with the actual charged points.
4. Execute `IRVideoSceneMediaFinalizerService.TryFinalizeSceneMediaAsync`.
5. Synchronize RVIDEO lifecycle through `IRVideoJobService.SyncLifecycleAsync`.
6. Complete billing only after local media/version persistence succeeds.
7. For eligible recovery only, complete the associated failed/pending reconciliation render job and append `RVIDEO_VIDEO_RECOVERY_COMPLETED`.

Provider success followed by local persistence failure remains pending reconciliation and retains the same provider task. Provider terminal failure still completes billing as unsuccessful and fails the scene-video version.

### 79AI reconciliation and billing

`AiImageBillingReconciliationItem` now carries `PayerType`, `CustomerChargedPoints`, and `SystemChargedPoints` from the existing billing record. Recovered versions use `item.CustomerChargedPoints`, rather than recomputing current pricing or hard-coding 173.

For `provider_code = 79ai` and `capability_code = rvideo_scene_video_generation`, reconciliation exclusively polls `IRVideo79AiVideoService`. It does not call `IYEScaleTaskClient`, `/image-upload`, or `/create-video`; it reuses the persisted `provider_task_id` and only polls/persists the existing provider result. Existing provider tasks also bypass a second `ReserveAsync`.

`RenderJobService.MarkRecoveredCompletedAsync` is guarded to the same tenant, `render_scene_video` job, matching project/scene/logical request/version, completed version, and either `pending_reconciliation` or a known provider-success persistence failure. It preserves historical events and adds `RVIDEO_VIDEO_RECOVERY_COMPLETED`.

YEScale provider implementation, 79AI endpoint contract, model fallback order, pricing tariff, prompts, and credentials were not changed. No migrations or direct database changes were created or run. `created_by` remains `Guid?`/UUID compatible.

## Changed Files

- `TodoX.Web/Program.cs`
- `TodoX.Web/Services/AiProviders/AiImageBillingReconciliationWorker.cs`
- `TodoX.Web/Services/AiProviders/AiImageBillingService.cs`
- `TodoX.Web/Services/Media/MediaFileService.cs`
- `TodoX.Web/Services/Render/RenderJobService.cs`
- `TodoX.Web/Services/VideoRender/RVideoSceneVideoCompletionService.cs`
- `TodoX.Web/Services/VideoRender/SceneMediaVersioningService.cs`
- `TodoX.Web/Services/VideoRender/SceneVideoWorkerHandler.cs`
- `TodoX.Web/Tests/RVideoProviderPollingRegressionTests.cs`
- `TodoX.Web.Tests/DanceSellRenderHandlerTests.cs`
- `TodoX.Web.Tests/Fakes.cs`
- `TodoX.Web.Tests/SceneImageBatchRenderHandlerTests.cs`
- `TodoX.Web/docs/reports/20260827_rvideo_79ai_video_recovery_storage_reconciliation_fix.md`

## Validation

- `git fetch origin integration/rdance-on-construction-video-core`: remote remained at the starting HEAD before committing.
- `git diff --check`: passed; only Git CRLF normalization notices were emitted.
- `dotnet restore ..\TodoX.Dashboard.sln`: passed, all projects up to date.
- `dotnet build ..\TodoX.Dashboard.sln -c Release --no-restore`: passed.
- `dotnet format TodoX.Dashboard.sln whitespace --verify-no-changes --no-restore --include ...`: passed after applying whitespace-only fixes to `SceneVideoWorkerHandler.cs`.
- Focused Web tests:
  `dotnet test ..\TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~SceneImageBatchRenderHandlerTests|FullyQualifiedName~RVideo"`
  passed, 35/35.
- Focused Phase1B tests:
  `dotnet test Tests\TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RVideoProviderPollingRegressionTests|FullyQualifiedName~RVideoVideoHotfixTests"`
  passed, 52/52.
- Full `TodoX.Web.Tests`: 769/775 passed. Unrelated failures were:
  `RDanceFashionDemoPageTests` (four source/UI assertions),
  `DanceSellPhase2ValidationTests.ReferencePrompt_MatchesTheVerified79AiTryOnPromptExactly`,
  and `DanceSellAi79ReferenceProviderTests.SubmitAsync_UsesVerifiedFashionTryOnFormPayload`.
- Full Phase1B: 152/159 passed. Unrelated failures were:
  missing legacy SQL fixture paths in `RVideoRuntimeSqlTests` (two),
  `TimelapseWorkerClaimRegressionTests.CustomerUiDistinguishesWorkerWaitFromProviderSubmission`,
  and four `TodoXVideoPromptParserTests.ScenePromptMetadata_NormalizesVoiceAliases` cases.
- `dotnet publish TodoX.Web.csproj -c Release --no-restore -o ..\artifacts\publish\todox-dashboard`: passed. Output: `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`.

## Production Verification Checklist

1. Claim the persisted `pending_reconciliation` billing records.
2. Verify 79AI reconciliation polls the existing `provider_task_id` only.
3. Verify no `/image-upload`, `/create-video`, or second `ReserveAsync` occurs.
4. Verify the provider output is persisted or tenant-safe immutable media is reused.
5. Verify the scene video is completed with the persisted billing points.
6. Verify billing completes once, `locked_balance` decreases by the reservation, and no refund is created for provider-success/storage-failure recovery.
7. Verify eligible render jobs receive `RVIDEO_VIDEO_RECOVERY_COMPLETED`.
8. Verify scene finalization and project lifecycle advance, allowing final-video processing when all scenes are ready.
9. Verify no duplicate debit transaction exists.

## Remaining Known Issues

The unrelated full-suite failures listed above remain outside this RVIDEO recovery change. Publish succeeded; deployment and the production checklist are still required in the target environment.
