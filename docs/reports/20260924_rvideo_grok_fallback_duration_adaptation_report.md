# RVIDEO Grok Fallback Duration Adaptation Report

Date: 2026-09-24

## Scope

This change is limited to RVIDEO scene-video fallback selection, Grok provider-duration adaptation, sanitized fallback diagnostics, and RVIDEO final timeline enforcement. It does not change provider endpoints, credentials, retry/reconciliation classifications, billing ownership, media versioning, database schema, migrations, image/audio/caption generation, RDance, Timelapse, or UI behavior.

## Root Cause

The fourth fallback policy forced `grok_video_heavy` to use `mode=normal`, while the production catalog has no Grok mode contract. Candidate validation also rejected policies with a blank mode and catalog models with an empty `SupportedModes` collection. Grok was therefore rejected with `catalog_mode_not_supported` before provider-duration adaptation could run.

## Fallback Behavior

Before:

1. `veo_omni / flash`
2. `veo_3_1 / fast`
3. `veo_3_1 / lite`
4. `grok_video_heavy / normal` (rejected against the production-like catalog)

After:

1. `veo_omni / flash`
2. `veo_3_1 / fast`
3. `veo_3_1 / lite`
4. `grok_video_heavy / no explicit mode`

An explicit policy mode is validated only when the catalog exposes a non-empty mode contract. The first three VEO policies and their modes remain unchanged. The existing outbound request behavior omits the `mode` field when the policy mode is null, so Grok sends neither `mode=normal` nor an empty mode value.

## Duration Adaptation

The existing ceiling resolver remains authoritative: choose the smallest supported provider duration greater than or equal to the requested scene duration. Tests cover `4 -> 6`, `8 -> 10`, `11 -> 12`, and `13 -> 15`. A 16-second scene does not fall back to 15 seconds; Grok is rejected with `duration_not_supported`, requested duration 16, and maximum supported duration 15.

Mandatory case: **SceneDuration 8 seconds -> Grok ProviderDuration 10 seconds**. The provider request and billable duration use 10 seconds. The scene and scene-video version retain 8 seconds.

## Final Timeline Enforcement

The existing external-voice mux already applies FFmpeg `-t` using `scene.DurationSeconds`, but scenes without external voice previously entered final concat as raw provider files. Final RVIDEO merge now writes an FFmpeg concat `outpoint` for every scene using its authoritative scene duration and adds `-t` using `RVideoRules.CalculateMergedDuration(...)` to both copy-concat and transcode-fallback commands. Thus a raw 10-second Grok result contributes 8 seconds to an 8-second scene, without creating another media version or duplicate provider asset.

## Billing and Metadata

Billing architecture is unchanged. `BillableDurationSeconds` continues to use the resolved provider duration, while `DurationSeconds` continues to store the scene timeline duration. For an 8-second scene submitted to Grok at 10 seconds, provider cost/customer point calculation uses 10 seconds under the existing contract, while scene timing, voice/captions, subsequent scene offsets, and final project duration remain based on 8 seconds. Existing usage metadata continues to record both `requestedDurationSeconds` and `providerDurationSeconds`.

## Changed Files

- `TodoX.Web/Services/VideoRender/RVideo79AiVideoService.cs`
- `TodoX.Web/Services/VideoRender/SceneVideoWorkerHandler.cs`
- `TodoX.Web/Services/VideoRender/VideoRenderMergeHandler.cs`
- `TodoX.Web/Tests/RVideoVideoHotfixTests.cs`
- `TodoX.Web/Tests/RVideoRuntimeSqlTests.cs`
- `docs/reports/20260924_rvideo_grok_fallback_duration_adaptation_report.md`

## Validation

- Focused new/updated regression tests: 13 passed, 0 failed.
- `RVideoVideoHotfixTests`: 122 passed, 5 pre-existing failures. The same five brittle/source-level failures existed before this change: missing manual rerender event text, reflection parameter mismatch, missing same-model fallback source text, newline-sensitive billing assertion, and an overbroad authorization sanitizer assertion.
- `RVideoProviderPollingRegressionTests`: 88 passed, 2 pre-existing failures: a reflection parameter mismatch and a stale source-text assertion for `route.ModelName`.
- Combined RVIDEO hotfix/runtime run: 200 passed, 7 pre-existing failures, including two lifecycle source assertions outside this task.
- `dotnet format TodoX.Web.csproj whitespace --verify-no-changes --no-restore --include ...`: passed.
- `dotnet build TodoX.Web.csproj -c Release --no-restore`: passed with existing generated Razor nullable warnings.
- `dotnet publish TodoX.Web.csproj -c Release --no-restore -o D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`: passed.
- `git diff --check`: passed.

No database update or migration is required.
