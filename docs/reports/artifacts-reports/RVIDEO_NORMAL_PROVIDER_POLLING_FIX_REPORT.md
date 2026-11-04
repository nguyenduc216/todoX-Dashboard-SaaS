# RVIDEO Normal Provider Polling Fix

## Root cause

Normal scene-video provider polling reused `VideoRender:MaxReconciliationRetries` as a hard limit for `providerPollCount`. With the production value of `3`, a known 79AI task that was still processing was no longer scheduled after three polls and was moved to `pending_reconciliation`.

The normal claim predicate also contained the same provider-poll-count gate. This conflated bounded recovery/reconciliation retries with ordinary polling of an already-created provider task.

## Normal poll policy

Normal polling now:

- keeps the existing provider task and `provider_video_id_base`;
- schedules provider polls without enforcing `MaxReconciliationRetries`;
- does not consume `render.render_jobs.attempt_count` when a provider poll is claimed;
- uses `VideoRender:MaxPollDurationMinutes` as the bounded elapsed-time stop policy (configured as 15 minutes in `appsettings.json`, with a 30-minute code default);
- transitions a timed-out known task to `pending_reconciliation` with `SCENE_VIDEO_PROVIDER_POLL_TIMEOUT`, retaining the provider identifiers.

`providerPollCount` remains recorded for diagnostics and reconciliation accounting, but it is not a normal polling stop condition.

## Reconciliation policy

Persistent reconciliation continues to call `ScheduleProviderPollAsync` with its default `enforceReconciliationLimit=true`, so `VideoRender:MaxReconciliationRetries` still bounds reconciliation retries. The reconciliation worker was not changed.

## Provider status mapping

The provider adapter contract and existing normalized mapping are unchanged. Queued/pending/active/processing states continue polling the same provider task; success proceeds to download/reconciliation; failure follows the existing failure path. No fallback order, model policy, endpoint, payload, or provider routing changed.

## Duplicate submit guard

Known-task paths continue to poll the persisted provider task before submission. The normal polling change only removes the accidental three-poll ceiling; it does not introduce another submit path.

## Attempt count

Provider-poll claims keep `attempt_count` unchanged. The claim update removes the transient `providerPoll` marker and increments `attempt_count` only for ordinary submit work. The poll scheduler now writes `providerPollCount` and `providerPollStartedAt` together in one nested JSONB expression so the count cannot be overwritten by the timestamp update.

## Recovery of existing ID_BASE

Existing rows with persisted provider identifiers remain eligible for the existing recovery/reconciliation path. The fix does not infer identifiers, resubmit known tasks, or alter database schema.

## Files changed

- `TodoX.Web/Services/Render/RenderJobService.cs`
- `TodoX.Web/Services/VideoRender/SceneVideoWorkerHandler.cs`
- `TodoX.Web/Services/VideoRender/VideoRenderOptions.cs`
- `TodoX.Web/appsettings.json`
- `TodoX.Web.Tests/DanceSellRenderHandlerTests.cs`
- `TodoX.Web/Tests/RVideoProviderPollingRegressionTests.cs`

## Validation

- Targeted tests: **30 passed, 0 failed**.
- Release build: **passed**, 0 errors, 45 existing Razor nullable warnings.
- Publish: **passed** to `artifacts/publish/todox-dashboard`.
- Database migration: **not required**; no SQL migration was created or executed.
- Production jobs were not retried and no production database was modified.

## Commit

`853d3146c782c7d419dab96665e4d258a66c28b8` before the report-SHA amend.
