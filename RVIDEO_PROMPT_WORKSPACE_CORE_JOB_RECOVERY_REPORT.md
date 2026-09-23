# RVIDEO Prompt Workspace Core Job Recovery Report

## 1. Scope

Fixed the RVIDEO Prompt Workspace creation path so new prompt-workspace projects use the canonical RVIDEO Core Job flow. The change does not modify image generation, video generation, voice/TTS, providers, workers, queue, ffmpeg, finalizer, billing, point calculation, or trusted payer validation.

## 2. Root Cause

`RenderVideoJobs.razor` had two project-creation paths:

- Normal RVIDEO draft save called `RVideoJobService.CreateDraftAsync`.
- Prompt Assistant Generate/Import called `EnsurePromptWorkspaceProjectAsync`, which called `VideoRenderRepository.CreateProjectAsync` directly.

`VideoRenderRepository.CreateProjectAsync` inserts only `video_render.video_projects`; it does not create a row in `render.render_jobs` and does not populate `core_job_id`. Therefore Prompt Workspace projects could be persisted with `core_job_id IS NULL`, and the existing `RVideoTrustedPayerContextService` correctly rejected them with `rvideo_video_payer_context_mismatch`.

## 3. Correct Entry Point

The canonical entry point is `RVideoJobService.CreateDraftAsync` in:

`TodoX.Web/Services/VideoRender/RVideoJobService.cs`

It creates:

1. `render.render_jobs` with the configured RVIDEO service and operation type.
2. `video_render.video_projects` with `core_job_id = @jobId`.
3. `video_render.rvideo_job_settings`.

These writes are inside one database transaction and use the same tenant, customer, and user values.

## 4. Code Changes

### Prompt Workspace

`EnsurePromptWorkspaceProjectAsync` now calls `RVideoJobService.CreateDraftAsync` instead of `VideoRenderRepository.CreateProjectAsync`. It stores both the returned Core Job ID and project ID, and passes the existing RVIDEO settings request.

### Idempotency

`RVideoJobCreateRequest` now supports an optional `LogicalRequestId`. Prompt Workspace supplies a component-level request key. The service uses an advisory transaction lock and reuses an existing matching Core Job/project pair before creating a new one. Existing RVIDEO callers that do not provide this optional key retain their previous behavior.

### Tests

Added source regression tests for:

- Atomic Core Job/project creation and ownership fields.
- `core_job_id` linkage and `operation_type` flow.
- Logical request reuse before generating a second Core Job.
- Prompt Workspace using the canonical RVIDEO creation service.

## 5. Database Diagnostic

Created read-only diagnostic SQL:

`TodoX.Web/database/rvideo/diagnose_prompt_workspace_core_job_104_108.sql`

It reports project/core-job linkage, ownership, operation type, timing/context, scene counts, image counts, video counts, and candidate jobs for manual review. It contains no `UPDATE`, `DELETE`, fake UUID, or automatic recovery.

## 6. Projects 104-108 Recovery Status

No production recovery was executed. The repository session did not have a live database result for projects 104-108, and the task explicitly forbids approximate matching or guessed UUIDs.

Project 108's four scenes and four images were not changed, deleted, regenerated, or reset.

Before any recovery update, run the diagnostic SQL and verify an exact Core Job match for tenant, customer, user, operation type, job type, and project timing/context. Only then may a separately reviewed, conditional recovery statement be prepared. If no exact Core Job exists, recovery must go through the application service with verified ownership rather than direct SQL job creation.

Projects 104-107 were not deleted.

## 7. Trusted Payer Validation

`RVideoTrustedPayerContextService` was not modified or bypassed. It continues to require a non-null Core Job, `operation_type = RVIDEO`, and matching customer/user ownership. The fix repairs the creation relationship before this validation; it does not weaken validation.

## 8. Validation

- `dotnet test TodoX.Web\\TodoX.Web.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RenderJobCoreServiceClaimRegressionTests|FullyQualifiedName~RenderVideoJobsLayoutTests|FullyQualifiedName~ServicePromptAssistantTests"`: **57 passed, 0 failed**.
- `dotnet build TodoX.Dashboard.sln -c Release --no-restore -p:UseSharedCompilation=false /m:1`: **succeeded, 0 errors**.
- `dotnet publish TodoX.Web\\TodoX.Web.csproj -c Release --no-restore -p:UseSharedCompilation=false /m:1 -o D:\\todoX\\Dashboard-web\\TodoXPortal\\todoX-Dashboard-SaaS\\artifacts\\publish\\todox-dashboard`: **succeeded**.

Existing unrelated full-suite failures were observed in static image billing source expectations and CSS layout expectations; they are outside this change and were not modified.

## 9. Changed Files

- `TodoX.Web/Components/Pages/RenderVideoJobs.razor`
- `TodoX.Web/Services/VideoRender/RVideoJobService.cs`
- `TodoX.Web.Tests/RenderVideoJobsLayoutTests.cs`
- `TodoX.Web.Tests/RenderJobCoreServiceClaimRegressionTests.cs`
- `TodoX.Web/database/rvideo/diagnose_prompt_workspace_core_job_104_108.sql`
- `RVIDEO_PROMPT_WORKSPACE_CORE_JOB_RECOVERY_REPORT.md`

## 10. Remaining Risk

The application creation path is fixed and verified by source regression tests, but production projects 104-108 remain pending evidence-based recovery. The diagnostic SQL must be run against the target database before deciding whether any project can be safely relinked.

## 11. Commit

Commit SHA: 6f3d830
