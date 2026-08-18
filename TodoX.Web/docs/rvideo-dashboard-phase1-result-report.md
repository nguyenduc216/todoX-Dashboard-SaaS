# RVIDEO Dashboard Phase 1 - Result Report

## Git

- Branch: `integration/rdance-on-construction-video-core`
- Starting SHA: `6faf7b212d8c9fe2d923aa1f360bc4e3721b23eb`
- Scope: native TodoX RVIDEO dashboard foundation; no Telegram wrapper and no n8n change.
- Commits:
  - `9d1884d` `feat(rvideo): add dashboard shell information and execution mode`
  - `4e000e0` `feat(rvideo): add scene editor and json import export`
  - correction commit: `fix(rvideo): complete information settings and auto render configuration`
- Final SHA: `6047420` (correction implementation commit; this report update follows in a separate commit).

## Implemented

- Added RVIDEO constants and validation for execution mode, lifecycle stage, voice mode, music volume, TTS rate, and supported scene durations `4/6/8/10`.
- Added additive RVIDEO settings repository with tenant-scoped persistence for:
  - `MANUAL` / `AUTO`
  - current stage `INFO/SCENE/IMAGE/VIDEO/RESULT`
  - character mode and snapshot fields
  - voice mode/catalog code/snapshot/rate
  - music catalog code/snapshot/volume
- Added native endpoints:
  - `GET /api/rvideo/projects/{projectId}/settings`
  - `PUT /api/rvideo/projects/{projectId}/settings`
  - `POST /api/rvideo/scenes/import`
  - `POST /api/rvideo/scenes/export`
- Added JSON import/export support with all requested narration aliases:
  `voice`, `dialogue`, `dialogue_text`, `tts_text`, `narration`, `narration_text`,
  `voice_over`, `voiceover`, `script`.
- Added the fifth dashboard tab (`Thông tin`, `Scene`, `Hình ảnh`, `Video`, `Kết quả`) and execution-mode control.
- Disabled browser-driven lifecycle transitions. Browser polling only refreshes display state.
- Added `RVideoLifecycleWorker`, which evaluates AUTO jobs server-side and uses the existing project-scoped idempotent enqueue/claim/lock worker architecture for video and final merge.
- Added regression tests for import aliases/order, manual stop, AUTO transitions, duration validation, and library voice validation.
- Removed the hard-coded AUTO video values `9:16` and `720P`; lifecycle resolves the persisted project prompt settings and covers both `16:9` and `9:16`.
- Completed explicit Character `NONE/UPLOAD/LIBRARY` state, tenant-scoped library selection, local media upload/preview/remove, and immutable runtime snapshots.
- Connected active Voice Library and Music Library catalogs, previews, rate/volume controls, local-MP3 validation, and reload restoration.
- Added per-scene `tts_rate` persistence through scene metadata and editor state.
- AUTO now validates persisted settings before provider work and server-side queues the initial image batch using the existing idempotent project queue path.
- Removed the duplicate `Tự động hoàn thành` control; `MANUAL/AUTO` is the single automation choice.

## Database

Standalone SQL file:

`database/migrations/20260818_rvideo_dashboard_phase1.sql`

The migration creates only `video_render.rvideo_job_settings` and its tenant/mode index. It was **not executed**, per database safety instructions. Existing `video_render` and `render.render_jobs` tables remain unchanged.

## Lifecycle

- Manual mode: user starts image rendering, reviews terminal images, explicitly starts video, then explicitly finalizes.
- Auto mode: server worker validates configuration, queues missing scene images, then evaluates image-to-video/final merge stages without browser or Telegram session dependency.
- Duplicate protection: existing `EnqueueForProjectIfNoneActiveAsync` advisory-lock/idempotency path is reused; existing provider task/version resume behavior is preserved.

## Compatibility

- RDance, Construction Timelapse, Voice Library, Music Library, provider management, and billing code were not behaviorally changed.
- Existing Telegram RVIDEO workflows were not removed or modified.
- No YEScale model/provider metadata was added or guessed.

## Validation

- `dotnet build TodoX.Web.csproj --no-restore`: passed, 0 errors.
- `dotnet test Tests\TodoX.Web.Phase1B.Tests.csproj --no-restore --logger "console;verbosity=minimal"`: passed, 19/19.
- `git diff --check`: passed.
- `dotnet publish TodoX.Web.csproj --no-restore -c Release -o D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`: passed.
- `dotnet format TodoX.Web.csproj --verify-no-changes --no-restore`: not clean because of pre-existing whitespace diagnostics in unrelated files including `AccountRepository.cs`, `AuditRepository.cs`, `WalletService.cs`, and settings/profile repositories. Those files were not changed.

## Deployment

1. Review and manually run `database/migrations/20260818_rvideo_dashboard_phase1.sql`.
2. Deploy the published application from `artifacts/publish/todox-dashboard`.
3. No n8n update is required for this phase.
