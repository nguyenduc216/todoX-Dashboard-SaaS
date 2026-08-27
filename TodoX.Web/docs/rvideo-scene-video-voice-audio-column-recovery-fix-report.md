# RVIDEO Scene-Video Voice Audio Column Recovery Fix

## Production Symptom

Project 11 had successful 79AI scene-video tasks for scenes 48 through 54, but all seven local scene-video versions were marked failed with `RVIDEO_VIDEO_PERSIST_FAILED`. The PostgreSQL error was:

```text
42703: column "voiceaudioversionid" does not exist
POSITION: 109
```

The provider task IDs were already populated and were treated as authoritative. No new 79AI video task was created by this fix.

## Trace And Root Cause

The success path is:

`SceneVideoWorkerHandler.HandleProviderVideoAsync`
-> `RVideoSceneVideoCompletionService.CompleteProviderVideoAsync`
-> `SceneMediaVersioningService.CompleteSceneVideoVersionAsync`
-> scene-video version and `video_project_scenes` persistence.

The checked-in completion statement is:

```sql
UPDATE video_render.scene_video_versions
   SET status='completed',
       voice_audio_version_id=COALESCE(@voiceAudioVersionId, voice_audio_version_id),
       result_media_id=COALESCE(@resultMediaId, result_media_id),
       public_url=@videoUrl,
       source_file_path=@videoPath
 WHERE id=@versionId AND tenant_id=@tenant;
```

The canonical SELECT is:

```sql
voice_audio_version_id AS VoiceAudioVersionId
```

Repository-wide source inspection found no current runtime statement containing the raw PostgreSQL identifier `voiceaudioversionid`. The exact malformed statement from the older deployed artifact is not present in the current branch checkout, so its complete historical text cannot be reconstructed without production SQL logging. The PostgreSQL error itself identifies the malformed identifier: an unquoted C# property name, `VoiceAudioVersionId`, was emitted as a database identifier. PostgreSQL folds unquoted identifiers to lowercase, so it looked for `voiceaudioversionid` instead of the existing `voice_audio_version_id` column.

The production schema remains unchanged and correct:

```text
video_render.scene_video_versions.voice_audio_version_id uuid NULL
```

No schema migration or database update is required.

## Changed Files And Methods

- `Tests/RVideoRuntimeSqlTests.cs`
  - Corrected repository-root resolution for SQL contract tests.
  - Added regression coverage for canonical scene-video mappings, nullable/UUID voice-audio hydration, recovery reuse, persistence fields, scene synchronization, and billing ordering.
- `docs/rvideo-scene-video-voice-audio-column-recovery-fix-report.md`
  - This report.

The application implementation already present at the target branch HEAD was audited and preserved:

- `SceneMediaVersioningService.SelectSceneVideoVersionSql`
- `SceneMediaVersioningService.CompleteSceneVideoVersionAsync`
- `SceneVideoWorkerHandler.HandleProviderVideoAsync`
- `AiImageBillingReconciliationWorker.Reconcile79AiVideoAsync`
- `RVideoSceneVideoCompletionService.CompleteProviderVideoAsync`
- `RenderJobService.MarkRecoveredCompletedAsync`

## Recovery Behavior

For a failed local-persistence version with a non-empty `provider_task_id`, the worker:

1. Locates the existing recoverable `scene_video_versions` row.
2. Reads the existing provider task ID.
3. Reuses the existing billing record via `GetReservationAsync`.
4. Polls the existing 79AI task.
5. Downloads the successful provider output at the version-scoped storage key.
6. Calls `CompleteSceneVideoVersionAsync`.
7. Populates `result_media_id`, `public_url`, and `source_file_path`, sets `status='completed'`, and synchronizes `selected_video_version_id`, `scene_video_url`, and `scene_video_path`.
8. Completes billing only after local persistence/version completion succeeds.
9. Marks the recovered render job complete when the persisted version and billing relationship are valid.

The reuse branch is guarded before the provider submit branch, so an existing task ID does not call `SubmitAsync` or `ReserveAsync`. The reconciliation worker calls the 79AI video polling service and `CompleteProviderVideoAsync`; it does not call `/create-video`.

## Validation

- `dotnet restore TodoX.Dashboard.sln`: **passed**.
- `dotnet test Tests\\TodoX.Web.Phase1B.Tests.csproj --filter RVideoRuntimeSqlTests -c Release`: **passed, 11/11**.
- `dotnet test TodoX.Web\\Tests\\TodoX.Web.Phase1B.Tests.csproj -c Release --no-build --filter "RVideoProviderPollingRegressionTests|RVideoVideoHotfixTests|RVideoRuntimeSqlTests"`: **passed, 81/81**.
- `dotnet test TodoX.Web.Tests\\TodoX.Web.Tests.csproj -c Release --no-build --filter "VbeeSceneRuntimeTests|RenderJobServiceTests|AiImageBillingTests|Ai79TaskClientTests"`: **passed, 68/68**.
- `dotnet build TodoX.Dashboard.sln -c Release --no-restore`: **passed, 0 warnings, 0 errors**.
- `dotnet test TodoX.Dashboard.sln -c Release --no-build`: **775 passed, 3 pre-existing failures**:
  - `DanceSellPhase2ValidationTests.ReferencePrompt_MatchesTheVerified79AiTryOnPromptExactly`
  - `DanceSellAi79ReferenceProviderTests.SubmitAsync_UsesVerifiedFashionTryOnFormPayload`
  - `BillingAndRatioRegressionTests.RequestedRatioOverridesProviderRouteDefaults`
- `dotnet publish TodoX.Web\\TodoX.Web.csproj -c Release --no-restore -o artifacts\\publish\\todox-dashboard`: **passed**.
- Publish output: `D:\\todoX\\Dashboard-web\\TodoXPortal\\todoX-Dashboard-SaaS\\artifacts\\publish\\todox-dashboard`.
- Source-only search for `voiceaudioversionid` in `.cs`, `.sql`, and `.md` files: **no runtime SQL occurrence**; matches are only DTO/property names and regression assertions.
- `git diff --check`: **passed**.
- `dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore`: **failed on pre-existing whitespace findings across unrelated files**; no formatting changes were applied.

- Commit SHA: `6d870de`
- Pushed branch: `integration/rdance-on-construction-video-core`
