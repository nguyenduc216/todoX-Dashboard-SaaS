# P0: Legacy/V2 image rerender FK-safe enqueue

- Date: 2026-10-25, Asia/Saigon. Agent: Codex.
- FEATURE_ID: TODOX-RVIDEO-VIDEO-RERENDER.
- CHANGE_ID: TODOX-20261025-IMAGE-RERENDER-FK-001.
- Worktree/branch: wt-scene05-main-release / main, continuing the explicitly authorized main workflow. Clean baseline c2b8427f74e7bc7acf6b29d000c943081a44f741; origin/main matched baseline before changes.
- Scope: Legacy image rerender, shared atomic image enqueue also used by V2, FK/concurrency tests and release package. No production SQL/data edits, provider call or IIS deployment.

## Exact root cause and lineage

At baseline RenderVideoJobs.razor:RerenderSceneImageAsync created operationId and passed it as SceneImageVersionCreateRequest.RenderJobId to CreateQueuedImageVersionAsync before RenderJobs.EnqueueAsync persisted any job. The service writes that value directly into scene_image_versions.render_job_id. PostgreSQL therefore rejects it with 23503/fk_scene_image_render_job. It happens before FindImageRenderJobAsync / TryActivateImageRenderJobAsync can run. A real PostgreSQL fixture reproduces the exact named FK failure with the old argument pattern.

Git blame places the operationId-as-job caller in ccbf010a, while the later draft/bind/activate checks were introduced by 108822aa. The earlier hotfix protected dispatch after enqueue but left the pre-enqueue invalid FK insert in place. V2 already used CreateQueuedImageVersionWithJobAsync and did not hit that specific insert; its placeholder request value is now NULL as well, keeping operationId only in billing/parent identity fields.

## Implementation / reuse map

- Legacy confirmation, prompt/reference persistence, dirty-save, cost, model selection, per-scene busy guard, billing reference and polling are retained. The enqueue portion now delegates to the same CreateQueuedImageVersionWithJobAsync transaction used by V2 instead of independently committing version creation, scene status change, job creation and activation.
- The shared service persists the actual Draft job first, inserts its version with a NULL FK, binds the FK to the actual existing job UUID, then activates that exact job and commits once. Generated imageVersionId is still written into canonical worker input. All intermediate states are uncommitted and unclaimable.
- Replays verify scene/project and recover the bound actual job in the same tenant; mismatched job-draft project/user/customer is rejected. The scene lock preserves active-slot exclusion and version numbering. Same-key concurrent requests replay one job; different-key concurrent requests allow one active operation.
- Legacy's configured image routing provider/model and not_required point status/zero estimate are explicitly preserved in the common job INSERT. UserRerender billing/reference IDs, prompt snapshots and selected image V1 remain unchanged until worker completion. No new billing behavior.
- An enqueue failure rolls back both job and version; the previous completed image and scene selection are untouched. No speculative scene reset occurs before successful enqueue.

## Changed files

1. TodoX.Web/Components/Pages/RenderVideoJobs.razor — only RerenderSceneImageAsync enqueue portion.
2. TodoX.Web/Services/VideoRender/RVideoSceneImageRenderAction.cs — request job ID NULL; billing operation ID retained.
3. TodoX.Web/Services/VideoRender/SceneMediaVersioningService.cs — shared Draft/version/bind/activate order and replay/identity guards.
4. TodoX.Web/Tests/SceneImageVersionConcurrencyContractTests.cs — Legacy delegation and Draft-first contract checks.
5. TodoX.Web/Tests/SceneImageRerenderFkPostgresTests.cs — Project 195 FK/rollback/concurrency regression.
6. This history and code-history/features/TODOX-RVIDEO-VIDEO-RERENDER.md.

Only the Legacy page was edited; V2 markup is unchanged and still reuses its existing action. Video Supersede, provider polling/recovery, generation fences, provider payloads, audio and production data/configuration were not modified.

## Verification

- PostgreSQL 17.10 on 127.0.0.1:55437; fresh empty UUID-named local databases, explicit TODOX_SUPERSEDE_TEST_DB, no production fallback. Project 195 fixtures cover scene index 5 and scene 7/id 864; the index-5 fixture ID is test data, not a new production identity claim.
- Exact old caller reproduction: CreateQueuedImageVersionAsync with a random unpersisted operation UUID raises SqlState 23503 and ConstraintName fk_scene_image_render_job. No orphan version remains.
- Tests install local-only assertion triggers requiring the Draft row to exist before image INSERT and the real FK bind before activation. Injected activation failure rolls back job/version and retains the complete selected-V1 row/scene pointer. Wrong project/user/customer identity is guarded; a different tenant cannot allocate the scene. Concurrent same-key replay and different-key active-slot exclusion are exercised. Routing, point metadata and billing reference are verified.
- `dotnet test TodoX.Web/Tests/TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "(FullyQualifiedName~Project195ImageRerender|FullyQualifiedName~Scene07RetryControlsTests|FullyQualifiedName~SceneVideoSupersedeUiTests|FullyQualifiedName~SceneVideoOperationDraft|FullyQualifiedName~SceneImageVersionConcurrencyContractTests|FullyQualifiedName~RVideoRenderRerenderReadinessTests|FullyQualifiedName~RenderJobQueueFairnessTests)&FullyQualifiedName!~Project196EightConfirmedTasks" --logger "trx;LogFileName=focused-final.trx" --results-directory artifacts/rvideo-image-fk`: PASS 101/101, zero failed/skipped. Includes 19 PostgreSQL-suite cases (17 local DB scenarios and two unsafe-connection rejection cases), existing V2 double-click/action checks and shared image completion/initial-video/final-merge regression. The unrelated HTTP provider recovery fixture is explicitly outside this task's filter; no provider simulation is needed for enqueue/FK work.
- `dotnet build TodoX.Web/TodoX.Web.csproj -c Release --no-restore`: PASS, zero errors, 52 existing warnings; Razor/MudBlazor analyzers execute in build. Strict UTF-8/added-line encoding and git diff --check before commit. No browser/provider E2E claim.
- Publish: `dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o D:\todoX\Dashboard-web\TodoXPortal\deployed-todoX-Dashboard-SaaS\image-rerender-fk-20261025`. Isolated sibling output preserves earlier packages. Republish after commit and verify BuildCommit/package manifest. Publish is local packaging, not deployment.

## Operator handoff

No additional SQL migration or manual production data repair is needed for this source fix. After operator deployment, the authorized user can edit/correct the image prompt for Project 195 Scene 7/id 864 and confirm the existing Tạo lại ảnh action. It creates one new immutable version with a real job FK; completed selected V1 remains visible until a new image succeeds. This is an explicitly user-requested provider-affecting render later, not something Codex performed. The old failed enqueue events remain history. Do not insert a fake job UUID or alter old selected rows manually.

Commit SHA: see Git commit containing this history entry. Stage only the seven scoped files; commit/push origin/main after checks, report actual SHA and publish folder. No publish/artifact/log/secret files are staged.
