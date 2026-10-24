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
