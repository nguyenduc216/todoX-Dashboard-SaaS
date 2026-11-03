# Manual final recompose with history disabled

## Requested behavior and root cause

The user explicitly approved allowing the existing final-recompose button to work while `features.final_video_versioning` is disabled. This supersedes the earlier manual-recompose requirement to enable the global flag for this path; it does not authorize flag/database changes or production rollout.

Previously, `RVideoProjectFinalizationService.TryEnqueueFinalMergeAsync` rejected every manual request when the history readiness state was false. `VideoRenderMergeHandler` repeated that rejection and additionally threw when `IsEnabledAsync(FinalVideos)` was false. Thus the existing validated non-versioned worker branch was unreachable from the manual button. Regression tests before the production-code change reproduced three failures with history disabled: Completed recompose, concurrent-request deduplication, and retry after terminal failure (9 passed, 3 failed).

Execution path remains shared by Legacy and V2: RVideoRecomposeAction -> RVideoProjectFinalizationService -> existing guarded merge enqueue -> RenderJobWorker -> VideoRenderMergeHandler -> selected completed sources -> FinalMergeMedia -> optional existing music mix -> final validation -> existing project completion. No additional merge pipeline or public API was introduced.

## Changes

- `Services/VideoRender/FinalVideoVersioningState.cs`: internal policy allows manual merge for ready history or the existing explicitly disabled-history state. Missing/ambiguous settings, unready schema and unavailable database still block. Existing schema readiness checks and flag parsing remain unchanged.
- `Services/VideoRender/RVideoProjectFinalizationService.cs`: use this policy when checking manual requests. Owner/authentication, scene readiness, fresh request identity, concurrency key and zero-point merge-only enqueue remain intact.
- `Services/VideoRender/VideoRenderMergeHandler.cs`: resolve a checked history mode once for manual execution, avoiding a second flag read with potentially different state. When disabled, reuse the existing non-versioned branch: no final-version creation/completion/item writes; a new job-specific file and composition manifest are produced, validated, and only then published through the existing project URL/path update. When enabled, retain final-version creation and transactional selection. Automatic merge keeps its existing flag behavior.
- `TodoX.Web.Tests/RVideoManualRecomposeTests.cs`: exercise enabled/disabled Completed requests, concurrent requests and retries; disabled-mode owner/auth/readiness checks; configuration/schema/database blocks; worker state resolution and unchanged automatic mode.
- `docs/rvideo-recompose-without-history-report.md`: this report.

Failure handling continues to preserve the old final URL/path and files; a failed attempt may mark project status/error through the existing retry/failure path. No old video is deleted or overwritten. Existing per-job/per-version output directories, normalization for mixed timebases, decode/timeline/audio/motion checks and post-music validation are unchanged. A legitimate static source remains allowed; sampled motion detection is not frame-exact or lip-sync proof.

Frozen areas untouched: AI scene video rendering, image generation, provider contracts/model metadata, voice/TTS/audio generation or mux behavior, points/billing, RDance and Timelapse. No YEScale metadata tools were needed or called. No schema, migration, global flag or manual database update is required. Normal application publication still updates the project's final URL/path using the existing repository method. No production job was enqueued, no service restarted and no production package deployed during this task. Existing unrelated untracked files were preserved.

## Verification

Commands run from `TodoX.Web`. Logs are ignored repository-root `artifacts/rvideo-no-history-*.log`.

```powershell
# Before production-code changes: 9 PASS, 3 FAIL, reproducing the disabled-history blocker.
dotnet test ../TodoX.Web.Tests/TodoX.Web.Tests.csproj --no-restore --filter 'FullyQualifiedName~RVideoManualRecomposeTests' -v normal

$env:TODOX_TEST_FFMPEG=(Get-ChildItem ../artifacts/tools -Recurse -Filter ffmpeg.exe | Select-Object -First 1).FullName
dotnet test ../TodoX.Web.Tests/TodoX.Web.Tests.csproj --no-restore --filter 'FullyQualifiedName~RVideoFinal|FullyQualifiedName~RVideoManualRecompose|FullyQualifiedName~RVideoRecompose|FullyQualifiedName~RVideoMerge' -v normal
dotnet format TodoX.Web.csproj whitespace --verify-no-changes --no-restore --include Services/VideoRender/FinalVideoVersioningState.cs Services/VideoRender/RVideoProjectFinalizationService.cs Services/VideoRender/VideoRenderMergeHandler.cs
dotnet format ../TodoX.Web.Tests/TodoX.Web.Tests.csproj whitespace --verify-no-changes --no-restore --include ../TodoX.Web.Tests/RVideoManualRecomposeTests.cs
git diff --check
dotnet build TodoX.Web.csproj -c Release --no-restore -v quiet
dotnet publish TodoX.Web.csproj -c Release -o ../artifacts/publish/todox-dashboard --no-restore -v quiet
Test-Path ../artifacts/publish/todox-dashboard/TodoX.Web.dll
```

Final test result: 55 PASS, 0 failed, 1 skipped (56 total), exit 0. Includes executable FFmpeg media fixtures, request tests, worker-mode tests, UI/source publication contracts and compatibility tests. The PostgreSQL staging test remains skipped because no approved staging runner is available; no live DB transaction/worker-publication or browser/IIS acceptance is claimed. Request concurrency uses the existing atomic fake; persistence publication/order/failure preservation is additionally checked by source contracts. The tracked Phase1B project was not rerun because its unrelated CS7036 constructor error was already recorded in the preceding freeze report; tests ran through the existing local test project. Full unrelated suites were not rerun.

Both format checks and diff check: exit 0. Release build: exit 0, 0 errors, 46 existing warnings. Folder publish: exit 0, `TodoX.Web.dll` verified present. Publish directory is `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`; artifacts/media/logs are excluded from Git.

## Delivery and operational boundary

This change is on `fix/rvideo-recompose-without-history-20261028`, based on the previous partial-freeze repair branch. It requires deployment of this package to the actual web application and merge worker; pushing source or publishing a folder does not update the remote worker. Keep the flag unchanged. After the established authorized deployment process, use the existing button once and verify a new final URL, PROJECT_MERGE_VALIDATED, playback and preservation of the old final on failure. Missing/invalid schema or unavailable settings remain actionable blocks rather than permission to bypass an unknown state.
