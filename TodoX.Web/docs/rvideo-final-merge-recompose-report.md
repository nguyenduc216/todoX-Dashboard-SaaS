# RVideo final merge and manual recompose — DEV report

Production job: `464639f5-16ee-4953-80c1-2050fcadb3fb`. Production was not accessed or modified. Its project, selected versions, media and events must be established by the operator evidence procedure below. A production-specific root cause is **not yet confirmed**.

## Evidence and diagnosis

The previous handler accepted any copy-concat result with exit code 0; its fallback transcoded the same heterogeneous concat demuxer input without normalizing individual files. It never checked decoded frame coverage or video timestamps. Manual finalization returned `already_final` when the final matched selected versions, even for an explicit manual request.

Local executable regression `ReproducesMixedTimebaseCopySuccessWithoutValidPictureTimeline` creates two 2-second H.264/AAC clips at 25 FPS, using track timescales 12800 and 90000. The original copy command returns 0 while the final picture timeline fails validation. The repaired pipeline normalizes the inputs and produces a decoded 4-second picture timeline. This demonstrates a concrete code defect and plausible mechanism; it does not prove which production inputs caused the reported symptom. Another regression supplies a stale concat manifest: copy returns 0 with truncated video, validation rejects it and fallback reconstructs the correct input sequence.

Execution path: Final tab -> `RVideoProjectFinalizationService` -> existing `EnqueueForLogCodeIfNoneActiveAsync` (transaction advisory lock, active-job lookup, zero-cost merge job) -> render worker -> `VideoRenderMergeHandler` -> selected version/local media resolution -> FFmpeg -> validation -> existing transactional final version completion/selection -> project/lifecycle events.

## Changes

- `Services/VideoRender/FinalMergeMedia.cs`: inspect codec, dimensions, pixel format, FPS, timebase, codec extradata and audio compatibility; copy only matching H.264/yuv420p streams; reject missing video, invalid duration, missing/nonmonotonic/gapped frame timestamps or decode errors. Normalize each input separately when incompatible or copy output is rejected, reset PTS, normalize dimensions/FPS/timebase and audio sample layout, pad/trim to configured scene duration, then concatenate/transcode. A scene without audio gets silence only when other scenes contain audio. All-silent inputs stay silent. Use the first selected scene's dimensions/FPS as normalization target. Kill child processes on cancellation/timeout.
- `Services/VideoRender/VideoRenderMergeHandler.cs`: validate again after existing background music mixing and before publication; retain selected completed scenes regardless of stale scene status; verify resolved input IDs match persisted composition items; isolate output per version/job and attempts; include input resolution failures in the retry/failure path; correct apostrophe escaping in concat paths. Existing final version is untouched until successful validation and the existing transactional selection method.
- `Services/VideoRender/RVideoProjectFinalizationService.cs`: explicit manual requests bypass `already_final`, require authenticated project owner/readiness and existing final versioning flag, retain the stable concurrency key but use a fresh logical request identity. Internal delegate boundary allows execution tests without a database. Automatic requests retain the existing already-final guard.
- `Components/Pages/RVideo/RVideoV2Result.razor`: retain the pre-existing in-progress remerge button/handler, expose merge errors and actionable rejection messages. Do not disable retries merely because project status is Completed or stale Merging.
- `Components/Pages/RVideo/RVideoV2Shell.razor`: retain the pre-existing result reload callback wiring. These two scoped UI changes existed partially before this task; they are included as required UI prerequisites. Other pre-existing edits remain unstaged.
- `TodoX.Web.Tests/RVideoFinalMergeMediaTests.cs`: executable local FFmpeg fixtures and timeline tests.
- `TodoX.Web.Tests/RVideoManualRecomposeTests.cs`: execute the real request workflow with strict fake ports; Completed recompose, concurrent callers, fresh identity after failure, ownership/auth/readiness/versioning and zero-cost merge-only enqueue.
- `TodoX.Web.Tests/RVideoFinalPublicationContractTests.cs`: source contracts for validation-before-publication, transactional final selection, previous URL/path retention on failure, post-publication error isolation and advisory-lock-before-active-lookup.
- `scripts/diagnostics/rvideo-final-evidence.sql`: operator-only read-only query for job/project/scenes/versions/composition/render jobs/events.
- `scripts/diagnostics/Collect-RVideoFinalEvidence.ps1`: read source media, copy merge artifacts and collect stream metadata, packet PTS/DTS, frame timestamps, strict decode results, freeze intervals, SHA256 and decoded frame hashes into a NEW evidence directory.
- `Tests/TodoX.Web.Phase1B.Tests.csproj`: link the three new test files into the tracked test project so the tests run without the ignored local `TodoX.Web.Tests.csproj`.
- This report.

Frozen areas untouched by this task: image generation, scene video rendering, provider payloads/IDs/pricing, voice generation, provider usage accounting and points/billing. No YEScale changes or calls. No scene rerenders, media/version deletions, production writes, migrations, schema changes or IIS deployment. **No database update is required**; manual recompose requires the existing final-video-versioning feature to already be enabled. Normalized intermediate files are retained alongside attempt logs for diagnosis and consume additional disk.

## Validation on the current DEV workspace

Commands were run from `TodoX.Web` unless stated otherwise. Logs are in repository-root `artifacts` and are not committed.

1. Baseline relevant tests before edits: `dotnet test ../TodoX.Web.Tests/TodoX.Web.Tests.csproj --filter 'FullyQualifiedName~RVideoMerge|FullyQualifiedName~RVideoV2SceneRerenderHistoryTests' --no-restore -v quiet` — 36 PASS.
2. Set `TODOX_TEST_FFMPEG` to the downloaded local FFmpeg executable (ffprobe alongside it). FFmpeg 9.0.2 essentials was used; binaries are under ignored `artifacts/tools`, not application configuration or Git.
3. `dotnet test ../TodoX.Web.Tests/TodoX.Web.Tests.csproj --filter 'FullyQualifiedName~RVideoFinal|FullyQualifiedName~RVideoManualRecompose|FullyQualifiedName~RVideoMerge|FullyQualifiedName~RVideoV2SceneRerenderHistoryTests' --no-restore -v quiet` — 54 PASS, 0 failed, 0 skipped after the final changes. Includes 6 media/timeline tests, 7 request tests and 5 publication/persistence source contracts. FFmpeg integration facts explicitly skip when the executable environment variable is absent.
4. `dotnet test ../TodoX.Web.Tests/TodoX.Web.Tests.csproj --no-restore -v quiet` — 1331 PASS, 22 failed, 15 skipped, total 1368 (run before adding the five publication contracts). Failures are in RDance, provider contracts, lifecycle source assertions, billing/static-image policy, Timelapse and favorites UI; none are the new merge/recompose tests. This is a dirty-workspace full-suite result, not a proven pristine baseline. The failure details are in `artifacts/rvideo-final-full-tests.log`; unrelated protected production behavior was not changed to make them pass.
5. `dotnet format TodoX.Web.csproj whitespace --verify-no-changes --no-restore --include Services/VideoRender/FinalMergeMedia.cs Services/VideoRender/VideoRenderMergeHandler.cs Services/VideoRender/RVideoProjectFinalizationService.cs Components/Pages/RVideo/RVideoV2Result.razor` — exit 0. `git diff --check` on changed tracked sources — exit 0.
6. `dotnet build TodoX.Web.csproj -c Release --no-restore -v quiet` — exit 0, 0 errors, 46 existing warnings.
7. `dotnet publish TodoX.Web.csproj -c Release -o ../artifacts/publish/todox-dashboard --no-restore -v quiet` — exit 0. `Test-Path ../artifacts/publish/todox-dashboard/TodoX.Web.dll` — True. This is folder publication only, not an IIS deployment.
8. PowerShell diagnostic script parsed and executed against locally generated media — all probe/packet/frame/decode/freeze/hash commands returned 0 for both scene and final. PowerShell 5.1 native stderr handling was corrected after the first smoke attempt.

No PostgreSQL write/integration test or browser/IIS test was run. Concurrency execution uses an atomic in-memory fake of the existing persistence guard; real database locking and transactional final selection are additionally checked as source contracts, not claimed as real DB integration coverage. Stage these checks before rollout. Visual-content freeze detection is inherently distinct from timestamp/decode validation: valid static scenes are allowed; the operator script supplies frame hashes/freezedetect evidence to compare final against source motion.

## Verification of the staged source independently of unrelated workspace changes

The staged index was exported to ignored `artifacts/rvideo-final-review`. The three test files are linked into the tracked test project; the existing ignored local test project is not required for this command. With `TODOX_TEST_FFMPEG` set:

```powershell
# From repository root, for a fresh checkout or the equivalent staged export:
dotnet test TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj --filter 'FullyQualifiedName~RVideoFinal|FullyQualifiedName~RVideoManualRecompose' -v quiet
```

Result on the staged export: **19 PASS, 0 failed, 0 skipped** (18 new tests/cases and one existing matching test). Running the entire tracked test project also exposed existing-scope failures: 566 passed, 21 failed, total 587 at that point, before the last added publication contract. They were not repaired by editing protected code.

From `artifacts/rvideo-final-review`, `dotnet build TodoX.Web/TodoX.Web.csproj -c Release --no-restore -v quiet` returned 0, 0 errors, 52 warnings. `dotnet publish TodoX.Web/TodoX.Web.csproj -c Release -o D:/todoX/Dashboard-web/TodoXPortal/todoX-Dashboard-SaaS/artifacts/publish/todox-dashboard --no-restore -v quiet` returned 0. The designated DLL was verified (9,662,976 bytes). The final designated artifact therefore comes from the staged source, not the unrelated uncommitted workspace changes. Its logs are `artifacts/rvideo-final-clean-build.log` and `artifacts/rvideo-final-clean-publish.log`.

`dotnet format Tests/TodoX.Web.Phase1B.Tests.csproj whitespace --verify-no-changes --no-restore --include ../TodoX.Web.Tests/RVideoFinalMergeMediaTests.cs ../TodoX.Web.Tests/RVideoManualRecomposeTests.cs ../TodoX.Web.Tests/RVideoFinalPublicationContractTests.cs` also returned 0. Final source whitespace and staged diff checks returned 0.

Post-publication lifecycle/event failures are logged and cannot turn an already-selected valid final version into a failed version. Existing final selection and URL/path atomicity continue to use the existing transaction.

## Operator procedure on Windows Server (read-only source access)

Do not run these against DEV looking for production data. Use an existing operator-managed read-only PostgreSQL role/service; do not paste credentials into commands, reports or chat.

```powershell
# From the repository root; replace only the existing pg_service alias.
psql 'service=YOUR_EXISTING_READONLY_SERVICE' -X -v job_uuid=464639f5-16ee-4953-80c1-2050fcadb3fb -f TodoX.Web/scripts/diagnostics/rvideo-final-evidence.sql -o C:/Evidence/rvideo-job.txt
```

The SQL executes BEGIN READ ONLY and ROLLBACK, with a 60-second timeout. It follows core-job ID, merge-job input project ID and scene/final version render-job relationships. First verify project/tenant/core UUID; then map `selected_video_version_id` and `selected_final_video_version_id`, ordered composition items, render job IDs, durations and events. If the target produces no rows or tables are unavailable, stop and have the operator confirm schema/identity; do not infer IDs or mutate records.

Use paths actually returned by the evidence and composition manifest:

```powershell
./TodoX.Web/scripts/diagnostics/Collect-RVideoFinalEvidence.ps1 `
  -Ffmpeg 'C:/Tools/ffmpeg/bin/ffmpeg.exe' `
  -Manifest 'D:/ACTUAL_PROJECT/final-videos/ACTUAL_VERSION/manifests/composition.json' `
  -FinalVideo 'D:/ACTUAL_PROJECT/final-videos/ACTUAL_VERSION/output/final-video.mp4' `
  -EvidenceDirectory 'C:/Evidence/rvideo-464639f5-attempt-1'
```

Paths above are placeholders, not hard-coded application settings. The evidence directory must not already exist. The script never downloads media or updates the database. If a source is on another instance, have the operator collect it from the correct host or approved storage; no provider submit is needed. Collect each candidate final attempt separately, including failed attempts. Retain original `composition.json`, `concat.txt`, `ffmpeg-copy.log`, `ffmpeg-fallback.log`, music log and project/render events; missing files are evidence too.

Compare video and audio durations, codec/extradata, resolution, FPS, timebase and packet/frame timeline at every scene boundary. Look for large PTS gaps, nonmonotonic DTS, missing decoded frames, copy warnings and audio continuing beyond picture coverage. Compare freeze intervals and frame hashes against the SAME selected source versions; static source scenes are not proof of corruption. Open the same downloaded final bytes in an independent local player and the browser. If decoding/hash evidence shows moving frames and correct coverage, investigate browser cache, byte-range/HTTP delivery and player behavior before declaring a file-level freeze. Do not recompose the production job during this evidence step.

## Safe rollout and acceptance gates

1. Review the feature branch and the 22 dirty-workspace full-suite failures. Do not merge directly to main. Review retained pre-existing scoped UI wiring separately from unrelated uncommitted work.
2. In an approved isolated staging environment with the existing schema/feature flags, run the 54 relevant workspace tests and the tracked focused test command below with FFmpeg/ffprobe matching the server version. Run two real concurrent requests and confirm one active merge row; verify no `render_scene_video` rows/provider submissions/usage charges appear and point cost remains zero.
3. Record the current selected final ID, URL/path and scene selections. Inject an unavailable scene source or local FFmpeg failure in staging; exhaust/retry the merge and prove the selected final ID, URL/path and old file remain unchanged. Retry after failure and after Completed.
4. Restore valid media, recompose and verify a NEW completed final version is selected atomically with the project pointer only after final validation PASS. Check all ordered item IDs, exact configured scene durations, audio sync, mixed codec/FPS/timebase fixtures and browser playback. Verify previous final versions and all scene histories remain accessible.
5. Confirm ffprobe is alongside the configured FFmpeg executable, worker can execute both, H.264/AAC encoders are installed, storage has sufficient space, and merge timeout/disk permissions suit production clips. Evidence collection should establish the production root cause before a repair operation.
6. After approval, operator takes normal backup, schedules deployment of the reviewed folder artifact via the established IIS process, monitors merge validation/fallback events and verifies one approved project. No automatic deployment was performed here.
7. Rollback: restore the previous application package via the existing operator process, retain all media/logs/versions, and suspend new manual recompose requests while investigating. No database rollback or destructive media cleanup is needed for this change.

Git branch: `fix/rvideo-final-merge-recompose`, remote `origin` (`nguyenduc216/todoX-Dashboard-SaaS`). Commit/push outcome and any clean-index verification are reported with delivery; artifacts/publish is excluded from Git.
