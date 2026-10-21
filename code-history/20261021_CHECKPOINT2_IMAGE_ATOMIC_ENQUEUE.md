# Checkpoint 2 — Image rerender atomic enqueue (Legacy + V2)

- Baseline: `d24c7a406da031755851597501796e2a8f4fb32d` (Checkpoint 1, branch `integration/todox-unified-release-clean-20261021`).
- Tag: `TODOX-20261015-RVIDEO-IMAGE-ATOMIC-ENQUEUE-001`.
- Scope: Image Rerender only (Legacy `RenderVideoJobs.razor` + V2 `RVideoV2Preview.razor` + `SceneMediaVersioningService` + `RVideoSceneImageRenderAction`).

## Problem closed

Before this checkpoint the queued scene-image version and its `render_scene_image` job were persisted on
**separate connections/transactions**: `SceneMediaVersioningService.CreateQueuedImageVersionAsync` committed
its own transaction, then the caller called `RenderJobService.EnqueueAsync` (another connection), then
`TryActivateImageRenderJobAsync` bound the FK and moved the job `draft` -> `queued` in a third transaction.
A failure between those steps could leave a **stranded queued version** with no job (blocking the scene via
`SCENE_IMAGE_ACTIVE`) or an **orphan draft job**, and a retry could double-enqueue.

## Change

- New `ISceneMediaVersioningService.CreateQueuedImageVersionWithJobAsync` performs, on **one connection and one
  transaction**: scene-lock -> idempotent lookup by `logical_request_id` -> active-version guard -> version
  INSERT -> draft job INSERT -> FK bind (`render_job_id`) -> `draft` -> `queued` activation -> single `tx.Commit()`.
  The intermediate state is therefore unreachable: either everything lands or nothing does.
- Idempotent retry: a repeat with the same `logical_request_id` returns `SceneImageEnqueueOutcome.Active` with
  the already-bound job id, so retries never create a second job.
- `RVideoSceneImageRenderAction.EnqueueRerenderAsync` (V2) now delegates to the atomic call and no longer
  performs a second `EnqueueAsync`. Its `MarkImageEnqueueUnknownAsync` recovery path was removed because an
  uncommitted transaction cannot strand a version; the action reports `Success`/`AlreadyActive`, never a
  catch-and-delete or an untracked queued version.
- `CreateQueuedImageVersionAsync` active-guard now also covers the `pending` status (was `processing`-only), so a
  version parked for reconciliation still blocks a concurrent duplicate attempt.
- V2 mobile action row gained the missing image re-render button so mobile and desktop share one lifecycle.

## Tests

- `TodoX.Web.Tests.SceneImageVersionConcurrencyContractTests` (4 tests):
  - `LegacyImageJobIsDraftUntilTransactionalBindingAndActivation` — Legacy job stays draft until binding+activation.
  - `QueuedVersionChecksActiveSceneAfterIdempotentLookupAndBeforeInsert` — lock -> idempotent lookup -> active check -> insert order.
  - `V2ImageButtonsUseDraftActivationAndKeepUnknownOutcome` — V2 exposes both image buttons and routes images through the image action, never the video action.
  - `AtomicImageEnqueueCommitsVersionJobBindAndActivationOnce` (new regression lock) — version insert < job insert < FK bind < activation < exactly one commit.
- Focused run: `SceneImageVersionConcurrencyContractTests` **4 passed / 0 failed / 0 skipped**.
- Scene-image suite: `FullyQualifiedName~SceneImage` **14 passed / 0 failed**.
- Builds: `TodoX.Web` Release **0 errors**; `TodoX.Landing` Release **Build succeeded**.
- Pre-existing (NOT introduced here): full suite is 22 failed / 553 passed / 575 total, and the failing test-name
  set is **byte-identical** to baseline `d24c7a4` (22 failed / 549 passed / 571 total; `Compare-Object` returned
  no differences). This checkpoint adds 4 passing tests and introduces **zero new failures**. The 22 pre-existing
  failures are video/provider-polling tests tracked separately and were not repaired here (out of scope; frozen).

## Not done / prerequisites

- No production SQL executed, no migration created, no worker restarted, no IIS deploy.
- Schema-dependent note: the atomic path relies on the existing `video_render.scene_image_versions` /
  `render.render_jobs` columns only; no new column or constraint is required. No unique constraint on
  `logical_request_id` exists, so idempotency is enforced by the scene lock plus the ordered in-transaction lookup.
- Video Rerender, Billing/Points, provider payloads, Voice/TTS and Landing were not modified.