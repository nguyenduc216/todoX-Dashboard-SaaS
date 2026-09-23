# RVIDEO Prompt Workspace Orphan Recovery Report

## Root Cause

The earlier Prompt Workspace path inserted `video_render.video_projects` directly through `VideoRenderRepository.CreateProjectAsync`. That path did not create a canonical `render.render_jobs` Core Job or populate `video_projects.core_job_id`. Trusted payer validation correctly rejected the resulting orphan project.

Production diagnostics confirmed projects 104-108 have `core_job_id IS NULL`; projects 104-107 have no scenes/images/videos, and project 108 has four scenes and four static images. The candidate Core Job query returned no candidates, so there is no existing Core Job that can be safely relinked.

## Recovery Architecture

Added an explicit application operation:

`POST /api/rvideo/projects/{projectId}/recover-orphan`

Request body:

```json
{"serviceId":"<configured-rvideo-service-id>","serviceCode":"<configured-rvideo-service-code>"}
```

The operation:

1. Requires an authenticated customer.
2. Loads and locks the existing project row with `FOR UPDATE`.
3. Verifies tenant, customer, and user ownership.
4. Requires the project to be a `Prompt workspace` and orphaned.
5. Returns the existing Core Job if `core_job_id` is already populated.
6. Otherwise creates exactly one draft `core_service` job with `operation_type = RVIDEO`.
7. Updates only `video_projects.core_job_id` and `updated_at`.
8. Commits atomically.

The operation is not called by startup, migration, worker, render queue, or any automatic deployment hook.

## Transaction and Idempotency

Recovery uses a PostgreSQL advisory transaction lock keyed by tenant and project ID, then re-checks `core_job_id` while holding the project row lock. Concurrent requests therefore cannot create two recovery jobs for the same project. The linking update also requires `core_job_id IS NULL`.

Failed operations roll back the Core Job and project linkage together.

## Ownership and Core Job Contract

The recovery job uses the authenticated project's tenant/customer/user values, configured RVIDEO service ID/code, `job_type = core_service`, `operation_type = RVIDEO`, `status = draft`, `current_step = info`, zero progress, zero point cost, and `point_status = not_required`. It does not queue or run the job.

`RVideoTrustedPayerContextService` was not changed or bypassed.

## Project 108 Preservation

Project 108 was not modified in production during this task. No scenes, scene IDs, image URLs, prompts, durations, image metadata, or video fields are touched by the recovery operation. The intended post-recovery state is the same project ID with its existing four scenes and four images plus a valid Core Job link.

Projects 104-107 were not deleted and were not automatically recovered.

## Protected Areas

No changes were made to image generation, video generation, voice/TTS, provider routing, render queue, workers, ffmpeg, finalizer, billing, points, or trusted payer validation. No migration or schema change was created.

## Changed Files

- `TodoX.Web/Services/VideoRender/RVideoJobService.cs`
- `TodoX.Web/Services/VideoRender/RVideoEndpoints.cs`
- `TodoX.Web.Tests/RenderJobCoreServiceClaimRegressionTests.cs`
- `RVIDEO_PROMPT_WORKSPACE_ORPHAN_RECOVERY_REPORT.md`

## Tests

- Targeted `RenderJobCoreServiceClaimRegressionTests`: **10 passed, 0 failed**.
- The recovery path now also creates the missing `video_render.rvideo_job_settings` row in the same transaction, using `ON CONFLICT (project_id) DO NOTHING` so existing settings are preserved.
- Full `TodoX.Web.Tests` run: **989 passed, 22 failed**. The failures are pre-existing regressions in unrelated Core Platform, RDance, provider, billing, and UI tests; no recovery test failed.
- `git diff --check`: passed.

## Build and Publish

- `dotnet build TodoX.Dashboard.sln -c Release --no-restore -p:UseSharedCompilation=false /m:1`: **succeeded, 0 errors** (46 existing generated/legacy warnings).
- `dotnet publish TodoX.Web.csproj -c Release --no-restore -p:UseSharedCompilation=false /m:1 -o D:\\todoX\\Dashboard-web\\TodoXPortal\\todoX-Dashboard-SaaS\\artifacts\\publish\\todox-dashboard`: **succeeded**.

## Production Recovery Status

Production recovery for projects 104-108 was **not executed**. The explicit operation is available for a reviewed, project-specific call after confirming the configured RVIDEO service ID/code and authenticated owner. This report does not claim that project 108 has already passed trusted payer validation in production.

## Commit

Commit SHA: recorded by the final Git commit for this change.
