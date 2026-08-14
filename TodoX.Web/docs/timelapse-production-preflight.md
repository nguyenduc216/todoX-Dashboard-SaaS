# Timelapse Production Preflight

Date: 2026-08-14

Scope: 79AI and Timelapse Phase 2C only. Phase 2B stage graph, retry rules, pricing, n8n, RVideo, RDance, and provider credential architecture were not redesigned.

## Effective Worker Configuration

The production-shaped `TodoX.Web/appsettings.json` explicitly enables both gates required by the workers:

| Setting | Value |
|---|---:|
| `RenderQueue:Enabled` | `true` |
| `TimelapseProviderWorkers:Enabled` | `true` |
| `ImageParallelism` | `1` |
| `VideoParallelism` | `3` |
| `FinalizerParallelism` | `1` |
| `PollDelayMs` | `1500` |
| `IdleDelayMs` | `1500` |
| `ClaimMinutes` | `10` |

No secret or access token is included in this report.

## Explicit Timelapse Routing

Timelapse does not use global provider defaults or priority fallback.

| Operation | Provider | Capability | Model |
|---|---|---|---|
| Image generation | `79ai` | `image_generation` | `seedream_5_0` |
| Image-to-video | `79ai` | `image_to_video` | `seedance_20_pro` |

The image route does not use the TodoX `scene_image_generation` abstraction, ImageAICreativeRender, YEScale, or the maintenance Nano Banana model. The video route does not fall back to YEScale, Grok, Veo, or Omni.

The 79AI catalog sync writes provider models and model capabilities to the catalog model tables. It does not automatically create the legacy runtime route row in `public.todox_ai_provider_capability`; the standalone idempotent script `database/manual/ai-provider-catalog/05_seed_79ai_seedance_timelapse_capability.sql` supplies the missing Seedance route without changing credentials, provider pricing, customer sell pricing, or global defaults.

## Verified 79AI Contract

The Timelapse adapter uses the verified 79AI base URL and paths:

| Operation | Endpoint |
|---|---|
| Image submit | `POST https://api.gommo.net/ai/generateImage` |
| Image poll | `POST https://api.gommo.net/ai/image` |
| Video submit | `POST https://api.gommo.net/ai/create-video` |
| Video poll | `POST https://api.gommo.net/ai/video` |

The access token is resolved at runtime through `IProviderCredentialResolver` with provider `79ai` and role `access_token`. It is sent only as form data and is sanitized from persisted response JSON and logs.

Image submit fields are `access_token`, `domain`, `model`, `prompt`, `image`, and configured options such as `ratio`.

Video submit fields are `access_token`, `domain`, `model`, `prompt`, `mode`, `duration`, `ratio`, `image` (start image), and `image_2` (end image). The configured Seedance model is `seedance_20_pro`. Customer standard maps to `fast`, premium maps to `professional`, and clip duration remains 6 seconds.

The runtime rejects configured submit/poll paths that do not match the verified image or video contract.

## Sanitized Fixtures

Regression fixtures cover:

- nested image submit task id under `data.task_id`
- nested video submit request id under `data.request_id`
- running responses under `data.status` and `task.state`
- successful image/video responses with `image_url` and `video_url`
- failed responses with nested error code/message
- submit responses containing only an unrelated `id`, which are rejected

Tests assert that secrets do not appear in sanitized response JSON.

## Media Storage

Current production-shaped configuration uses local storage:

- provider defaults to `local`
- object keys resolve below `Storage:LocalUploadRoot`
- successful provider output is first persisted by `IMediaFileService`
- finalizer reads local files only after validating the storage provider

If `Storage:Provider` or a stored media row uses a non-local provider, the finalizer fails safely with a clear server-side error instead of passing a non-existent physical path to FFmpeg. A remote/object-storage download adapter is not present in this repository.

## FFmpeg

Configured path: `VideoRender:FfmpegPath = ffmpeg`.

Preflight result in the current development environment: `ERROR / BLOCKED`; `ffmpeg -version` could not run because `ffmpeg` is not available on PATH. Run the same command from the production deployment context before customer release.

## Live Smoke Test and End-to-End Result

No live 79AI submission or production database verification was executed in this coding environment. The required live smoke test needs the production secure credential mapping, two saved test images, FFmpeg, and the target database. Running it here would require exposing or bypassing the application credential resolver.

Before customer release, run one `CONSTRUCTION_VIDEO` job with three scenes and verify the image dependency order `100 -> 70 -> 35 -> 0`, then clips `0->35`, `35->70`, and `70->100`. Verify the current versions in:

- `timelapse.timelapse_image_stages`
- `timelapse.timelapse_image_stage_versions`
- `timelapse.timelapse_video_clips`
- `timelapse.timelapse_video_clip_versions`
- `timelapse.timelapse_final_outputs`
- `render.render_jobs`

The Seedance capability script must be reviewed and run manually in each target environment before enabling production Timelapse workers. No production database was modified by this code task.

## Validation

- `dotnet build TodoX.Dashboard.sln -c Release`: passed.
- `dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release`: passed, 413 tests.
- `dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard`: passed.
- `git diff --check`: passed.
- `dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore`: failed on pre-existing whitespace findings in unrelated files; no unrelated files were changed.
