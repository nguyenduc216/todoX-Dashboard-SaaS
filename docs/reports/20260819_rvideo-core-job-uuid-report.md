# RVIDEO Core Job UUID Report

Date: 2026-08-19

## Git

- Starting SHA: `0dd48eb76383f781210b6dea24c13e91fdf26822`
- RVIDEO implementation SHA: `8ab53a66082742a67ac900169006fc07a5a403bc`
- Implementation commit: `feat(rvideo): standardize customer flow on core job uuid`
- The implementation commit was pushed to `origin/integration/rdance-on-construction-video-core`.

## Changed Files

- `TodoX.Web/Components/Pages/MyJobs.razor`
- `TodoX.Web/Components/Pages/RVideoJobCreate.razor`
- `TodoX.Web/Components/Pages/RVideoJobDetail.razor`
- `TodoX.Web/Components/Pages/RenderVideoJobs.razor`
- `TodoX.Web/Models/VideoRenderModels.cs`
- `TodoX.Web/Program.cs`
- `TodoX.Web/Services/VideoRender/RVideoEndpoints.cs`
- `TodoX.Web/Services/VideoRender/RVideoJobService.cs`
- `TodoX.Web/Services/VideoRender/RVideoLifecycleWorker.cs`
- `TodoX.Web/Services/VideoRender/VideoRenderRepository.cs`
- `TodoX.Web/Tests/RVideoFoundationTests.cs`
- `database/migrations/20260819_rvideo_core_job_link.sql`
- `docs/support/rvideo-job-diagnostic.sql`
- `docs/reports/20260819_rvideo-core-job-uuid-report.md`

## Architecture

- The authoritative Core TodoX Job table is `render.render_jobs`.
- The public RVIDEO job UUID is `render.render_jobs.id` (`uuid`).
- New RVIDEO projects are linked through `video_render.video_projects.core_job_id`, which references `render.render_jobs.id`.
- `video_render.video_projects.id` remains the internal RVIDEO project identity.
- Internal render worker records continue to use `render.render_jobs.input_json.projectId`; no worker/provider contract was renamed.

## Create Flow

Before: RVIDEO service -> `video_render.video_projects`.

After: RVIDEO service -> Core Job UUID -> linked RVIDEO project -> RVIDEO settings.

The first customer save creates all three records in one PostgreSQL transaction. The Core Job defaults to `draft`, `info`, `not_required`, and zero estimated/charged points. Save does not enqueue rendering or reserve/charge points.

The input snapshot retains RVIDEO engine, service ID/code, title, prompt, aspect ratio, resolution, execution mode, character, voice, music, and scene timing. The project ID is added internally to the snapshot after project creation.

## Routes And UI

- Create route remains `/jobs/rvideo/new`.
- First save redirects to `/jobs/rvideo/{jobUuid}`.
- Added detail route `/jobs/rvideo/{JobId:guid}`.
- The RVIDEO header presents `Job ID: {jobUuid}` rather than the project ID.
- The create wrapper passes both `ServiceId` and `ServiceCode`.
- The customer scene action requires an already-saved native RVIDEO job; it does not create a Core Job.
- Existing `/render-job?projectId=...` behavior remains available for legacy/admin RVIDEO projects.

## My Jobs And Lifecycle

- RVIDEO Core Jobs appear in the central My Jobs list and link to `/jobs/rvideo/{jobUuid}`.
- The lifecycle worker resolves the linked Core Job and synchronizes draft/processing/completed/failed state from the RVIDEO project lifecycle.
- Job lookups require the current tenant and customer scope; an unknown or foreign job returns no RVIDEO job view.

## Database

- Added additive migration: `database/migrations/20260819_rvideo_core_job_link.sql`.
- It adds nullable `video_render.video_projects.core_job_id uuid`, FK `fk_video_projects_core_job`, a partial unique index, and a tenant/core-job lookup index.
- The migration is idempotent and leaves legacy projects valid with `core_job_id IS NULL`.
- SQL was not executed. No existing migration or live database schema was modified.

## Support SQL

- Added `docs/support/rvideo-job-diagnostic.sql`.
- Its only input is `:job_uuid`.
- It returns the Core Job, linked project, scenes, internal render jobs, project events, and Core Job events.

## Validation

Passed:

```powershell
dotnet format ..\TodoX.Dashboard.sln --verify-no-changes --no-restore --include TodoX.Web\Components\Pages\MyJobs.razor TodoX.Web\Components\Pages\RVideoJobCreate.razor TodoX.Web\Components\Pages\RVideoJobDetail.razor TodoX.Web\Components\Pages\RenderVideoJobs.razor TodoX.Web\Models\VideoRenderModels.cs TodoX.Web\Program.cs TodoX.Web\Services\VideoRender\RVideoEndpoints.cs TodoX.Web\Services\VideoRender\RVideoJobService.cs TodoX.Web\Services\VideoRender\RVideoLifecycleWorker.cs TodoX.Web\Services\VideoRender\VideoRenderRepository.cs TodoX.Web\Tests\RVideoFoundationTests.cs
```

Result: passed for the changed application and test files.

```powershell
dotnet test Tests\TodoX.Web.Phase1B.Tests.csproj --no-restore --logger "console;verbosity=minimal"
```

Result: 45 passed, 0 failed.

```powershell
dotnet build TodoX.Web.csproj --no-restore
```

Result: succeeded with 0 warnings and 0 errors.

```powershell
git diff --check
```

Result: passed.

```powershell
dotnet publish TodoX.Web.csproj --no-restore -c Release -o ..\artifacts\publish\todox-dashboard
```

Result: succeeded. Output directory: `artifacts\publish\todox-dashboard`.

## Compatibility And Follow-Up Verification

- Scope review preserves RDance, Construction Timelapse, legacy RVIDEO, Telegram/n8n RVIDEO identifiers, billing behavior, and provider routing.
- No migration was applied and no live provider or database integration test was run in this workspace.
- Before deployment, apply the migration through the normal database release process and manually verify: create RVIDEO job, refresh `/jobs/rvideo/{jobUuid}`, open it from My Jobs, start image generation, and confirm the same UUID through the final result.
- Phase 2 work was not started.
