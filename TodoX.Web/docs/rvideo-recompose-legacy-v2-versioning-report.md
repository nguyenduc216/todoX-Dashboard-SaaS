# RVideo Legacy/V2 recompose and final versioning follow-up

Baseline: `390fb8559953666e39f56fae222421ef23a994d7`, branch `fix/rvideo-final-merge-recompose`. Source work and validation use an isolated worktree at `artifacts/rvideo-legacy-review`; unrelated dirty-workspace changes are not included. No production access, DB migration/update or IIS deployment was performed. Reference production job remains `464639f5-16ee-4953-80c1-2050fcadb3fb`; its file-level root cause remains unconfirmed without server evidence.

## Findings

Legacy result markup only rendered the initial merge button when FinalVideoUrl was empty. Its CanClickFinalMerge also excluded Completed, so reusing that condition could never offer recompose for Completed finals. The previous V2 button did call the common finalization service, but it refreshed only immediately after enqueue; there was no final-result monitor in the V2 shell to refresh the player after completion.

`SceneMediaVersioningService.IsEnabledAsync` reads GLOBAL `system.app_settings` rows by setting_key, is_active=true, LIMIT 1. It does not read appsettings.json or a tenant-specific override. Only true/1/yes, case-insensitive and without trimming, return true. Missing row, inactive row, false/0/unrecognized/whitespace-padded value return false. Duplicate active rows previously made LIMIT 1 nondeterministic. `database/scene-media-versioning/03_seed_scene_media_versioning_settings.sql` seeds features.final_video_versioning OFF. The existing manual guard intentionally rejects when disabled; deleting the guard would permit unversioned output and violate preservation requirements.

These are source/configuration-file findings. Actual DEV/staging/production setting values, actual installed schema and the number of legacy Completed projects were NOT queried. No approved PostgreSQL environment or credentials were supplied; no production settings were searched on DEV. The new read-only preflight/verify scripts provide those measurements to the operator.

The historical nonversioned merge wrote the project FinalVideoUrl/FinalVideoPath without creating final version/item records. The baseline handler already isolates each new unversioned job output; manual requests must continue to require versioning. A legacy final with no selected final record needs no synthetic Completed backfill: when schema/flag and existing selected completed scene versions are ready, recompose creates a NEW queued final version with ordered items. The previous legacy URL/path are saved in composition_config_json.previousFinal, and the old file is not overwritten. Only validated new output goes through the existing atomic final selection/project pointer transaction.

The old general backfill script marks legacy paths Completed without validating files; it is not used, altered or recommended for this recovery. Missing selected completed scene versions remain a data repair issue, never a reason to auto-pick another version or call an AI provider.

## Changes (all files in this follow-up)

- `Components/Shared/RVideoRecomposeAction.razor`: shared manual action, loading/blocked/active/success/failed states, active-job read-only monitoring, double-click guard, terminal refresh and safe retry. Mounting/polling never enqueues. Uses configured PollIntervalSeconds; cancels monitoring on disposal.
- `Components/Pages/RenderVideoJobs.razor`: add shared recompose next to download for an existing final, independently of CanClickFinalMerge; preserve initial merge for no final. Key final player by URL. Use specific backend rejection messages; do not optimistically mutate project status before backend acceptance.
- `Components/Pages/RVideo/RVideoV2Result.razor`: keep ReMergeAsync as a wrapper around the same shared action and key player by final URL. The existing RVideoV2Shell reload callback is reused unchanged; no second polling/merge implementation is added to it.
- `Services/VideoRender/FinalMergeMessages.cs`: common actionable rejection messages and active render-job status classification.
- `Services/VideoRender/FinalVideoVersioningState.cs`: catalog-only schema prerequisite checks and unambiguous global flag state; distinguish disabled, missing/ambiguous setting, missing schema and DB unavailable. No writes or automatic enablement.
- `Services/VideoRender/RVideoProjectFinalizationService.cs`: manual requests require the detailed readiness result; retain owner/readiness checks, existing atomic active-job enqueue guard and fresh request identities.
- `Services/VideoRender/SceneMediaVersioningService.cs`: expose readiness; final item creation joins exact selected completed versions from the SAME scene/project/tenant, snapshots per-item configured durations; serialize final publication on the project row before final selection. Existing queued request identity remains idempotent. No scene-render/history behavior is changed.
- `Services/VideoRender/VideoRenderMergeHandler.cs`: recheck manual versioning readiness at execution (and reject if the flag changes off), retain previous final location in the composition snapshot. Keep the validated FFmpeg pipeline and publication guards from the baseline.
- `TodoX.Web.Tests/RVideoManualRecomposeTests.cs`: adapt strict fake to the detailed readiness port.
- `TodoX.Web.Tests/RVideoFinalMergeMediaTests.cs`: additionally verify audio/video start and duration alignment on mixed normalized fixtures (tone timestamp checks, not a claim of real speech lip sync).
- `TodoX.Web.Tests/RVideoRecomposeCompatibilityTests.cs`: flag states, actionable messages and Legacy/V2/snapshot source contracts.
- `TodoX.Web.Tests/RVideoRecomposeUiTests.cs`: execute the actual shared Blazor component with HtmlRenderer and strict fake service ports; no enqueue on mount, duplicate click guard, terminal refresh, old URL retention and retry.
- `TodoX.Web.Tests/RVideoRecomposePostgresTests.cs`: guarded REAL PostgreSQL integration harness using existing approved disposable rows/schema and local scene fixtures; no schema creation. Explicitly SKIPPED on this DEV environment, not represented as PASS.
- `Tests/TodoX.Web.Phase1B.Tests.csproj`: link the new tests into the tracked test project.
- `database/manual/rvideo-recompose/preflight.sql`: read-only catalog columns/types/indexes/constraint inventory followed by verification.
- `database/manual/rvideo-recompose/verify.sql`: read-only readiness, flag, legacy final inventory, selected pointer consistency, source selection/orphan/composition checks and duplicate active merge report.
- `database/manual/rvideo-recompose/rollback.md`: prerequisites, operator enablement gate, actual PostgreSQL test setup/commands, application/flag/data rollback separation. `enable.sql` is withheld because actual schema and PostgreSQL integration prerequisites have not been verified; the request allows providing it only after that verification.
- This report, `docs/rvideo-recompose-legacy-v2-versioning-report.md`.

No new migration or legacy final backfill is required when the existing schema is complete. If preflight reports missing schema, installation/repair requires separately approved existing schema scripts; this task does not execute them. Do not blindly set the flag to true.

Protected areas untouched: image generation, scene AI video generation/provider adapters and payloads, TTS/voice synthesis, scene identity/history, pricing/billing/points, RDance and Timelapse. No YEScale calls/metadata changes. New action only enqueues merge_video_job with ffmpeg, zero point estimate and NotRequired point status. Unit tests assert those fields and strict fake ports reject other job operations. No live AI usage/billing database claim is made without the skipped PostgreSQL/worker staging checks.

## Validation and limits

From the isolated worktree's TodoX.Web folder, with TODOX_TEST_FFMPEG set to the local FFmpeg 9.0.2 executable (ffprobe alongside it):

```powershell
dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj --filter 'FullyQualifiedName~RVideoFinal|FullyQualifiedName~RVideoManualRecompose|FullyQualifiedName~RVideoRecompose' --no-restore -v quiet
```

Result: **35 PASS, 0 failed, 1 SKIPPED (real PostgreSQL integration)**. This includes real local FFmpeg integration, executed Blazor rendering/action tests, request unit tests and explicitly identified source contracts. Focused logs: repository-root `artifacts/rvideo-legacy-tests.log`. PostgreSQL prerequisites/schema SQL and data transactions have NOT been executed on a real server; a compiled skipped test is not database validation. No real browser/IIS test was run.

`dotnet test Tests/TodoX.Web.Phase1B.Tests.csproj --no-build -v quiet` without the media executable environment variable: 578 PASS, 21 failed, 6 skipped, total 605. Five media facts and the PostgreSQL fact skipped. The 21 failures are outside this follow-up's focused tests (existing provider/RDance/lifecycle/static contract tests); no protected production behavior was changed to repair them. Full log: `artifacts/rvideo-legacy-full-tests.log`.

`dotnet format TodoX.Web.csproj whitespace --verify-no-changes --no-restore --include Components/Shared/RVideoRecomposeAction.razor Services/VideoRender/FinalMergeMessages.cs Services/VideoRender/FinalVideoVersioningState.cs Services/VideoRender/RVideoProjectFinalizationService.cs Services/VideoRender/SceneMediaVersioningService.cs Services/VideoRender/VideoRenderMergeHandler.cs` and the equivalent tracked-test-project check for the changed/new test files: exit 0 (workspace-load warnings only). `git diff --check`: exit 0 after correcting a trailing space in the SQL literal.

Release build/publish commands, run from isolated worktree root:

```powershell
dotnet build TodoX.Web/TodoX.Web.csproj -c Release --no-restore -v quiet
dotnet publish TodoX.Web/TodoX.Web.csproj -c Release -o D:/todoX/Dashboard-web/TodoXPortal/todoX-Dashboard-SaaS/artifacts/publish/todox-dashboard --no-restore -v quiet
```

The build must have 0 errors and designated TodoX.Web.dll must exist before commit/push. After committing, rebuild/publish the clean committed worktree and verify the DLL's embedded BuildCommit matches that commit; this avoids attributing a baseline-stamped DLL to the new commit. Final command results, remote SHA verification and DLL hash are delivered alongside this report. Artifacts and logs are not committed.

## Release status and operator next steps

**NOT production-ready yet.** Required gates remain: actual schema/data preflight; operator-approved enablement; real PostgreSQL test PASS; actual worker failure/success and billing-ledger checks; real player/voice synchronization; rollout approval. Follow `database/manual/rvideo-recompose/rollback.md`. The new detailed readiness guard will continue blocking safely while the global flag or schema is not ready, with clear guidance in both UIs.

On Windows Server, the operator runs read-only preflight/verify with an existing read-only pg_service, reviews old data and constraints, takes normal backup, prepares isolated staging and runs the guarded integration harness. Only after successful staging and explicit operational approval should the global flag be enabled and the reviewed package deployed through the existing IIS process. Never update production or rerender scenes as part of diagnosis. Existing job evidence scripts remain available for the reference UUID.

Push only the working feature branch to origin; do not merge main. The original dirty workspace changes are preserved and excluded from this follow-up commit. Remote SHA and artifact provenance are verified at delivery.
