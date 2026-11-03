# Project 201 — final recompose acceptance checklist

Baseline f80621e3b851ec06e650bb8a88cafbca4a093e54, branch fix/rvideo-final-merge-recompose. No production access/write, feature-flag change, IIS deployment, scene rendering, provider/TTS call, billing operation or main merge was performed. Work uses isolated artifacts/rvideo-acceptance-review; unrelated dirty edits are excluded.

## Pipeline review and local evidence

No production pipeline change was needed in this follow-up. FinalMergeMedia already validates copy output even at exit 0, checks video/audio timeline/decode and source-relative motion. Rejection enters the SAME normalized fallback: each configured scene gets PTS reset, scale/pad/FPS normalization, H.264/yuv420p with video track timebase 90000, existing audio resampled/re-encoded AAC 48 kHz stereo (silence only for missing-audio intervals when other selected sources have audio). The normalized concat is re-encoded H.264/AAC and validated again. Failure throws before selected-final publication. VideoRenderMergeHandler repeats validation after existing optional music mixing and preserves the previous final through the existing version/publication transaction. No alternative merge pipeline is introduced.

The strengthened FullDurationAudioWithFrozenPictureIsRejectedAgainstMovingSelectedSources test deliberately supplies a stale concat pointing to a valid four-second frozen H.264/AAC file while selected inputs point to moving clips. Actual copy exits 0; generic duration/timestamps/audio/decode pass; motion validation supplies scene_motion_lost; the real fallback produces two normalized files, H.264/AAC output and fallback-validation.log=PASS. Final timeline/decode/audio/motion checks pass and the old frozen file remains. This proves fallback routing for that controlled defect, not the production freeze root cause.

Local focused suite: **50 PASS, 0 failed, 1 SKIPPED**, total 51. The skipped real PostgreSQL integration test remains disabled because no approved staging runner exists. Nine-scene fixture, different codecs/FPS/timebases, homogeneous copy, short audio rejection, static-source acceptance, invalid copy timeline, UI submit/refresh/retry and local media-resolution tests are included. The nine-scene fixture uses synthetic two-second clips, not the actual project 201 files.

Command from isolated root, with TODOX_TEST_FFMPEG pointing at existing FFmpeg 9.0.2 and adjacent ffprobe:

```powershell
dotnet test TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj --filter 'FullyQualifiedName~RVideoFinal|FullyQualifiedName~RVideoManualRecompose|FullyQualifiedName~RVideoRecompose|FullyQualifiedName~VideoMergeCrossInstance' -v quiet
```

Logs: ignored artifacts/rvideo-acceptance-tests.log. Source-relative sampled motion is not proof of exact frames, all partial freezes or lip synchronization; actual playback remains mandatory.

## Changed files

- TodoX.Web.Tests/RVideoFinalMergeMediaTests.cs: strengthen the existing frozen-picture test to exercise actual copy -> rejection -> normalized fallback and assertions for codec/audio/logs/validation.
- scripts/diagnostics/Test-RVideoSelectedSceneReadAccess.ps1: read-only file access/hash check scoped to an operator-exported exact selected manifest and expected worker account/project/tenant/job/scene count. It writes only JSON to stdout, opens source files READ only and never changes ACLs, impersonates, downloads media, contacts DB or selects another file/version. Operator redirection to an evidence file is optional and explicit.
- This report/checklist, docs/rvideo-project-201-acceptance-checklist.md.

No runtime application change, schema/migration or flag update is required by these test/operational additions. Protected provider, AI scene generation, TTS, billing/points, scene identity/history, RDance and Timelapse remain unchanged.

Script smoke: local generated MP4 with nine distinct synthetic scene/version identities -> allReadsPassed=true, count=9; wrong account and wrong project rejected. These synthetic entries reference one local file to test full-byte reads; this is not evidence that nine production files are readable. No production manifest or service token was available. Hash/read tests do not prove decode or media quality.

## Manual gate A — exact nine sources and worker READ access

[ ] Operator exports read-only project manifest using database/manual/rvideo-recompose/project-manifest.sql with project_id=201, actual tenant ID and core job UUID 464639f5-16ee-4953-80c1-2050fcadb3fb. Follow project-201-recovery.md. Inspect exact nine selected versions, distinct scene IDs/version IDs/order, completed/is_selected, lineage and configured duration sum. Preserve old final URL/path/hash and selected-final pointer. Do not backfill an unverified final Completed.

[ ] Establish the worker host and actual execution identity (IIS pool, Windows service or separate worker process); use existing approved service/account tooling. Run the command IN THAT execution context, not an Administrator shell. The script does not create a task/service or acquire credentials. Comparing an account name is only a guard: operator must verify actual token/groups, network credentials, sandbox and filesystem mappings match the live worker. A different logon token for the same account is not sufficient proof.

```powershell
# From repository root, inside the approved worker execution context.
# Substitute actual tenant and identity values from operator evidence, never guess them.
./TodoX.Web/scripts/diagnostics/Test-RVideoSelectedSceneReadAccess.ps1 `
  -Manifest C:/Evidence/rvideo-201-selected-scenes.json `
  -ExpectedProjectId 201 -ExpectedTenantId ACTUAL_TENANT_UUID `
  -ExpectedJobId 464639f5-16ee-4953-80c1-2050fcadb3fb `
  -ExpectedWorkerIdentity 'ACTUAL_WORKER_ACCOUNT' -ExpectedSceneCount 9
```

[ ] All nine full-byte reads/hash checks succeed, identities/path mappings agree with the original read-only manifest, no source changes during reads. Capture JSON stdout to an operator-controlled NEW evidence file if desired. Missing/stale/relative SourceFilePath is a BLOCK, not permission to substitute a video. If application localization via storage key/media ID is required, operator first establishes the SAME selected version's canonical local path/hash and documents that mapping while retaining the original manifest; no provider/render operation is needed. Do not claim an unmapped file passes. No production read script was run from DEV.

[ ] Read/probe each exact source and old final with Collect-RVideoFinalEvidence.ps1. Review FFmpeg errors, duration, FPS, codec, timebase, packet PTS/DTS and picture continuity at boundaries. Verify current configured scene durations (do not infer them merely from the old final length). Verify FFmpeg and adjacent ffprobe run under worker token and libx264/AAC are available; verify approved output/temp permission checks, disk space and timeout. Read access alone does not validate temp/output writes or public HTTP access. This task does not perform any production write probe.

## Manual gate B — independent PostgreSQL staging

[ ] Obtain owner approval for isolated staging endpoint/database/user and runner secret injection; never target production/DBeaver connection. Existing user confirmation says NO such runner is approved yet. Required exact environment variable names and allowlist/fixture instructions are in database/manual/rvideo-recompose/project-201-recovery.md and rollback.md. Do not print connection strings/secrets.

[ ] Provision disposable legacy Completed project with NULL selected final, old final fixture, exact selected completed local scene versions, verified tenant/project/scene lineage, existing reviewed schema and one enabled test feature setting. Stop workers for transaction harness; use independently approved provisioning only. No migration/schema creation or flag update is done by the test/task.

[ ] Run RVideoRecomposePostgresTests against that fixture and retain actual PASS results. Confirm two concurrent requests produce one active merge, new logical ID after terminal failure, queued-version idempotency, exact ordered items, atomic selected final URL/path and no orphan items. A SKIPPED fact or fake-port test does not satisfy this gate.

[ ] In a separate staging exercise with real worker enabled, inject FFmpeg/read failure; verify old selected final/URL/path/hash and scene states remain unchanged. Retry with valid media; verify new version/path, final-validation PASS and atomic selection. Compare before/after render_scene_video rows, provider task IDs, TTS work, AI usage and points ledger: no new AI/provider/TTS/scene-render/charge activity attributable to recompose. Unit tests establish zero cost/NotRequired merge-only request fields, not a production ledger audit.

## Manual gate C — IIS/package and UI acceptance

[ ] On staging, verify delivered DLL BuildCommit and publish-provenance.json match the reviewed branch commit; retain package backup. The task only publishes artifacts/publish/todox-dashboard, never deploys it to IIS.

[ ] Operator performs separately authorized staging deployment using the established IIS process. Preserve app settings/secrets, uploads and version history; do not copy runtime media from DEV or overwrite production directories. Verify worker registration/claims and application pool/service identity match the access evidence. No automatic deployment, recycle or ACL edit is included here.

[ ] As owner in Legacy AND V2 Dashboard, open Completed result with old final. “Ghép lại video cuối” is visible. Opening/refreshing tab does not enqueue. One click creates one FFmpeg merge; double-click/concurrent tab is blocked/reuses active job. Non-owner is rejected. Disabled/invalid schema/flag shows actionable block.

[ ] While merging, old final remains playable. On success, project/player refresh to new distinct version/URL. On failure, old final and scene states/history remain, error is visible and retry works without reopening or creating a new project. Verify no provider/TTS/billing activity as above.

[ ] Browser GET/range/seek/download of the new file works through IIS (correct media path/content type/range response); compare downloaded bytes/hash with worker output. Play all nine boundaries, continuous motion where sources move, configured total duration and NATIVE voice sync in browser and independent player. Timestamp/sample checks alone do not establish lip sync. Retain old final access/hash evidence.

## Production decision and safe manual action

**NOT ready for production recovery until gates A–C are recorded PASS and operator explicitly approves rollout/global flag impact.** No project 201 media/worker permissions, PostgreSQL staging or IIS/browser acceptance was established by DEV. Production freeze cause remains unconfirmed without its actual files/logs.

After acceptance only: operator follows project-201-recovery.md for approved package rollout and managed GLOBAL flag enablement, re-runs read-only schema/data/selected-version checks, checks no active merge, then uses the existing Dashboard action ONCE. Do not rerender any of the nine scenes, call provider/TTS, backfill a fake Completed final or edit DB pointers to force completion. Monitor merge logs/events, codec/fallback reason and final validation; preserve all old files/history. Stop on failure and fix the proven readiness/media issue before canonical retry. The task itself does none of these production actions.

Rollback: settle/cancel active jobs through supported mechanisms, suspend new manual requests, restore recorded flag/application package via approved operator process; preserve media/history/logs. Do not delete scene/final versions or blindly restore old frozen final pointers. No schema rollback is needed for this change.

## Build/lint/publish record

From isolated root: dotnet format TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj whitespace --verify-no-changes --no-restore --include TodoX.Web.Tests/RVideoFinalMergeMediaTests.cs; git diff --check; PowerShell parser and local smoke checks for the new script. Release commands: dotnet build TodoX.Web/TodoX.Web.csproj -c Release --no-restore -v quiet; dotnet publish TodoX.Web/TodoX.Web.csproj -c Release -o D:/todoX/Dashboard-web/TodoXPortal/todoX-Dashboard-SaaS/artifacts/publish/todox-dashboard --no-restore -v quiet. Commit-stamped DLL/provenance and remote SHA are verified at delivery. Artifacts/logs are ignored and not committed. No unrelated full-suite repair is attempted; prior unrelated failures remain documented.
