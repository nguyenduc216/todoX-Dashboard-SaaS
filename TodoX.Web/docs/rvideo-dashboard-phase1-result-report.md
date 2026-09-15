# RVIDEO Phase 1 Final Hotfix Report

## 1. Git

- Starting SHA: `c9adbf6000d4d286b3219e6908303ebffbe0cf73`
- Final SHA: `aff653bd8bb4c32fa9bff83c153b0f54b3b64276`
- Commit: `fix(rvideo): prevent terminal image retry loops`

## 2. Root Cause

`VideoSceneStatuses.Failed` represented both terminal static-image failures and terminal scene-video failures. The AUTO lifecycle treated every failed scene as image work and could enqueue a new top-level image batch after the previous failed batch had finished. `EnqueueForProjectIfNoneActiveAsync` only prevents concurrent active jobs, so it did not prevent those sequential retry loops.

## 3. New Lifecycle Classification

The lifecycle now derives stage-aware scene state from persisted scene media URLs/paths, scene status, active `render.render_jobs`, and the latest scene-specific project failure event:

- `SCENE_IMAGE_RENDER_FAILED` -> `IMAGE_FAILED`
- `SCENE_VIDEO_RENDER_FAILED` -> `VIDEO_FAILED`

Legacy failed records fall back to available persisted media evidence. No schema change is required.

## 4. Image Retry Behavior

- Terminal image failures no longer auto-retry.
- AUTO image batches target only explicitly classified pending scene IDs.
- Existing user-triggered bulk and per-scene retry paths remain direct commands and retain persisted active-job idempotency.
- A terminal image failure blocks AUTO video/finalization until a user retry succeeds.

## 5. Video Partial Failure

- `VIDEO_READY + VIDEO_FAILED` finalizes the successful clips.
- All `VIDEO_FAILED` scenes are terminal without a merge.
- `IMAGE_FAILED` is not classified as `VIDEO_FAILED` and cannot trigger a partial finalize.

## 6. Merge Duration

Final-video duration now sums only `VIDEO_READY` scenes. `PROJECT_MERGED` includes `mergedSceneCount`, `failedSceneCount`, `mergedSceneIndexes`, and `finalDurationSeconds`.

## 7. Database

- NO DATABASE MIGRATION CHANGE.
- The existing project events, scene fields, and render job records provide the persisted state required for the fix.

## 8. Tests

- `dotnet test Tests\TodoX.Web.Phase1B.Tests.csproj --no-restore --logger "console;verbosity=minimal"`
- Passed: 40, Failed: 0.
- Added regression coverage for terminal image failure, explicit retry eligibility, event-based failure classification, partial video merge decisions, mixed states, manual mode, and partial merge duration.

## 9. Build and Publish

- `dotnet build TodoX.Web.csproj --no-restore`: succeeded, 0 warnings, 0 errors.
- `git diff --check`: passed.
- `dotnet publish TodoX.Web.csproj --no-restore -c Release -o ..\artifacts\publish\todox-dashboard`: succeeded.
- Publish output: `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`.

## 10. Compatibility

No intended behavioral change was made to RDance, Construction Timelapse, Telegram RVIDEO, n8n, Voice/Music catalog administration, provider pricing, billing, or 79AI model mapping.
