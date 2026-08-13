# Timelapse Phase 2B Render Runtime

Date: 2026-08-13

## Scope

Phase 2B adds the Timelapse customer job-detail workflow for ordered image/video rendering state, dependency-aware retry, finalization state, and the operational Scene/Result UI. It preserves the Phase 2A commercial service contract: `ServiceId` is mandatory, server-authoritative, and customer sell price is resolved from the exact selected service.

## DB Tables

Reused:

- `render.render_jobs`: parent Timelapse draft/job state and customer ownership.
- `render.render_job_events`: append-only workflow events.
- `media.media_files`: original image, generated images, clip outputs, and final output media references.

Added by `database/migrations/20260813_timelapse_phase_2b_render_workflow.sql`:

- `timelapse.timelapse_image_stages`: current image stage state.
- `timelapse.timelapse_image_stage_versions`: attempt/version history for image stages.
- `timelapse.timelapse_video_clips`: current video clip state.
- `timelapse.timelapse_video_clip_versions`: attempt/version history for video clips.
- `timelapse.timelapse_final_outputs`: versioned final merged output state.

The migration is idempotent and does not modify existing Phase 2A draft payloads. Existing drafts build their stage graph on first Start/Resume.

## State Machine

Parent Timelapse states:

- `DRAFT`
- `GENERATING_IMAGES`
- `IMAGES_READY`
- `GENERATING_VIDEOS`
- `VIDEOS_READY`
- `FINALIZING`
- `COMPLETED`
- `PAUSED`
- `FAILED`

Child operation states:

- `WAITING`
- `RENDERING`
- `COMPLETED`
- `FAILED`
- `INVALIDATED`

`HasActiveOperations` is derived from child image/video/finalizer state. `CanEditRequest` is true only when there are no active child operations and the parent state is safely stopped (`DRAFT`, `PAUSED`, `FAILED`).

## Stage Graph

The stage graph is built by `TimelapseStageGraphBuilder`.

- 3 scenes: images `[0,35,70,100]`, videos `0->35`, `35->70`, `70->100`, generated order `[70,35,0]`.
- 4 scenes: images `[0,25,50,75,100]`, generated order `[75,50,25,0]`.
- 5 scenes: images `[0,20,40,60,80,100]`, generated order `[80,60,40,20,0]`.
- 6 scenes: images `[0,25,40,55,70,85,100]`, generated order `[85,70,55,40,25,0]`.

The 100% stage is the customer original image and is stored as `COMPLETED`; it is not auto-generated.

## Reverse Image Dependency

Generated images are reverse-dependent from the customer final image:

`100 -> 70 -> 35 -> 0` for a 3-scene job.

Each generated image stage stores `depends_on_progress_percent` and `prompt_snapshot_json`. The profile snapshot is captured from `public.todox_timelapse_prompt_profiles` via `to_jsonb(p)` to reuse the existing n8n/automation prompt data without hard-coded C# prompts.

## Retry Rules

Image rerender uses deterministic dependency planning:

- Rerender 35 invalidates image 0 and videos `0->35`, `35->70`.
- Rerender 70 invalidates images 35 and 0, and all 3 videos.
- Replacing original 100 invalidates all generated images, all videos, and the final output.

Video rerender invalidates only the selected clip and final output; images and other clips remain valid.

Completed and failed generated images can be rerendered. The original 100% image cannot be rerendered as an AI stage.

## Provider Lifecycle

Phase 2B persists the provider lifecycle fields needed for restart-safe workers:

- provider code/model/task id
- prompt snapshot
- request/response JSON
- attempt/version
- status
- media id/object key/public URL
- error code/message
- timestamps

The customer UI never calls provider APIs directly and does not expose provider/model selection. Future workers should submit/poll provider tasks from the server and update these Timelapse child tables.

## UI

`/jobs/timelapse/{jobId}` now has 3 tabs:

- `YÊU CẦU`: polished request summary, original image, profile, scene count, quality, ratio, and pricing snapshot.
- `SCENE`: image cards first, then video clip cards.
- `KẾT QUẢ`: finalizer state and final video/download when available.

Image rendering cards use `Icons.Material.Filled.Image` and image-specific loading text. Video/finalizer cards use movie icons and video-specific loading text.

The page polls TodoX application state every 4 seconds while operations are active and stops polling when stable.

## Finalizer And Download

The finalizer can start only when all video clips are current and `COMPLETED`. The final output is versioned in `timelapse.timelapse_final_outputs`. The download link uses the stored TodoX media public URL from the final output row; arbitrary filesystem paths are not exposed.

## Billing Limitation

Phase 2B does not change customer sell-price totals and does not add generated-image customer charges. It records authoritative generated-image count for the next pricing phase:

- 3 scenes: 3 AI images.
- 4 scenes: 4 AI images.
- 5 scenes: 5 AI images.
- 6 scenes: 6 AI images.

Provider actual costs remain separate from the customer sell-price snapshot.

## Validation

- `dotnet build TodoX.Dashboard.sln -c Release`: passed, 0 warnings, 0 errors.
- `dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release`: passed, 380/380 tests.
- `git diff --check`: passed. Git reported line-ending normalization warnings only.
- `dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard`: passed with 45 existing CS8669 Razor generated-code warnings and 0 errors.
- Publish output: `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`.
