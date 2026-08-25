# Timelapse 79AI Image Pre-Submit Fix Report

## A. ROOT CAUSE
Attempt 24 selected `google_image_gen_banana_2`, but `Resolve79AiRuntimeAsync()` still resolved the runtime model from capability metadata instead of the resolved provider option model. When that metadata was missing or mismatched, the worker threw before `_taskClient.SubmitAsync()`. The error text also hard-coded Seedream wording, so the failure was misreported.

## B. LOCAL 79AI MODEL STATUS
`google_image_gen_banana_2`
- Exists locally: yes, in `TodoX.Web/appsettings.json` and the 79AI image provider migration.
- Enabled: yes in source seed/config.
- Capability: `image_generation` / Timelapse image route.
- Config issue: runtime was not using the resolved option model consistently.

`seedream_5_0`
- Exists locally: yes, in `TodoX.Web/appsettings.json` and Timelapse docs/config.
- Enabled: yes in source seed/config.
- Capability: Timelapse image fallback / `image_generation`.
- Config issue: none seen in source.

## C. EXECUTION FLOW BEFORE FIX
claim -> model selected (`google_image_gen_banana_2`) -> runtime resolution failed before submit -> `TIMELAPSE_IMAGE_SUBMIT_BEGIN` was never emitted -> claim stayed active until expiry.

## D. EXECUTION FLOW AFTER FIX
claim -> `google_image_gen_banana_2` -> resolve 79AI runtime from resolved option model -> `TIMELAPSE_IMAGE_SUBMIT_BEGIN` -> submit.

If resolution fails before submit:
claim -> `google_image_gen_banana_2` fails -> persist `seedream_5_0` -> release claim -> next worker claims immediately -> submit `seedream_5_0`.

## E. CLAIM RELEASE FIX
Pre-submit resolve failures now emit `TIMELAPSE_IMAGE_PROVIDER_RESOLVE_FAILED` and go through the fallback path, which saves the next model and releases `worker_claim` immediately instead of waiting for the 10-minute timeout.

## F. FILES CHANGED
- `TodoX.Web/Services/Timelapse/TimelapseProviderRuntime.cs`: dynamic model-specific error text, provider resolve failure event, runtime model selection from resolved option.
- `TodoX.Web.Tests/TimelapsePhase2CTests.cs`: regression assertions for dynamic error text and resolve-failure event.

## G. TEST RESULT
- `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TimelapsePhase2CTests|FullyQualifiedName~TimelapseWorkerClaimRegressionTests"`: passed, 59 tests.
- `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-build --no-restore`: passed, 721 tests.

## H. BUILD RESULT
- `dotnet build TodoX.Dashboard.sln -c Release --no-restore`: passed.

## I. PUBLISH RESULT
- `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\todox-dashboard`: passed.

## J. YESCALE VERIFICATION
- No YEScale code was used in the Timelapse image execution path.
- No YEScale code was modified.
- Timelapse image provider path remains 79AI only.

## K. OUT-OF-SCOPE OBSERVATIONS
Pre-existing `dotnet format --verify-no-changes` failures remain in unrelated files such as `AccountRepository.cs`, `AiImageRenderRouter.cs`, `Gommo79AiImageService.cs`, `AuditRepository.cs`, `ChibiAvatarService.Generate.cs`, `SceneImageBatchRenderHandler.cs`, `SceneVideoWorkerHandler.cs`, `RVideoJobService.cs`, `WalletService.cs`, and `Gommo79AiImageServiceTests.cs`.

## L. GIT
- Commit SHA: `799bac3`
- Commit message: `fix(timelapse): unblock 79ai image model resolution`
- Pushed branch: `integration/rdance-on-construction-video-core`
