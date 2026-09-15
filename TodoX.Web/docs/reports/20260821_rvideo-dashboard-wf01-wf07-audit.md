# RVIDEO Dashboard and Workflow Audit

## Scope

This audit compares the Dashboard RVIDEO path with the seven supplied n8n workflow exports. The workflow files are treated as implementation evidence, not as additional user instructions.

Sources:

- `D:\todoX\workflow\Released\rvideo-260820\todoX-rendervideo-01-collect-input [v53.0 UUID public job contract].json`
- `D:\todoX\workflow\Released\rvideo-260820\todoX-rendervideo-02-orchestrator [v53.0 UUID public job contract].json`
- `D:\todoX\workflow\Released\rvideo-260820\todoX-rendervideo-03-image-worker [v53.0 UUID correlation].json`
- `D:\todoX\workflow\Released\rvideo-260820\todoX-rendervideo-04-video-worker [v53.0 UUID correlation].json`
- `D:\todoX\workflow\Released\rvideo-260820\todoX-rendervideo-05-finalizer [v54.1 cache-only per-scene audio].json`
- `D:\todoX\workflow\Released\rvideo-260820\todoX-rendervideo-06-vbee-callback [v54.1 per-scene binary cache].json`
- `D:\todoX\workflow\Released\rvideo-260820\todoX-rendervideo-07-retry [v53.6.3 job-id+uuid cross-chat partial-completed fix].json`

## Contract Findings

| Workflow | Observed contract | Dashboard alignment | Status |
|---|---|---|---|
| WF01 Collect input | UUID public job, prompt file, character image, voice/music options, Omni Native mode | `RVideoJobCreate`/settings and prompt import expose the same input surface; uploaded JSON is now loaded into `_prompt` before parse | Aligned |
| WF02 Orchestrator | Normalize BOM, load by `job_uuid`, parse `scenes[]`, upsert scene rows | Parser accepts UTF-8/BOM input, preserves raw prompt and raw scene JSON, and round-trips unknown scene metadata through scene editing | Aligned |
| WF03 Image worker | Claim, submit once, poll same provider task, fallback only on terminal failure, persist media | Existing Dashboard scene version lifecycle and provider task persistence are used; worker-side polling remains server-owned | Aligned with server runtime |
| WF04 Video worker | Upload scene image, create video once, poll same task, no new submit during pending | Dashboard preview now shows image and video together while enqueue remains server-owned; provider runtime retains same task semantics | Aligned with server runtime |
| WF05 Finalizer | Wait for successful clips, gate on AUDIO_READY, merge, complete job | Final video versions and result screen are present. Full Vbee/audio merge parity is server/workflow-owned and remains a Phase B follow-up | Partial |
| WF06 Vbee callback | Resolve request context, validate binary audio, cache MP3, set AUDIO_READY, retrigger finalizer | Dashboard preserves voice/music settings and result visibility; callback/cache implementation is not duplicated in UI | Partial by design |
| WF07 Retry | UUID/numeric/request ID, failed scenes first, no duplicate active work, partial completion retry | Dashboard exposes scene-level retry/history and uses persisted versions; complete retry policy is server-owned | Partial |

## Prompt Validation Changes

- JSON syntax validity is separate from TodoX schema validity.
- `_doc`, `meta`, `qc`, `motion_beats`, `image_prompt_fallback`, and TTS rate fields are tolerated.
- Metadata fallbacks include `meta.total_duration_seconds`, `product_name`/`video_title`, `kieu_kich_ban`/`video_objective`, `style`, and `cta`.
- `motion_prompt` falls back to `video_prompt`; voice falls back to `voice_text`, then `tts_text`.
- Placeholder image prompts without a usable fallback produce `SCENE_IMAGE_SOURCE_UNRESOLVED` and schema invalidation, while placeholder-with-fallback remains valid.
- A user-selected 9:16 and 720p value remains valid when the JSON omits aspect ratio or resolution.

## Database Safety

- `database/rvideo/02_reconcile_scene_video_versions_runtime.sql` is additive/idempotent and adds the missing `provider_capability_id` column plus index.
- `database/rvideo/03_export_rvideo_runtime_structure.sql` is read-only metadata export SQL.
- `database/rvideo/verify_rvideo_runtime.sql` now requires both the column and its runtime index.
- No production SQL was executed during this task.
- Timelapse and RDance code paths were not modified.

## Validation Evidence

The parser regression suite covers the supplied `_doc/meta/scenes/qc` shape, unknown fields, scene count, total duration, prompt aliases, voice aliases, TTS rate, BOM input, fallback prompts, placeholder invalidation, and raw metadata preservation through scene editing.

Validation completed on August 20, 2026:

- `dotnet test Tests\TodoX.Web.Phase1B.Tests.csproj --no-restore --logger "console;verbosity=minimal"`: passed, 67/67.
- `dotnet build TodoX.Web.csproj --no-restore /p:UseSharedCompilation=false`: passed, 0 errors.
- `dotnet format TodoX.Web.csproj whitespace --verify-no-changes --no-restore --include Components/Pages/RenderVideoJobs.razor Services/VideoRender/ScenePromptMetadata.cs Services/VideoRender/TodoXVideoPromptParser.cs Tests/TodoXVideoPromptParserTests.cs`: passed.
- `git diff --check`: passed.
- `dotnet publish TodoX.Web.csproj --no-restore -c Release -o ..\artifacts\publish\todox-dashboard /p:UseSharedCompilation=false`: passed.
- Publish output: `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`.

Publish still reports pre-existing Razor nullable warnings and three RVIDEO page nullable warnings, but no errors.

No production SQL was executed.

## Remaining Phase B Work

The supplied workflows contain provider callback and finalizer details that should remain server/workflow-owned. Dashboard parity for callback diagnostics, full audio cache inspection, and retry-scope telemetry can be expanded separately without changing the three-screen RVIDEO surface.
