# RVideo recurring final freeze — 2026-10-28

## Confirmed incident

Job `83153b15-07ca-4347-b741-85c751847499` maps to project 206. The latest completed merge is `69139457-1406-4573-b0b8-d4a0a6c2a5ff`, claimed by worker `WIN-HSHL54Q184K-4c61688c089947db9dee124981f51500`. Its final file is under `E:\N8N.ANHDUC\todoX\work\video-render-jobs\20261028-105546\final\6913945714064573b0b8d4a0a6c2a5ff\final.mp4` on the worker machine. This drive is unavailable locally. The final and five selected source files were downloaded from their existing public URLs for local analysis. Database inspection used parameterized SELECTs inside READ ONLY transactions followed by rollback; no application/database state was changed.

The final is 30 seconds with 720 video frames and continuous audio. FFmpeg `freezedetect=n=-60dB:d=1` reports a freeze from 19.876067 to 23.916667 seconds. This is scene 4, configured at seconds 20–24. Selected scenes 1–5 have durations 8, 6, 6, 4, 6 seconds. Scenes 1, 2, 3 and 5 have video timebase 1/90000; scene 4 has 1/12288. All report 24 FPS. A nominally matching FPS and successful FFmpeg exit do not establish concat compatibility.

Execution path: existing finalization service -> merge queue -> RenderJobWorker -> VideoRenderMergeHandler -> selected completed versions/localization -> FinalMergeMedia -> validation -> publication. Current code already checks timebase compatibility and normalizes heterogeneous inputs; the incident final fails its timeline validation (`non_monotonic_or_gapped_video_timestamps`) and motion validation (`scene_motion_lost:3:offset=20`). Production events show PROJECT_MERGED without PROJECT_MERGE_VALIDATED or the normalization fallback event. This demonstrates that the worker output did not pass the current validation path; the exact binary SHA on the remote worker has not been inspected. Updating only the web frontend cannot establish that this queue worker is running the fix.

## Local reproduction and repair

Recomposing the actual five downloaded selected sources through the current merge pipeline selects `incompatible_input_streams`, normalizes the sources and produces a new 30-second file. Timeline, strict decode, audio and source-relative motion validation PASS. The same FFmpeg freeze scan reports no freeze interval. Local artifact: repository-root `artifacts/rvideo-job-83153b15/repaired/final-repaired.mp4`. The original final/source files are preserved. The repaired file has not been uploaded or selected in production.

An additional defect was independently reproduced before changing production code: a four-second moving source whose final picture freezes after one second still passes the previous whole-scene motion check, because some initial movement is sufficient. The new regression failed on the original code at `Assert.NotNull(error)`. The fix compares overlapping four-sample windows at the existing two-samples/second rate, in addition to the whole-scene check. Movement elsewhere in the scene no longer masks a sustained partial freeze. A pause already present in the selected source remains valid. This remains bounded sampled motion detection, not proof against every brief freeze or semantic mismatch. Existing normalization, audio handling and publication contracts are unchanged.

## Changed files and scope

- `TodoX.Web/Services/VideoRender/FinalMergeMedia.cs`: detect loss of source motion within a scene, triggering the existing normalization fallback or rejection before publication.
- `TodoX.Web.Tests/RVideoFinalMergeMediaTests.cs`: configurable fixture duration; regressions for partial freeze with continuous audio and successful fallback, legitimate source pause, and the fourth of five scenes having a different timebase.
- `TodoX.Web/docs/rvideo-job-83153b15-freeze-report.md`: this report.

Untouched: scene video generation, image generation, voice/audio generation and mux, provider payloads/model metadata, points/billing, public APIs, configuration and database schema/data. No migration or database update is required. No YEScale tools were called because provider behavior/metadata was not changed. Existing untracked user files were left untouched.

## Validation

Commands below run from `TodoX.Web`; FFmpeg 9.0.2 and adjacent ffprobe are local tools under ignored repository-root `artifacts/tools`.

```powershell
$env:TODOX_TEST_FFMPEG = (Get-ChildItem ../artifacts/tools -Recurse -Filter ffmpeg.exe | Select-Object -First 1).FullName
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj --no-restore --filter 'FullyQualifiedName~MovingSceneWithFrozenTailAndContinuousAudioTriggersRepair' -v quiet
```

Blocked by existing CS7036 at `Tests/RVideoVideoHotfixTests.cs:2852`: missing `reconciliationConfiguration` constructor argument. No unrelated test or production behavior was changed. The existing local `TodoX.Web.Tests.csproj` can compile and execute the linked test source:

```powershell
dotnet test ../TodoX.Web.Tests/TodoX.Web.Tests.csproj --no-restore --filter 'FullyQualifiedName~MovingSceneWithFrozenTailAndContinuousAudioTriggersRepair' -v quiet
```

Before fix: 1 failed, proving the partial-freeze validation hole.

```powershell
dotnet test ../TodoX.Web.Tests/TodoX.Web.Tests.csproj --no-restore --filter 'FullyQualifiedName~RVideoFinal|FullyQualifiedName~RVideoManualRecompose|FullyQualifiedName~RVideoMerge' -v normal
dotnet format TodoX.Web.csproj whitespace --verify-no-changes --no-restore --include Services/VideoRender/FinalMergeMedia.cs
dotnet format ../TodoX.Web.Tests/TodoX.Web.Tests.csproj whitespace --verify-no-changes --no-restore --include ../TodoX.Web.Tests/RVideoFinalMergeMediaTests.cs
git diff --check
dotnet build TodoX.Web.csproj -c Release --no-restore -v quiet
dotnet publish TodoX.Web.csproj -c Release -o ../artifacts/publish/todox-dashboard --no-restore -v quiet
Test-Path ../artifacts/publish/todox-dashboard/TodoX.Web.dll
```

Final relevant tests: 24 passed, 0 failed, 0 skipped, including real FFmpeg fixtures. Both format checks and diff check: exit 0. Release build: exit 0, 0 errors, 46 existing warnings. Folder publish: exit 0; DLL existence verified True. Publish artifacts are ignored and excluded from commit. Full unrelated suites were not rerun; the tracked Phase1B test project remains blocked as described above.

Actual-media replay used an ignored local harness calling the real `FinalMergeMedia` methods:

```powershell
dotnet run --project ../artifacts/tmp/rvideo-media-check/Check.csproj -- ../artifacts/rvideo-job-83153b15 $env:TODOX_TEST_FFMPEG
& $env:TODOX_TEST_FFMPEG -hide_banner -i ../artifacts/rvideo-job-83153b15/repaired/final-repaired.mp4 -an -vf 'freezedetect=n=-60dB:d=1' -f null -
```

Replay exit 0: original rejected, incompatible-stream fallback selected, repaired timeline/motion PASS. Freeze scan exit 0: no freeze interval. Evidence and command logs remain under ignored `artifacts/rvideo-job-83153b15` and `artifacts/rvideo-partial-freeze-*.log`.

Production rollout remains outstanding: verify/update the actual merge worker on WIN-HSHL54Q184K, ensure all consumers of this merge queue use the validated implementation, then recompose through the existing application action and verify PROJECT_MERGE_VALIDATED plus the new final. No production deployment, restart, enqueue, source rerender, charge, or final-pointer write was performed by this task.
