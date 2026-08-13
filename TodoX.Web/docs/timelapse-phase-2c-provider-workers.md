# Timelapse Phase 2C - Production Provider Workers

## Scope

Phase 2C adds production workers for the existing Phase 2B Timelapse workflow. It does not redesign the Phase 2B schema, state graph, retry invalidation, dependency rules, UI structure, pricing, n8n, RVideo, RDance, or 79AI catalog sync.

## Image Provider Path

`TimelapseImageWorker` claims one `RENDERING` image stage attempt and calls `TimelapseProviderRuntime`. The runtime resolves the configured TodoX provider for `scene_image_generation` and requires `provider_code=79ai`. Credentials are resolved with `ProviderCredentialResolver.ResolveAsync("79ai", "access_token")`; secrets are never stored in request/response JSON or logs.

The image prompt is resolved server-side from the persisted Phase 2B profile snapshot (`prompt_snapshot_json`) plus the stage progress. The dependency image comes from the completed later stage, such as 100 -> 70 -> 35 -> 0.

## Video Provider Path

`TimelapseVideoWorker` claims one `RENDERING` clip attempt and resolves the configured TodoX provider for `image_to_video`, also requiring 79AI. It submits the completed start and end image URLs for the clip, duration 6 seconds, runtime mode, and ratio.

This preserves the configured 79AI model/capability behavior instead of switching to another provider.

## Submit/Poll Contract

Workers are asynchronous:

- If `provider_task_id` is empty, submit to 79AI and persist provider, model, task id, sanitized request JSON, and sanitized response JSON.
- If `provider_task_id` exists, poll the existing 79AI task.
- `RUNNING` remains `RENDERING`.
- `SUCCESS` is not marked complete until output media has been downloaded and saved to TodoX media storage.
- `FAILED` marks the current attempt failed and blocks downstream work.

79AI status values are normalized into `RUNNING`, `SUCCESS`, and `FAILED`.

## Worker Claiming

Claiming is restart-safe and duplicate-resistant. The repository selects eligible rows using `FOR UPDATE SKIP LOCKED` and stores a short `worker_claim` object in the active version `request_json`. If a process dies, the claim expires and another worker resumes the same attempt. Existing `provider_task_id` rows are polled, not resubmitted.

## Restart Behavior

Application restart preserves:

- image stages in `RENDERING` with `provider_task_id`
- video clips in `RENDERING` with `provider_task_id`
- finalizer rows in `RENDERING`

Workers resume by polling existing provider tasks or continuing finalization after the claim timeout.

## Media Persistence

Provider temporary output URLs are never used as final customer media. Successful image outputs are saved through `DownloadAndSaveImageAtObjectKeyAsync`; successful video clips are saved through `DownloadAndSaveBinaryAtObjectKeyAsync`; final MP4 output is saved through `SaveBinaryAtObjectKeyAsync`.

## State Advancement

Image completion calls `AdvanceAfterImageCompletedAsync`:

- completed 70 enables 35
- completed 35 enables 0
- all images completed moves parent through `IMAGES_READY` to `GENERATING_VIDEOS`

Video completion calls `AdvanceAfterVideoCompletedAsync`:

- all current clips completed moves parent to `VIDEOS_READY`

Failures mark the failed child and the parent as `FAILED` when no active child remains. Retry behavior keeps the Phase 2B invalidation model and creates a new attempt with a new provider task.

## Finalizer

`TimelapseFinalizerWorker` claims `timelapse_final_outputs.status='RENDERING'`, loads completed current clips ordered by `clip_index`, resolves local TodoX media files, and runs FFmpeg concat demuxer with stream copy:

`ffmpeg -f concat -safe 0 -i concat.txt -c copy final.mp4`

The final MP4 is stored in TodoX media and the parent job is marked `COMPLETED`. On FFmpeg/media failure, the final output and parent are marked `FAILED`; individual clips are preserved.

## Test Results

Validation commands for this phase:

- `dotnet build TodoX.Dashboard.sln -c Release`: passed.
- `dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release`: passed, 390 tests.
- `dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard`: passed.
- `git diff --check`: passed.
- `dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore`: failed on pre-existing whitespace issues in unrelated files (`AccountRepository.cs`, `AuditRepository.cs`, `ChibiAvatarService.Generate.cs`, settings repositories, `SocialPageRepository.cs`, `WalletService.cs`). Those files were not modified in this phase.

Final command output is recorded in the task completion report.
