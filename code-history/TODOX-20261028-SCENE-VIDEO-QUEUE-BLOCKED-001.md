# Scene video queue ownership quarantine hotfix — NOT ACCEPTED for production yet

- CHANGE_ID: TODOX-20261028-SCENE-VIDEO-QUEUE-BLOCKED-001
- FEATURE_ID: TODOX-RVIDEO-VIDEO-RERENDER
- Date: 2026-10-28
- Worktree: `D:\todoX\Dashboard-web\TodoXPortal\wt-scene-video-queue-blocked`
- Base: `origin/main` at `1101084`; branch `fix/scene-video-queue-blocked-20261028`.
- Commit SHA: see the commit containing this record; remote SHA is reported after push.

## Evidence and root cause

The operator confirmed production DLL commit `ff693e3dc7c1887f51633134b931d29dfc47e406`, matching the previous release manifest SHA256. That commit's `ClaimNextInternal` selects an eligible queued row by priority/ready-time with `FOR UPDATE SKIP LOCKED LIMIT 1`, then updates it to preparing and increments attempt_count in the same transaction. It has NO `scene_video_job_is_current` predicate. `origin/main` at `1101084` DOES have that ownership predicate for scene-video/batch jobs. Verified with `git show ff693e3:TodoX.Web/Services/Render/RenderJobService.cs` versus the corresponding main file. The production binary therefore lacks the ownership filter already present on main.

The supplied production observations (13 NOT_ACTIVE_JOB rows, generation zero, repeated 55000 and attempt_count zero) match the deployed source path: the generation fence rejects the preparing UPDATE; transaction disposal rolls back the lease/attempt; the outer worker catches/logs/delays, then all three lanes can select the same earliest candidates again. SKIP LOCKED avoids simultaneous row ownership, but cannot exclude a candidate after the rejecting transaction releases its lock. The worker's existing post-claim quarantine is never reached when claim itself fails.

In the source migration, the BEFORE UPDATE fence calls `assert_video_operation_current`, locks the head FOR SHARE and rejects an unequal active job even at generation zero. A head at generation zero is NOT blanket legacy authorization. No-head jobs pass the migration's current-function predicate because there is no contradictory head; the existing writer/submit coordinator still requires project/scene/logical ownership and durable submit intent checks.

Production trigger existence and target function were confirmed by the operator. Complete production function/trigger definitions were NOT supplied or read, so full deployed-schema equivalence is NOT verified. The added read-only collector exports the exact definitions for operator comparison. No PostgreSQL connection, fixture, SQL collector, production UPDATE or provider HTTP call was executed during this task.

## Minimal runtime change

Only `TodoX.Web/Services/Render/RenderJobService.cs` changes production behavior:

- Claim candidates remain row-locked with SKIP LOCKED and existing ordering. The loop continues after a rejected scene-video candidate; no recursive retry and no attempt consumed.
- Check operation ownership before UPDATE, including head-zero active-job identity. No-head candidates must have legacy generation zero/absent, valid tenant/project/scene/logical identity and no conflicting version owner. Fresh submissions carrying job/version/provider/submit-intent evidence are held for review. Existing known-task providerPoll claims retain their separate path and existing coordinator guards.
- Record `SCENE_VIDEO_QUEUE_QUARANTINED` in the existing job-event table inside the candidate transaction, then exclude that marker from later claims. A fresh post-lock marker check prevents concurrent lanes from adding the same audit after a stale SELECT snapshot.
- Use a savepoint around the preparing UPDATE. Catch ONLY SQLSTATE 55000 whose MessageText starts with `VIDEO_OPERATION_SUPERSEDED:`. Roll back to savepoint, retain the original candidate lock, write audit, commit and continue. Other PostgreSQL errors propagate.
- Superseded job rows cannot themselves be marked failed/pending by this trigger. Quarantine is therefore a durable queue exclusion plus audit, NOT an UPDATE to the immutable historical job. Its status remains queued; queued counts alone are not an acceptance measure. No trigger exception/disable, head rebind, selected-version change, submit-intent change, billing write or provider resend.
- Audit failure rolls the candidate transaction back. It does not silently drop the job or proceed without durable evidence.

`SceneVideoJobWorker`, `SceneVideoWorkerHandler` and `SceneVideoSubmitCoordinator` stay unchanged. Initial/current submit ownership, provider polling, idempotency and billing contracts remain authoritative.

## Files

1. `TodoX.Web/Services/Render/RenderJobService.cs` — claim/quarantine only.
2. `TodoX.Web/Tests/SceneVideoQueueBlockedTests.cs` — executable unit/source regression tests.
3. `TodoX.Web/Tests/SceneVideoQueueBlockedPostgresTests.cs` — compiled, unexecuted integration tests sharing the isolated existing fixture.
4. `database/diagnostics/scene-video-queue-blocked-readonly.sql` — operator-only read-only collector, never executed here.
5. This change record.
6. `code-history/features/TODOX-RVIDEO-VIDEO-RERENDER.md` — feature lineage.

## Validation and limits

Commands below run from the new worktree root.

- Before implementation: `dotnet test TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --filter FullyQualifiedName~SceneVideoQueueBlockedTests --nologo -v minimal`: 1 FAIL, reproducing absent claim quarantine/savepoint boundary on main.
- Final: `dotnet test TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~SceneVideoQueueBlockedTests|FullyQualifiedName~RenderJobQueueFairnessTests|FullyQualifiedName~SceneVideoOperationDraftTests' --nologo -v quiet`: 34 PASS, zero FAIL/SKIP. The entire test project, including the new PostgreSQL tests, compiles. This filtered command invokes no database tests.
- An exploratory broader filter also selected existing `RVideoVideoHotfixTests.SceneVideoSubmitIntentAllowsCompletedTaskAndExplicitlyRejectedFallbackOnly`, which failed a source-string assertion in unchanged coordinator code. Its asserted string is also absent at the baseline; the coordinator/test were not modified. Full-suite green status is NOT claimed.
- `dotnet format TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj whitespace --verify-no-changes --no-restore --include TodoX.Web/Tests/SceneVideoQueueBlockedTests.cs TodoX.Web/Tests/SceneVideoQueueBlockedPostgresTests.cs --verbosity diagnostic`: actual process exit 0, no formatting changes. Workspace emitted existing package-advisory warnings on stderr. Production changed block formatted; unrelated formatting changes were removed. `git diff --check`: PASS. Build runs existing compiler/MudBlazor analyzers.
- `dotnet build TodoX.Web/TodoX.Web.csproj -c Release --nologo -v minimal`: PASS, 0 errors, 59 warnings including existing package advisories.
- `dotnet publish TodoX.Web/TodoX.Web.csproj -c Release -o D:/todoX/Dashboard-web/TodoXPortal/todoX-Dashboard-SaaS/artifacts/publish/todox-dashboard --nologo -v minimal`: PASS; verified DLL and IIS web.config. Candidate package only, NOT proof of production acceptance.
- PostgreSQL integration: **NOT RUN by explicit user instruction: "Chỉ viết test, không chạy PostgreSQL".** Tests are written for 13 superseded jobs before six valid no-head scene jobs, three concurrent lanes, exact trigger rejection despite stale predicate, atomic audit failure, unchanged historical rows/selection/billing, one intent/BeginSubmit and ambiguous replay refusal. These are NOT passing integration results. Real provider submission and billing idempotency remain unverified.

No migration or database update is required for the application change when the existing V1 operation and submit-intent schema matches source. Full schema equivalence must be checked before release. IMAGE, VOICE/AUDIO, POINT/BILLING, provider payloads/models/catalog/pricing, UI and final merge are untouched. YEScale work not involved; no MCP metadata query required.

## Safe reconciliation of 32 production rows

1. Operator captures deployed SHA, read-only function/trigger definitions, all 32 candidate identities, original enqueue provenance, own logical request/version, selected version, provider IDs, submit states and billing facts using the collector. Compare complete definitions with the checked-in V1 migration; do not reapply deployed migrations. Keep the snapshot for later comparison.
2. For the 13 NOT_ACTIVE_JOB rows: verify head ownership and generation independently for each row. Never reset/rebind them to the head or declare their provider outcome failed/no-task. After reviewed deployment, the application writes quarantine audit without changing historical job/version outcomes. Any old provider task/ambiguous intent receives separate operator reconciliation; do not resend.
3. For the 19 NO_OPERATION_HEAD rows: split genuine generation-zero/absent legacy enqueue from missing-head nonzero generation, malformed identity, conflicting logical/version owner, known-task polling and unresolved submits/results. Only fresh valid legacy operations without own prior submit evidence may proceed through unchanged writer/coordinator. Known-task work must use existing poll-only handling; ambiguous/no-ID outcomes stay held. Missing heads are not bulk backfilled to bypass guards. Manually repair nothing without per-row evidence and approval.
4. Specifically identify the six Project 205 scenes 911–916 in that read-only snapshot. They are not authorized for resend merely because the queue is blocked. Require valid source/prompt/catalog/payer ownership plus safe legacy/current operation evidence before observing normal worker progression.

## Deployment and production acceptance checklist — still pending

- Obtain the complete production definitions and compare them with source. Complete the mandatory isolated PostgreSQL integration run only if separately authorized; current instruction forbids it. Do not treat compiled tests as queue-proof.
- Review integration baseline: this candidate is based on main `1101084`, NOT the incompatible `ff693e3` rerender branch. The package therefore includes main's existing changes as well as this minimal source delta. Review the main-versus-deployed release delta before rollout; do not overwrite a live site with an unreviewed mixed version.
- After acceptance approval, use a maintenance window: stop/fence ALL worker replicas and old IIS process generations; back up current binaries/config/uploads; copy reviewed package while preserving environment settings, keys, secrets, uploads; restart with the same schema and worker configuration. No SQL mutation is part of deployment.
- Verify running DLL SHA/BuildCommit, source-matched trigger/function definitions and exactly three configured lanes. Verify 13 per-job quarantine audit markers with no historical outcome/selected/billing mutation. Raw queued counts may still include quarantined history.
- Observe distinct eligible claims behind those rows, attempt_count increment only for accepted fresh claims, no repeated 55000 loop on one row, and Project 205 eligible scenes reaching the unchanged coordinator/provider path. Require one durable intent and one provider task per logical request; no new charge for old quarantined jobs. Observe rather than force submission.
- Check ambiguous/known-task outcomes remain poll-only/manual, history/selection is preserved, and queue fairness/non-video jobs continue. Keep task/billing reconciliation separate from queue release.
- Rollback: stop all lanes before reverting application binaries; preserve schema, heads, intents and audit history. **Do not roll back to ff693e3 with generation triggers active**; it lacks the required claim filter and recreates this incident. Rollback build must preserve generation-aware claim exclusion/quarantine.

Status: code/build/publish candidate delivered; **P0 NOT COMPLETE / production deployment not approved by test evidence**, because required PostgreSQL execution and full production schema comparison remain outstanding. No production deployment, restart, data change or provider resend performed.
