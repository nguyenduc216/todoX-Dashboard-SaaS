# RVIDEO Provider Resource-Unavailable Recovery

Date: 2026-09-13

## Status

Implemented the poll-time recovery hotfix for an existing 79AI scene-video task that repeatedly returns `NOT_RESOURCES` without progress evidence.

## Root Cause

The state resolver reads `await _jobs.GetEventsAsync(job.Id)`, which correctly scopes history to `render.render_job_events`. However, the canonical `RVIDEO_VIDEO_PROVIDER_RESOURCES_UNAVAILABLE` event was previously written only with `_repo.AddProjectEventAsync`, which stores `video_render.video_project_events`. Consequently, every poll saw no prior canonical job observation: `ResourceUnavailableCount` behaved as `1` and `FirstSeenAt` reset to the current poll time. The threshold and grace conditions could never be satisfied.

The fix adds a bounded, event-derived decision for poll-time resource unavailability. It does not alter provider routing, payloads, identifiers, normal retries, duration fallback, or duplicate-submit protection.

## Behavior

For an existing task with both `providerTaskId` and `providerVideoIdBase`:

- First and early repeated `NOT_RESOURCES` responses remain pending and poll the same task.
- Historical/current progress evidence prevents terminalization.
- After the configured threshold and grace period, with no progress evidence, the current candidate is terminalized and the existing fallback resolver advances to the next model candidate.
- The default threshold is `3` resource-unavailable poll observations and the default grace period is `30` seconds.
- The classification is `POLL_RESOURCE_UNAVAILABLE_NO_PROGRESS`.
- Same-model duration fallback remains limited to `PROVIDER_DURATION_REJECTED`; resource starvation advances to the next model candidate.

The resource-unavailable count and first-seen time are derived from persisted `render_job_events` data, so the decision survives worker restart without schema changes. `RVIDEO_VIDEO_PROVIDER_RESOURCES_UNAVAILABLE` is the canonical persisted observation event and is counted once per poll. The companion `RVIDEO_VIDEO_RESOURCE_UNAVAILABLE_RETRY` event is diagnostic/scheduling metadata only and is never included in the observation count.

Each poll now writes exactly one canonical event through `_jobs.AddEventAsync(job.Id, ...)` after resolving the current state and before terminalization or rescheduling. The existing project-level event remains for UI/audit, but is not used for state counting. The render-job event includes the scene/version/job identity, both provider identifiers, candidate information, count, stable first-seen timestamp, progress fields, threshold/grace values, and sanitized provider response.

Progress evidence includes a positive progress/percent value, `ACTIVE`, `PROCESSING`, or `SUCCESSFUL` status evidence, provider video/work identifiers, and explicit output/download URLs. Arbitrary `url`, source-image, endpoint, and callback URL properties are not treated as provider progress. A task without both durable identifiers is never treated as safely terminalizable.

## Scene 312 Recovery Procedure

For project `59`, scene `312`, task `fa5dba97e8b331e5`, and id-base `54320c4df1202e58`:

1. Deploy the build after applying no database migration.
2. Leave the existing provider task and identifiers intact.
3. Allow the normal worker to claim the pending reconciliation item.
4. The worker polls the same task while below the threshold or while progress evidence exists.
5. If the task reaches three no-progress `NOT_RESOURCES` observations after at least 30 seconds, the worker records the terminal classification, closes the current billing attempt, marks that scene version failed, and submits the next configured model candidate through the existing fallback path.
6. Verify the lifecycle events and provider diagnostics. Do not manually delete identifiers or resubmit the legacy task.

The following is a guarded preview/requeue script for an operator to review and execute manually. It is not run by the application or by this task. Run the `SELECT` first, verify exactly one row, then replace `ROLLBACK` with `COMMIT` only after approval:

```sql
BEGIN;

SELECT id, status, provider_task_id, provider_video_id_base, input_json,
       worker_key, lock_owner, lock_until, started_at
  FROM render.render_jobs
 WHERE id = '79356436-b3ee-486f-9765-02fc2c5c5d0e'
   AND provider_task_id = 'fa5dba97e8b331e5'
   AND provider_video_id_base = '54320c4df1202e58';

UPDATE render.render_jobs
   SET status = 'pending_reconciliation',
       retry_after = now(),
       worker_key = NULL,
       lock_owner = NULL,
       lock_until = NULL,
       started_at = NULL,
       input_json = jsonb_set(
           jsonb_set(COALESCE(input_json, '{}'::jsonb), '{providerPoll}', 'true'::jsonb, true),
           '{providerPollStartedAt}', to_jsonb(now()::text), true)
 WHERE id = '79356436-b3ee-486f-9765-02fc2c5c5d0e'
   AND provider_task_id = 'fa5dba97e8b331e5'
   AND provider_video_id_base = '54320c4df1202e58'
   AND status IN ('rendering', 'pending_reconciliation');

SELECT id, status, provider_task_id, provider_video_id_base, input_json,
       worker_key, lock_owner, lock_until, started_at
  FROM render.render_jobs
 WHERE id = '79356436-b3ee-486f-9765-02fc2c5c5d0e';

ROLLBACK;
```

This preserves both provider identifiers, does not reset `attempt_count`, does not delete project events, and does not submit a new Fast task. Confirm the deployed schema uses the shown `render_jobs` columns before any manual execution.

## Files Changed

- `TodoX.Web/Services/VideoRender/SceneVideoWorkerHandler.cs`
- `TodoX.Web/Tests/RVideoVideoHotfixTests.cs`
- `artifacts/reports/RVIDEO_PROVIDER_RESOURCE_UNAVAILABLE_RECOVERY_REPORT.md`

No migrations, appsettings files, provider adapters, endpoint/payload code, or unrelated protected subsystems were changed.

## Validation

Passed:

- `dotnet test Tests\TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RVideoVideoHotfixTests"` compiled and ran; the new hotfix cases passed, with 113 passed and 4 pre-existing legacy source/assertion failures.
- `dotnet test Tests\TodoX.Web.Phase1B.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~FirstPollTimeNotResources|FullyQualifiedName~RepeatedPollTimeNotResources|FullyQualifiedName~ProgressHistoryBlocks|FullyQualifiedName~CurrentOutputEvidence|FullyQualifiedName~PollTimeResourceTerminalFallback|FullyQualifiedName~DurationRejectedStillUses"`: 11 passed, 0 failed.
- Canonical-observation and false-positive regression tests: 13 passed, 0 failed.
- `dotnet build TodoX.Web\TodoX.Web.csproj --configuration Release --no-restore`: passed, 0 errors.
- `git diff --check`: passed.

The four class-level failures are pre-existing assertions in the same legacy test class: two expect source strings removed by earlier completed RVIDEO changes, one reflects an outdated private-method signature, and one scans source text that necessarily contains the word `authorization` in the sanitizer implementation. They do not exercise the new recovery logic.

## Publish

Published with:

`dotnet publish TodoX.Web\TodoX.Web.csproj --configuration Release --no-build --output artifacts\publish\todox-dashboard`

Result: passed to `artifacts/publish/todox-dashboard`.

## Commit

Commit: pending

The report itself is stored under `artifacts`, which is ignored by the repository's normal ignore rules; it is force-added separately so the requested artifact is versioned.

## Remaining Risks

- The policy is intentionally bounded by event history and configured threshold/grace values; operational tuning may be needed if 79AI resource recovery normally takes longer.
- Provider-side state may remain ambiguous when no progress evidence is returned. In that case the worker continues reconciling rather than risking a duplicate submit.
