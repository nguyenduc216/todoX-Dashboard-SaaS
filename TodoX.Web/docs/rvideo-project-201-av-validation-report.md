# Project 201 final recompose — audio/video validation follow-up

Branch: fix/rvideo-final-merge-recompose. Baseline: 191cb049c2547900bb57fdbe1664ea764c4e8eb0. Implementation/test work is isolated in artifacts/rvideo-av-review. No production access, global flag update, DB write/migration, IIS deployment or main merge was performed. Existing unrelated workspace edits are excluded.

## Findings and evidence

The existing Manual Recompose/Final Video Versioning flow is retained: Dashboard Legacy/V2 -> shared RVideoRecomposeAction -> RVideoProjectFinalizationService -> one zero-point-cost merge_video_job -> VideoRenderMergeHandler -> selected version/local media resolution -> existing FFmpeg merge/normalize -> validation -> existing transactional final selection. It does not enqueue scene render, provider or TTS jobs. Ownership/readiness/concurrency/retry guards are unchanged.

The earlier local mixed-timebase fixture proves copy-concat can return exit 0 with an invalid picture timeline; that remains a plausible mechanism, not the confirmed root cause for production job 464639f5-16ee-4953-80c1-2050fcadb3fb/project 201. No actual source/final media or FFmpeg logs from project 201 were inspected in this task, so no production root cause is claimed.

A further gap was identified in the existing validator: frame timestamp/duration/decode checks alone accept repeated pictures with valid timestamps, and audio validation existed only in fixtures, not the runtime publication path. The new executable FullDurationAudioWithFrozenPictureIsRejectedAgainstMovingSelectedSources fixture creates a four-second H.264/AAC file with a repeated first picture and continuous audio. It passes generic timestamp/duration/decode/audio checks, but the new source-relative motion check rejects it; remerging its moving selected sources passes. This is a controlled DEV reproduction, not production evidence.

## Changed files and behavior

1. Services/VideoRender/FinalMergeMedia.cs: strict decode maps video AND available audio; audio stream start/duration must match the configured timeline within 250 ms, decoded audio timestamps must increase and cover the timeline. Silent selected sources remain supported. If any selected source contains audio, a copy/fallback result missing audio is rejected. Copy rejection enters the existing normalization/transcode fallback; fallback rejection fails the attempt.
2. The same file adds source-relative motion validation for EVERY ordered scene interval. It samples grayscale 32x32 pictures at 2 FPS from each selected source and the corresponding final interval; if the source has perceptible sampled motion but the final interval does not, it rejects scene_motion_lost with scene order/offset. Mean luma change greater than 1 ignores low-level compression noise. Missing samples/decode errors fail closed. Temporary validation samples have unique paths and are deleted; selected media files are untouched. This adds local decode work per scene and requires temporary disk/write permission for the worker.
3. Services/VideoRender/VideoRenderMergeHandler.cs: repeat the same motion validation after the existing optional background-music mix and before final-validation PASS/publication. Previous final version/URL/path/file remain untouched on rejection through the existing failure/retry flow. No second composition pipeline or final-selection mechanism was introduced.
4. Components/Shared/RVideoRecomposeAction.razor: label the existing shared Dashboard action “Ghép lại video cuối”. Legacy/V2 still use the same backend and read-only status monitoring.
5. TodoX.Web.Tests/RVideoFinalMergeMediaTests.cs: four new executable local fixtures: full-duration audio with frozen picture rejected against moving inputs; short audio rejected despite valid picture timeline; static source allowed while intervals are checked independently; nine ordered selected scene inputs with heterogeneous codecs/FPS/timebases merge successfully with audio and sampled motion. Existing fixtures cover homogeneous copy, copy-exit-zero invalid timeline, malformed input and normalization.
6. This report, docs/rvideo-project-201-av-validation-report.md.

Existing scene identity/order/configured durations and selected scene media are reused. No render/provider/TTS/pricing/points/scene-history/RDance/Timelapse behavior changed. No new schema, FK, synthetic Completed backfill or API endpoint was added. No database update is required. Versioned output uses the existing distinct final version directory and is selected only after validation and persistence.

## Validation

From isolated worktree root, TODOX_TEST_FFMPEG points at the existing local FFmpeg 9.0.2 essentials executable, with ffprobe alongside:

```powershell
dotnet test TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj --filter 'FullyQualifiedName~RVideoFinal|FullyQualifiedName~RVideoManualRecompose|FullyQualifiedName~RVideoRecompose|FullyQualifiedName~VideoMergeCrossInstance' --no-restore -v quiet
```

Result: **50 PASS, 0 failed, 1 SKIPPED**, total 51. The skipped fact is REAL PostgreSQL integration; the user confirmed no approved staging runner exists. Includes actual local FFmpeg fixtures (nine two-second inputs give an 18-second synthetic timeline, not project 201's production bytes), actual shared Blazor component tests for no enqueue on mount/duplicate guard/completion refresh/failure retry, request unit tests and explicit persistence source contracts. The tests confirm the manual action/media stages in local test environments; they do not establish real PostgreSQL transaction or production worker success.

Runtime source-relative motion comparison is a screening check, not frame-exact fidelity/semantic proof. It detects loss of sampled motion, not every partial freeze, wrong scene, intentional cut or lip-sync defect. Static sources are allowed. Audio timestamps/start/duration/decode establish timeline alignment, not semantic speech/lip synchronization. Human playback and comparison of actual selected sources at all boundaries remain mandatory for project 201. Existing normalization can pad shorter inputs; operators must verify real input durations rather than interpreting a full output duration as proof of continuous source motion.

Lint commands (exit 0):

```powershell
dotnet format TodoX.Web/TodoX.Web.csproj whitespace --verify-no-changes --no-restore --include TodoX.Web/Services/VideoRender/FinalMergeMedia.cs TodoX.Web/Services/VideoRender/VideoRenderMergeHandler.cs
dotnet format TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj whitespace --verify-no-changes --no-restore --include TodoX.Web.Tests/RVideoFinalMergeMediaTests.cs
```

Release build/publish commands from isolated root:

```powershell
dotnet build TodoX.Web/TodoX.Web.csproj -c Release --no-restore -v quiet
dotnet publish TodoX.Web/TodoX.Web.csproj -c Release -o D:/todoX/Dashboard-web/TodoXPortal/todoX-Dashboard-SaaS/artifacts/publish/todox-dashboard --no-restore -v quiet
```

Build must have zero errors; DLL existence and committed BuildCommit are verified before delivery. Final test/lint/build/publish logs are under ignored artifacts/rvideo-av-*.log. The committed worktree is rebuilt/published after commit so the delivered DLL metadata identifies that commit; artifacts/provenance are not committed. Full-suite unrelated failures from prior tasks remain documented; unrelated protected production behavior is not changed to repair them.

## Conditions still missing for actual project 201 recovery

Project 201 is NOT declared ready for a real merge. Follow database/manual/rvideo-recompose/project-201-recovery.md and the existing read-only diagnostic scripts:

- Operator confirms exact nine selected IDs, distinct scene identity, tenant/project/scene lineage, ordering and configured durations, old final URL/path/hash and selected-final pointer. NULL selected final is supported without a fabricated Completed backfill.
- Actual selected files resolve under the worker identity on the claiming instance, decode correctly, contain expected motion/audio and are long enough; collect ffprobe PTS/DTS/timebase/FPS/codec/audio evidence and compare the old final. The production freeze root cause remains open until that evidence exists.
- Worker FFmpeg/adjacent ffprobe/encoders, temp/output storage permission, timeout/disk space, handler/claim configuration and media/public paths are verified. Additional motion/audio checks increase decode cost; use server-sized staging clips to validate runtime/timeouts.
- Independently approved PostgreSQL staging fixture/runner, real integration PASS, actual worker failure-retains-old-final/success-selects-new-final exercise and database billing/provider/TTS/scene-job checks. No write test may target production or the DBeaver production connection.
- Existing GLOBAL final-versioning flag is still reported false. Only operator approval after schema/data/media/worker/staging acceptance may enable it through managed settings; no enablement was performed here. Review global tenant impact and rollback first.
- Approved package rollout and real playback of the new final across all nine scene boundaries, including NATIVE voice sync. Old file/history must remain intact and accessible. No automatic IIS deployment/main merge is authorized.
