# RVIDEO Provider Identifier Persistence Fix

## Root Cause

The deployed implementation introduced `ProviderTaskIdMetadata` as an application-only DTO/property and then referenced `@providerTaskIdMetadata` in the scene-video persistence SQL without including it in Dapper's anonymous parameter object. The relevant deployed statement was:

```sql
UPDATE video_render.scene_video_versions
   SET status='submitted',
       provider_task_id=@providerTaskId,
       render_config_json=COALESCE(render_config_json, '{}'::jsonb)
           || jsonb_strip_nulls(jsonb_build_object(
               'providerVideoIdBase', @providerTaskId,
               'providerTaskId', @providerTaskIdMetadata))
 WHERE id=@versionId AND tenant_id=@tenant;
```

`@providerTaskIdMetadata` was not supplied to Dapper. PostgreSQL consequently parsed the unresolved token as the unquoted identifier `providertaskidmetadata`, causing `42703` (the reported position is within that `jsonb_build_object` expression). A matching pending-reconciliation UPDATE had the same unbound parameter defect.

The current source tree contains no production SQL reference to `ProviderTaskIdMetadata`, `providerTaskIdMetadata`, `provider_task_id_metadata`, or `providertaskidmetadata`. No database column with that name is added.

## Persistence Contract

| Source | Durable field | Purpose |
| --- | --- | --- |
| `videoInfo.task_id` | `provider_task_id` | Provider task/billing/completion correlation |
| `videoInfo.id_base` | `provider_video_id_base` | Required 79AI status poll identifier |

`POST /ai/video` receives `videoId=provider_video_id_base`; it never receives `provider_task_id`. Both identifiers are saved immediately after a successful submit in `render.render_jobs` and `video_render.scene_video_versions`.

Rows without `provider_video_id_base` are not allowed to guess it from `provider_task_id`. A row with a task ID but no base ID stays in `pending_reconciliation` and is not resubmitted or polled with the task ID. Reconciliation selection requires a nonempty persisted base ID.

## Reconciliation Bound

Provider polling uses `input_json.providerPollCount`, bounded by `VideoRender:MaxReconciliationRetries` (default `3`). Poll claims do not increment `attempt_count`; submit attempts retain their existing accounting. When scheduling reaches the bound, the job remains `pending_reconciliation` with `SCENE_VIDEO_RECONCILIATION_LIMIT_REACHED` instead of polling indefinitely.

## Database Migration

**Required: YES.** Apply [RVIDEO_PROVIDER_VIDEO_ID_BASE_MIGRATION.sql](RVIDEO_PROVIDER_VIDEO_ID_BASE_MIGRATION.sql) manually before deploying this application change. It is idempotent and is not executed by the app, build, publish, or this task.

```sql
ALTER TABLE render.render_jobs
    ADD COLUMN IF NOT EXISTS provider_video_id_base text;

ALTER TABLE video_render.scene_video_versions
    ADD COLUMN IF NOT EXISTS provider_video_id_base text;
```

## Files Changed

- `TodoX.Web/Services/Render/RenderJobModels.cs`
- `TodoX.Web/Services/Render/RenderJobService.cs`
- `TodoX.Web/Services/VideoRender/Ai79VideoGenerationProviderAdapter.cs`
- `TodoX.Web/Services/VideoRender/VideoGenerationProviderAdapter.cs`
- `TodoX.Web/Services/VideoRender/SceneMediaVersioningService.cs`
- `TodoX.Web/Services/VideoRender/SceneVideoWorkerHandler.cs`
- `TodoX.Web/Services/VideoRender/VideoRenderRepository.cs`
- `TodoX.Web.Tests/RVideoSceneVideoRecoveryAndDiagnosticsTests.cs`
- `TodoX.Web.Tests/DanceSellRenderHandlerTests.cs` (test fake updated for the new interface member)
- `TodoX.Web/Tests/RVideoVideoHotfixTests.cs` (legacy source assertion updated)
- `artifacts/reports/RVIDEO_PROVIDER_VIDEO_ID_BASE_MIGRATION.sql`

No provider endpoint, payload field, provider routing, model policy, fallback ordering, or normal submit retry policy was changed.

## Validation

- `dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~RVideoSceneVideoRecoveryAndDiagnosticsTests|FullyQualifiedName~RVideoCoreExecutionTests"`: **24 passed, 0 failed**.
- Expanded 79AI/RVIDEO filter: **71 passed, 2 failed**. The failures are pre-existing legacy 79AI tests expecting video submit responses without `id_base`; that behavior was intentionally rejected by the already-committed 79AI polling contract.
- Full `TodoX.Web.Tests`: **924 passed, 20 failed**. The remaining failures are pre-existing, unrelated RDance/UI, Timelapse, billing regression, and the same two legacy 79AI contract tests.
- `dotnet build TodoX.Web/TodoX.Web.csproj --configuration Release --no-restore`: **passed**, 0 errors (45 existing Razor nullable warnings).
- `dotnet publish TodoX.Web/TodoX.Web.csproj --configuration Release --no-build --output artifacts/publish/todox-dashboard`: **passed** to `artifacts/publish/todox-dashboard`.

## Commit

Pending final commit and push.
