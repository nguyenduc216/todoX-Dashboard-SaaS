# Recover confirmed tasks after local polling timeout

FEATURE_ID: TODOX-RVIDEO-VIDEO-RERENDER. CHANGE_ID: TODOX-20261025-KNOWN-TASK-RECOVERY-001. Baseline: c6275e19f5454b0248ae78802cdbf685f5fdf12f.

## Root cause

ProviderRenderHardTimeoutEvaluator retains SubmittedAt (or the original attempt timestamp); Processing past the local window enters pending_reconciliation rather than establishing provider failure. That is correct, but SceneVideoReconciliationWorker's existing SchedulePersistentProviderPollAsync opened a writer transaction without passing it to the update or committing it. The update could report success, then roll back on disposal; duplicate schedulers could both report true. Jobs stayed pending with retry_after NULL. The normal poll scheduler also refuses an expired original poll window. Therefore the existing recovery scan never durably returned these tasks to the queue.

## Fix and limits

The existing reconciliation worker and normal scene worker are reused. Persistent scheduling commits on the same writer transaction, verifies exactly one matching job/version/CONFIRMED_PROVIDER_TASK intent, current generation, due time and expired queue lease, then atomically queues only that job as a provider poll. Already queued jobs cannot be repeatedly postponed by the scan. Poll claims preserve attempt_count and immutable provider identities.

A separate persisted `providerRecoveryStartedAt` / `providerRecoveryPollCount` budget in existing input_json bounds recovery without resetting submitted_at or providerPollStartedAt. Defaults: `VideoRender:KnownTaskRecoveryMaxPolls=20` (clamped 1–200), `VideoRender:KnownTaskRecoveryWindowMinutes=1440` (clamped 1–10080). Exhaustion leaves pending_reconciliation and IDs intact; it never means provider failure and never permits a new submission. Normal scheduling during a recovery window respects that same budget.

A per-tenant/job PostgreSQL advisory lock spans the existing handler's poll, download and completion. A duplicate handler returns deferred before HTTP even after a queue lease expires; the lock connection/process lifetime controls release. Moving the scene job to pending_reconciliation releases its queue lease so the next due recovery scan can act. Superseded, ambiguous, conflicting-identity and manual-review jobs do not become pollable through this path. No new service, submit path, provider API or pricing rule was introduced.

SUCCESS uses the existing RVideoSceneVideoCompletionService: immutable output key, download/storage, existing success billing semantics, guarded version selection and lifecycle continuation. Processing stays nonterminal. The same job/version/logical IDs and CONFIRMED_PROVIDER_TASK intent remain; no new billing reservation or provider submit is made.

## Operator procedure for Project 196 / scenes 865–872

1. Deploy the matching application package under the operator's normal procedure, preserving configuration and uploads. No SQL migration is required. Do not queue a new render, use Supersede, reset historical IDs, or mark a local timeout as provider failure.
2. Keep the existing `RenderQueue:Enabled` worker configuration enabled. Both SceneVideoReconciliationWorker and SceneVideoJobWorker are already registered. The scan interval defaults to 30 seconds (`VideoRender:ReconciliationIntervalSeconds`). Expired queue leases are released by the timeout status path; pre-existing live leases are respected until they expire.
3. The existing scan automatically discovers the eight current confirmed tasks, persists retry_after and the poll-only marker, then the scene worker polls the persisted provider_video_id_base. No production SQL/manual status update is needed to kick off the first recovery window.
4. Observe existing job/project events: JOB_PROVIDER_POLL_SCHEDULED, RVIDEO_VIDEO_POLL_STARTED/COMPLETED, RVIDEO_VIDEO_PERSIST_SUCCESS/REUSED and RVIDEO_VIDEO_RECOVERY_COMPLETED. Verify exact original task/base IDs on job/version/intent, completed version with result_media_id, selected scene output and completed job. Dashboard reload/polling reads those persisted selections.
5. If the provider is still Processing, the same task is polled again only within the recovery budget/window. On exhaustion or identity conflict retain pending_reconciliation and investigate. Do not reset counters/IDs or resubmit automatically. An explicit reviewed extension of configuration may be appropriate for a still-valid task; it is not evidence of failure or permission for a new render.

Codex has not run production SQL, called 79AI, deployed IIS or recovered production assets. Operator performs live acceptance.

## Verification scope

PostgreSQL integration reconstructs all eight current generation-1 tasks with confirmed matching intents, expired local poll clocks and retry_after NULL. The real worker and completion code run against a local Postman simulation: 16 polls, eight local downloads, zero submits, eight selected completed scene versions, unchanged intent rows/provider IDs and no new provider reservation. Concurrent scheduler and handler exclusion, live lease, budget/window and ambiguous-intent refusals are checked. Storage/credential/lifecycle boundaries are test doubles; this is not a production provider/browser acceptance test.

Targeted results and broader baseline comparison are recorded in the change history and release manifest. No DB schema or SQL migration file is changed.
