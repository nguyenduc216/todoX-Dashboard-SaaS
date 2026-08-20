# RVIDEO 79AI Video Hotfix Report

Date: 2026-08-20
Branch: `integration/rdance-on-construction-video-core`
Starting SHA: `ea35d00`

## Scope
- Keep RVIDEO video on 79AI only.
- Preserve YEScale behavior outside the RVIDEO path.
- Fix fallback/persisted task handling for attempt-specific reconciliation.
- Add regression coverage.

## What Changed
- `Services/VideoRender/SceneVideoWorkerHandler.cs`
  - RVIDEO fallback attempts carry the current `attemptLogicalRequestId` into billing reconciliation and usage logging.
  - Attempt-specific metadata persists the current logical request id.
  - RVIDEO still routes through `IRVideo79AiVideoService`.
- `Tests/RVideoVideoHotfixTests.cs`
  - Added regression coverage for attempt metadata and fallback indexing.
- `database/rvideo/verify_rvideo_runtime.sql`
  - Read-only verification now checks provider, capability, endpoint contract, credentials, and current catalog policy state.
- `database/rvideo/01_seed_rvideo_79ai_video_capability.sql`
  - Manual additive/idempotent seed for the existing provider `id=18`, `provider_code=79ai`.
  - Does not create a provider or modify image, Timelapse, or RDance capabilities.
- `TodoX.Web.csproj`
  - Excludes `artifacts/**` from item globbing so published output does not poison the next build.

## Verified Code Points
- `Program.cs` registers `IRVideo79AiVideoService` and the render handlers.
- `RVideo79AiVideoService` keeps the RVIDEO capability code at `rvideo_scene_video_generation`.
- `SceneVideoWorkerHandler` uses `attemptLogicalRequestId` for fallback reconciliation and usage logging.
- Terminal RVIDEO errors now call billing `CompleteAsync(Success=false)` before advancing to the next fallback model.
- `SceneVideoWorkerHandler` does not call `IYEScaleTaskClient` in the RVIDEO branch.
- Fallback attempts stop at the last entry in `RVideoVideoModelPolicy.Models`.

## Validation
- `dotnet build TodoX.Web.csproj -c Release --no-restore` ✅
- `dotnet test Tests\\TodoX.Web.Phase1B.Tests.csproj -c Release` ✅
- `dotnet publish TodoX.Web.csproj -c Release --no-restore -o artifacts\\publish\\todox-dashboard` ✅
- `dotnet format TodoX.Web.csproj --verify-no-changes` ❌ preexisting whitespace issues outside this hotfix scope

## Runtime Verification
- Added `database/rvideo/verify_rvideo_runtime.sql` as a read-only check for provider/capability presence, endpoint contract, credential mapping, and catalog policy state.
- No production SQL was applied.
- No migrations were created.

## Model Policy Status
- Current catalog evidence supports keeping RVIDEO on the existing Seedance-based policy.
- Catalog evidence confirms `veo_omni/flash`, `veo_3_1/fast`, and `veo_3_1/lite`.
- The requested VEO/Grok-only policy remains blocked because no catalog evidence proves `grok_video_heavy` supports `mode=normal` for this flow.
- The capability seed does not change the runtime model policy.

## Current Status
- Runtime code has already been committed and pushed.
- The manual capability seed SQL is prepared locally for review before execution.
- Live smoke test still pending.
- Not READY yet until submit -> persisted provider_task_id -> same-task poll -> mp4 save is proven in the live environment.
