# TODOX-RVIDEO-VIDEO-RERENDER

Verified baseline: code-history/20261019_VIDEO-RERENDER-RELEASE.md and the current guarded scene-video pipeline. This feature index is established from source on 2026-10-22; it does not infer unrecorded history.

- SceneMediaVersioningService owns immutable version identity, media linkage, selection and guarded recovery.
- SceneVideoSubmitCoordinator owns durable submit intents and submit guard classification.
- SceneVideoWorkerHandler owns provider submission/polling and no-fallback transitions after task acceptance.
- RVideoSceneVideoCompletionService persists media before version/job completion; recovery preserves existing selection.
- RenderJobService owns operation-level dispatch/enqueue and polling lifecycle locks.
- Legacy RenderVideoJobs uses SceneVideoUiState for independent selected playback/latest operation and rerender blockers.
- Existing DI registrations are reused. Dapper/Npgsql and existing tenant/scene lock ordering remain authoritative.
- Schema owners: video_render.scene_video_versions, render.render_jobs, render.scene_video_submit_intents, media.media_files and existing billing records. Existing submit-intent preflight/forward/postcheck scripts remain prerequisites; no new migration is introduced by P0.
- No provider payload, image/audio generation, pricing or production configuration changes.

## Changes

1. 2026-10-22 — TODOX-20261022-SCENE05-P0-001: Tasks 1/2/3, selective main integration and operator package. See ../20261022_TODOX-20261022-SCENE05-P0-001.md. Main SHA and package evidence are reported after verification.
2. 2026-10-22 — TODOX-20261022-SCENE05-RELEASE-002: scoped operator reconciliation SQL and audited terminal-failure submit-guard release. See ../20261022_TODOX-20261022-SCENE05-RELEASE-002.md. No production action; terminal evidence and operator approval are still required.

3. 2026-10-24 — Operation Supersede V1: explicit generation-aware user operation in V2/Legacy; atomic draft activation, durable submit and writer fences, immutable old outcome/selection. Manual migration required; see ../../database/manual/scene-video-operation-v1-README.md. Local PostgreSQL transaction/concurrency and focused UI/backend tests cover the implementation. No production SQL/provider/IIS/deployment action.

4. 2026-10-25 — TODOX-20261025-SCENE07-UI-001: expose explicit new-operation consent for failed owned/provider-task attempts; keep unsafe old-owner retry disabled; accessible scene-image rerender and V2 operation polling. Adds one read-only exact-job collector; no new migration or persistence/provider/billing change. See ../20261025_TODOX-20261025-SCENE07-UI-001.md.

5. 2026-10-25 — TODOX-20261025-RVIDEO-FINALIZE-001: bind the generated image-version UUID into the atomic draft's worker input, keep prepared/abandoned video drafts out of current-operation UI state, and fix the not-yet-installed V1 final-version fence's bigint/uuid comparison. PostgreSQL 17.10 tests cover real image enqueue/completion, initial and explicit-generation video completion/final persistence, and precise stale-merge rejection. See ../20261025_TODOX-20261025-RVIDEO-FINALIZE-001.md. Production export comparison remains unverified; no runtime/deployment review is required by this task.

6. 2026-10-25 — TODOX-20261025-SUPERSEDE-PRESUBMIT-001: the shared coordinator guard uses proven activated head/job/version/logical ownership to exclude earlier-generation scene operations, including ancestors not directly named by the transition. Preserves current-operation duplicate checks, generation-zero behavior, physical-send locks and all historical outcomes/billing. Source-only fix: no additional migration after V1. 99 focused cases pass, including 17 PostgreSQL-suite cases. See ../20261025_TODOX-20261025-SUPERSEDE-PRESUBMIT-001.md and ../../TodoX.Web/docs/rvideo-supersede-presubmit-p0-20261025.md.

7. 2026-10-25 — TODOX-20261025-KNOWN-TASK-RECOVERY-001: fix the persistent poll scheduler's uncommitted transaction, bounded same-task recovery window/count and per-job poll/download mutex. Existing worker/completion flow restores successful outputs without Reserve/Submit or generation changes. PostgreSQL plus local Postman scenario covers all eight Project 196 tasks, Processing then SUCCESS, immutable IDs/intents and duplicate exclusion. Targeted 100 PASS; expanded suite retains the same eight baseline failures, no new failures. No migration. See ../20261025_TODOX-20261025-KNOWN-TASK-RECOVERY-001.md.
