# RVIDEO Voice-Audio Dapper Binding Fix

## Production Error

PostgreSQL reported:

```text
42703: column "voiceaudioversionid" does not exist
POSITION: 109
```

The affected method is `SceneMediaVersioningService.CompleteSceneVideoVersionAsync`.

## Root Cause

The SQL correctly uses the physical column and parameter:

```sql
voice_audio_version_id=COALESCE(@voiceAudioVersionId, voice_audio_version_id)
```

The Dapper anonymous parameter object omitted the matching binding before this fix. PostgreSQL therefore treated the unresolved token as an identifier and searched for `voiceaudioversionid`.

The final binding is:

```csharp
voiceAudioVersionId = request.VoiceAudioVersionId,
```

All requested completion SQL parameters are now covered by the regression guard in `RVideoRuntimeSqlTests`.

## Schema And Recovery Safety

- The production schema was not changed.
- The canonical column remains `video_render.scene_video_versions.voice_audio_version_id uuid NULL`.
- No `voiceaudioversionid` column was added.
- No migration was created or modified.
- Existing `provider_task_id` values are reused during recovery.
- Recovery polls the existing provider task and does not call `/create-video` or `SubmitAsync` again.
- Existing persisted media is reused at the immutable version-scoped storage key.
- Existing billing reservations are reused; recovery does not call `ReserveAsync`.
- No second point charge or billing logical request is created.
- Version completion still persists `result_media_id`, `public_url`, and `source_file_path`, synchronizes the selected scene video, completes billing after persistence, and marks the recovered render job complete.

## Reconciliation Retry Findings

`AiImageBillingReconciliationWorker` defaults `AiImageBilling:ReconciliationMaxAttempts` to `6` and clamps the configured value between `1` and `100`.

`AiImageBillingService.ClaimReconciliationBatchAsync` claims only records satisfying:

```sql
status IN ('reserved','pending_reconciliation')
AND COALESCE(reconciliation_attempt_count, 0) < @maxAttempts
AND (reconciliation_lock_until IS NULL OR reconciliation_lock_until < now())
AND (
    status = 'pending_reconciliation'
    OR (status = 'reserved' AND reserved_until < now())
)
```

Therefore records already at `reconciliation_attempt_count = 6` are not automatically claimed with the default configuration. The idempotent manual script `database/manual/rvideo-project-11-video-reconciliation-rearm.sql` is provided for project 11 scenes 48-54. It verifies status/provider/capability and resets only reconciliation attempt, lock, and scheduling fields. It does not modify provider task IDs, charged points, wallet balances, logical request IDs, provider identity, or scene-video version identity.

The manual SQL was not executed against production.

## Changed Files

- `TodoX.Web/Services/VideoRender/SceneMediaVersioningService.cs`
- `TodoX.Web/Tests/RVideoRuntimeSqlTests.cs`
- `TodoX.Web/docs/rvideo-voice-audio-dapper-binding-fix-report.md`
- `database/manual/rvideo-project-11-video-reconciliation-rearm.sql`

## Validation

- `dotnet restore TodoX.Dashboard.sln`: passed.
- `dotnet test TodoX.Web\Tests\TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "RVideoRuntimeSqlTests|RVideoProviderPollingRegressionTests|RVideoVideoHotfixTests"`: passed, 102/102.
- `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore --filter "Billing|Reconciliation|RenderJob"`: 28 passed, 1 pre-existing unrelated failure in `BillingAndRatioRegressionTests.RequestedRatioOverridesProviderRouteDefaults` (`expected 16:9`, actual `default`).
- `dotnet build TodoX.Dashboard.sln -c Release --no-restore`: passed, 0 warnings, 0 errors.
- `dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore --include TodoX.Web\Services\VideoRender\SceneMediaVersioningService.cs TodoX.Web\Tests\RVideoRuntimeSqlTests.cs`: passed.
- `git diff --check`: passed.
- `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\todox-dashboard`: passed.
- Publish output: `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`.
- Full solution test suite was not run after this narrow change; the focused RVIDEO suite passed. The unrelated billing test failure remains documented above.

## Git

- Commit SHA: pending commit.
- Branch: `integration/rdance-on-construction-video-core`.
- Push result: pending commit.
