# P0: bounded recovery of confirmed provider tasks after local timeout

- Date: 2026-10-25, Asia/Saigon. Agent: Codex.
- FEATURE_ID: TODOX-RVIDEO-VIDEO-RERENDER. CHANGE_ID: TODOX-20261025-KNOWN-TASK-RECOVERY-001.
- Branch: main, continuing this session's authorized commit/push workflow; clean baseline c6275e19f5454b0248ae78802cdbf685f5fdf12f. Remote main matched baseline before finalization.
- Scope: existing scheduler/worker known-task recovery, mutual exclusion, local PostgreSQL/HTTP tests, handoff and publish. No production SQL, 79AI call, deployment, resubmit, new generation, task-ID rewrite or historical billing repair.

## Root cause and fix

SceneVideoWorkerHandler's ProviderRenderHardTimeoutEvaluator compares the original submitted/attempt timestamp. Processing after that clock expires becomes pending_reconciliation. ScheduleProviderPollAsync also refuses an expired poll window. Existing SceneVideoReconciliationWorker could discover the job but SchedulePersistentProviderPollAsync opened BeginSceneVideoWriterAsync without passing the transaction to its UPDATE and without committing. Npgsql executed the update in that connection's transaction and disposal rolled it back. A PostgreSQL reproduction showed two concurrent schedulers both returning true instead of one persisted queued result.

The persistent scheduler now executes/commits on the writer transaction, refuses already-queued/live-lease jobs, and proves exactly one matching current-generation job/version/CONFIRMED_PROVIDER_TASK intent before requeueing the same job for polling. Its input_json stores an independent bounded recovery count/window while retaining providerPollStartedAt and submitted_at. Defaults: 20 recovery polls, 1440 minutes; settings clamp to 1–200 and 1–10080 respectively. Normal scheduling inside recovery also observes the bound. Pending/terminal scene-job status updates release queue leases; Processing is never converted to provider failure solely by time.

SceneVideoSubmitCoordinator supplies a per-tenant/job nonblocking advisory poll lock, held by SceneVideoWorkerHandler through poll and completion. Duplicate recovery handlers defer before HTTP even after queue lease expiry. No database schema/function change. Existing confirmed-identity validation, generation fences, adapter.PollAsync with persisted video base, immutable storage and RVideoSceneVideoCompletionService remain authoritative. A diagnostic log preserves the underlying local error before an existing-task guard masks it.

## Changed files

- TodoX.Web/Services/Render/RenderJobService.cs.
- TodoX.Web/Services/VideoRender/SceneVideoSubmitCoordinator.cs.
- TodoX.Web/Services/VideoRender/SceneVideoWorkerHandler.cs.
- TodoX.Web/Services/VideoRender/VideoRenderRepository.cs.
- TodoX.Web/Tests/SceneVideoOperationDraftPostgresTests.cs (partial fixture reuse only).
- TodoX.Web/Tests/SceneVideoKnownTaskRecoveryPostgresTests.cs and SceneVideoKnownTaskRecoveryHttpFixture.cs.
- TodoX.Web/docs/rvideo-known-task-timeout-recovery-20261025.md.
- postman/mocks/rvideo-known-task/{config.yaml,default.js}; postman/simulations/rvideo-known-task/{rvideo-known-task.sim.yaml,rvideo-known-task.context.md}.
- This history and the feature index.

No Razor/UI, provider submit payload, pricing, billing implementation, SQL migration or production configuration changed.

## Verification

PostgreSQL 17.10 on 127.0.0.1:55437, fresh UUID-named empty databases only; TODOX_SUPERSEDE_TEST_DB explicitly supplied, no application connection fallback. Postman CLI 1.75 local simulation only, no cloud publication or live provider credentials.

- Start: `postman simulation run postman/simulations/rvideo-known-task/rvideo-known-task.sim.yaml --port 49196 --no-history --output ndjson`; TODOX_RECOVERY_SIM_URL=`http://127.0.0.1:49196/provider`.
- `dotnet test TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Scene07RetryControlsTests|FullyQualifiedName~SceneVideoSupersedeUiTests|FullyQualifiedName~SceneVideoOperationDraft|FullyQualifiedName~SceneImageVersionConcurrencyContractTests|FullyQualifiedName~RVideoRenderRerenderReadinessTests|FullyQualifiedName~RenderJobQueueFairnessTests|FullyQualifiedName~RVideoProviderPollingRegressionTests" --logger "trx;LogFileName=focused-final.trx" --results-directory artifacts/rvideo-timeout-recovery`: 201 total, 193 PASS, 8 FAIL, 0 skip. An isolated git archive of c6275e19 ran the polling suite: 101 total, 93 PASS, the identical 8 named failures. baseline-comparison.json confirms zero new failures. Unrelated baseline tests were not changed.
- Same command without the final RVideoProviderPollingRegressionTests filter, using `--no-build --no-restore`, logger targeted-final.trx: 100/100 PASS, zero failed/skipped. Includes 18 PostgreSQL-suite cases (16 local DB scenarios, 2 connection rejection cases).
- Eight-job test runs real job scheduling/claiming, handler, Ai79TaskClient/RVideo79AiVideoService/adapter poll, guarded completion, version/job persistence and wallet usage logging. Local HTTP metrics: 16 polls, eight downloads, zero submits. All eight original IDs/confirmed intents/generation heads remain intact; completed selected versions and scenes carry media identities. ReserveAsync throws if called. Concurrent scheduling, poll mutex, live lease, ambiguous intent, budget and window refusals pass.
- Limits: credential/catalog/lifecycle boundaries and media storage are test doubles; downloads use real local HTTP and write fixture files. Production SSRF protection is unchanged. Billing completion is counted; production customer debit idempotency and browser rendering are not claimed as E2E. No FFmpeg or live provider test.
- `dotnet build TodoX.Web/TodoX.Web.csproj -c Release --no-restore`: PASS, zero errors, 52 existing warnings. Build includes Razor/MudBlazor analyzers. `git diff --check` and strict UTF-8 checks required before commit.
- Publish: `dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o D:\todoX\Dashboard-web\TodoXPortal\deployed-todoX-Dashboard-SaaS\known-task-recovery-20261025`. Isolated sibling output follows the codec publish rule and preserves earlier packages. Republish after commit for accurate BuildCommit; verify core files and manifest. Publish is not deploy.

## Operator handoff

No required SQL migration or production status update. After matching package deployment with existing RenderQueue enabled, the existing scan discovers due current confirmed jobs, queues only same-task polls and completes through existing storage/billing/generation guards. Do not create a new render/Supersede or reset IDs/counts. Exhausted/ambiguous tasks remain pending for investigation. See the handoff doc for events and exact constraints. The production eight jobs were not modified by Codex.

Commit SHA: see Git commit containing this history entry. Stage only scoped source/tests/mock/docs/history; no artifacts, secrets, logs or publish output. Actual commit/push and publish results are reported after verification.
