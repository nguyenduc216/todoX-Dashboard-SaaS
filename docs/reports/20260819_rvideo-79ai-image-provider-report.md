# RVIDEO 79AI Scene Image Pipeline Report

Date: 2026-08-19
Branch: `integration/rdance-on-construction-video-core`

## Git

- Starting SHA: `ed65c747afcfcbf99dfc6dcf1777f7f094643ce9`
- Final implementation SHA: `3d0a4f319028d9dff27f443a951d8173f41efc34`
- Report commit SHA: `e8c224f763ec759dad0be48f9866e315dee6754a`
- No migration or SQL was executed.

## IMAGE PROVIDER

- Capability: `rvideo_scene_image_generation`
- Factory: `79ai_task_image` only.
- Non-79AI resolution fails with `RVIDEO_IMAGE_PROVIDER_MUST_BE_79AI`.
- Endpoints: `POST /generateImage`, `POST /image`, exact `id_base` recovery through `POST /images`.
- Ratio normalization: `9:16 -> 9_16`, `16:9 -> 16_9`.

## MODEL POLICY

The adapter now executes one requested model/task per invocation and no longer loops through models internally.

- Primary: `google_image_gen_banana_2` / `vip` / `1k`
- Fallback 1: `imagegen_2_0` / `low_basic` / `1k`
- Fallback 2: `seedream_4_5` / `vip` / `2k`

Terminal provider failure now closes the current version and queues a new version with the next 79AI model.

## PERSISTED POLLING

- Submit once: YES
- Provider task persisted through router/outcome: YES
- Same task reused on later worker pass: YES
- Typed provider state (`Pending`, `Success`, `Failed`): YES
- Process restart safe by persisted task ID: YES
- Pending does not fail, clear task, or charge again: YES
- Transient poll retains task and requeues: YES
- `RequestedModel` is now the model submitted to 79AI, reserved in billing, and retained in usage metadata: YES

## REFERENCE

- JPEG/PNG/WebP data URL handling: YES
- `editImage=true` only with valid reference bytes: YES
- Missing reference fails before provider submit with `RVIDEO_REFERENCE_IMAGE_UNAVAILABLE`: YES
- Base64 is not logged.
- The shared resolver now has an explicit strict RVIDEO mode; legacy callers retain their nullable behavior.

## VERSION HISTORY

- Stale completion protection: YES
- Separate fallback image version: YES
- Active image version guard prevents the bulk queue action from creating another top-level image attempt while a version is queued, submitted, pending, processing, or pending reconciliation: YES
- Mandatory end-to-end worker/router/provider integration tests: NOT COMPLETED

## BILLING, MEDIA, UI

- Poll does not recharge: YES
- Pending usage status: `pending`
- Customer image charge: deferred/zero by existing Phase 1 policy
- Provider URL copied into TodoX media on success: YES
- AUTO video transition: **DISABLED / NOT IMPLEMENTED IN THIS TASK**

## TESTS

- Focused 79AI provider tests: `5 passed, 0 failed`
- Phase1B: `45 passed, 0 failed`
- Full `TodoX.Web.Tests`: `670 passed, 0 failed`
- Build and `git diff --check`: passed.
- Publish: passed.

## 2026-08-19 FOLLOW-UP: RVIDEO BULK/PER-SCENE/UI

Starting SHA for this follow-up: `51a0fbc027311a131e0f81dba412dd1907c86fba`

Final remote SHA for this follow-up: `PENDING_COMMIT`

Implemented:

- `SceneImageBatchInput.CapabilityCode` now defaults to `rvideo_scene_image_generation`.
- `/render-job` bulk image enqueue explicitly sends `SceneImageRenderContext.RVideoCapabilityCode`.
- Per-scene image rerender no longer calls `RerenderSceneImageWithOpenRouterAsync` directly.
- Per-scene image rerender now saves dirty scene draft, blocks duplicate active image versions, requires a reference media id, creates a queued image version, and enqueues a `render_scene_image` work item with `RequestedModel` and `ModelAttemptIndex`.
- Per-scene image rerender now routes through the same persisted 79AI polling worker path as batch scene image rendering.
- `RenderVideoJobs.razor.css` now defines local flash/shimmer keyframes directly and uses them for queued/submitted/processing scene image states.

Regression tests added:

- `SceneImageBatchInput_DefaultsToRVideoCapability`
- `PerSceneImageRerender_EnqueuesPersisted79AiWorkItem`
- `SceneImageRenderStates_DefineLocalFlashAndShimmerKeyframes`

Follow-up validation:

- `dotnet test ..\TodoX.Web.Tests\TodoX.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~RenderVideoJobsLayoutTests|FullyQualifiedName~SceneImageBatchRenderHandlerTests" --logger "console;verbosity=minimal"`: `26 passed, 0 failed`
- `dotnet format ..\TodoX.Dashboard.sln --verify-no-changes --no-restore --include TodoX.Web\Components\Pages\RenderVideoJobs.razor TodoX.Web\Components\Pages\RenderVideoJobs.razor.css TodoX.Web\Services\Render\SceneImageBatchRenderHandler.cs TodoX.Web.Tests\RenderVideoJobsLayoutTests.cs TodoX.Web.Tests\SceneImageBatchRenderHandlerTests.cs`: passed
- `dotnet build TodoX.Web.csproj --no-restore -p:UseSharedCompilation=false`: passed, `0 warning, 0 error`
- `git diff --check`: passed
- `dotnet test ..\TodoX.Web.Tests\TodoX.Web.Tests.csproj --no-restore --logger "console;verbosity=minimal"`: `670 passed, 0 failed`
- `dotnet test Tests\TodoX.Web.Phase1B.Tests.csproj --no-restore --logger "console;verbosity=minimal"`: `45 passed, 0 failed`
- `dotnet publish TodoX.Web.csproj --no-restore -c Release -o ..\artifacts\publish\todox-dashboard -p:UseSharedCompilation=false`: passed; output `artifacts\publish\todox-dashboard`

Publish warnings:

- Existing/generated Razor nullable warnings remain in `obj\Release\net10.0\...\Components\Pages\AiProviders_razor.g.cs`.
- Existing nullable warnings remain at `TodoX.Web\Components\Pages\RenderVideoJobs.razor` lines 2112 and 2129 during Release publish.

Database:

- No migration created.
- No migration executed.
- No SQL executed.

## DATABASE

- Migration required: only if deployment lacks the existing additive seed.
- Migration path: `database/migrations/20260819_rvideo_79ai_image_provider.sql`
- SQL executed: NO
- Support SQL: `docs/support/rvideo-job-diagnostic.sql`

## PUBLISH

- Command: `dotnet publish TodoX.Web\TodoX.Web.csproj --no-restore -c Release -o artifacts\publish\todox-dashboard`
- Output: `artifacts/publish\todox-dashboard`
- Result: successful.

## FINAL IMAGE READINESS

`RVIDEO_79AI_IMAGE_NOT_READY`

Exact blockers:

1. The mandatory full integration harness that executes `SceneImageRenderWorkItemHandler -> SceneImageRenderService -> AiImageRenderRouter -> fake 79AI provider/client` is still not present.
2. Therefore the required cross-layer fallback/restart/duplicate-submit/stale-success/reference/media/billing scenarios are not proven end to end, despite the focused provider and policy regressions passing.

No video implementation was started.
