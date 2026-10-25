# P0: generation-aware Supersede pre-submit guard

- Date: 2026-10-25, Asia/Saigon. Agent: Codex.
- FEATURE_ID: TODOX-RVIDEO-VIDEO-RERENDER.
- CHANGE_ID: TODOX-20261025-SUPERSEDE-PRESUBMIT-001.
- Worktree/branch: wt-scene05-main-release / main, continuing the session's authorized main workflow. Requested baseline 27ead400; actual clean source base 0486bad7214463e5ab6e02f9cfd4d19a2e9fb269 preserves the preceding fixes. SceneVideoSubmitCoordinator.cs is unchanged between those two commits, so the reported guard defect applies to both. Remote main matched 0486bad before this change.
- Scope: pre-submit intent/job/version exclusions, PostgreSQL regression tests, handoff, Release build and local publish. No production SQL, provider call, IIS deployment or old-task reconciliation.

## Root cause and ownership

SceneVideoSubmitCoordinator.SceneSubmitBlockReasonSql used only scene_video_job_is_superseded to waive historical blockers. That function recognizes the directly acknowledged old_job_id, not every earlier scene operation. Production-shaped cases for Project 196 / Scenes 865, 866 and 869 reproduced the exact refusal: worker claim succeeds for activated generation 1, but the earlier failed confirmed-task ancestor blocks ClaimSubmit before creating a new intent. The directly acknowledged job is superseded while the ancestor is not. The three cases failed before the fix at expected Claimed versus actual Blocked.

The coordinator now proves a fully matched activated generation and derives earlier immutable operation identities in one shared SQL CTE, reused by all three refusal clauses and the UI state reader. It does not broaden the database function's meaning. Only same-tenant/project/scene linked earlier versions with lower/legacy generations qualify, and intent logical identity must match. Current intents/provider identities, future/same/invalid generations, orphan identities and incomplete activation remain protected. Locking/CAS/physical-send code is unchanged. No public API or provider payload change.

## Files

- TodoX.Web/Services/VideoRender/SceneVideoSubmitCoordinator.cs.
- TodoX.Web/Tests/SceneVideoOperationDraftPostgresTests.cs.
- TodoX.Web/docs/rvideo-supersede-presubmit-p0-20261025.md.
- This history and code-history/features/TODOX-RVIDEO-VIDEO-RERENDER.md.

No Razor, worker/provider, image/audio, billing, configuration or migration files changed.

## Verification and publish

- PostgreSQL 17.10, fresh empty UUID-named local loopback databases only; explicit TODOX_SUPERSEDE_TEST_DB with no production fallback.
- `dotnet test TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Scene07RetryControlsTests|FullyQualifiedName~SceneVideoSupersedeUiTests|FullyQualifiedName~SceneVideoOperationDraft|FullyQualifiedName~SceneImageVersionConcurrencyContractTests|FullyQualifiedName~RVideoRenderRerenderReadinessTests|FullyQualifiedName~RenderJobQueueFairnessTests" --logger "trx;LogFileName=focused-final.trx" --results-directory artifacts/rvideo-presubmit`: PASS, 99 total, zero failures/skips. Includes 17 PostgreSQL-suite cases, 15 accessing the isolated database and two connection rejections. Ten new cases include normal generation-1 sends, conservative guard negatives, concurrent idempotent claims, physical lease waiting and fresh generation-2 handling of a failed/no-intent generation 1. Historical rows and points remain equal.
- `dotnet build TodoX.Web/TodoX.Web.csproj -c Release --no-restore`: PASS, 0 errors, 52 existing warnings. Razor/MudBlazor analyzers run with build. `git diff --check` and strict UTF-8 checks required before commit.
- Publish command: `dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard` in this worktree, as required by its AGENTS.md. Verify TodoX.Web.dll and matching package files. Republish after commit so embedded BuildCommit identifies this source commit; manifest includes the actual full SHA. Artifacts are excluded from Git. Final result/path reported with handoff.

## SQL and already-failed job

No additional migration after Supersede V1 for this fix; no SQL file changed or executed on production. Do not rerun an already installed V1 migration. This statement concerns the pre-submit fix, not a new claim about unrelated production-schema drift.

Keep failed generation-1 job e417e3f6-30b6-4b34-a9ba-2486c13e3986 unchanged. The authorized user starts an explicit new operation with fresh consent/key to create generation 2; do not queue/retry the failed job or resend its logical request. Existing old provider identities and outcomes are retained. No such user operation was performed by Codex.

Commit SHA: see Git commit containing this history entry. Stage only the five scoped files; commit/push to origin/main after verification and report actual remote SHA. No force push or production deployment.
