# Timelapse UI Thumbnail and Draft Autosave Report

Date: 2026-08-25

## Scope

Implemented only the requested Timelapse UI media preview and draft lifecycle changes.
No database migration or direct database change was created or executed.

## Job Auto-Save

- A draft is created automatically after the required service, profile, scene count, video mode, ratio, valid price estimate, and image upload are available.
- The created job UUID is retained in `_draftJobId`.
- Later meaningful form changes use a 700 ms debounce and update the same draft through `UpdateDraftAsync`.
- A `SemaphoreSlim` prevents overlapping saves. The manual primary action also calls `EnsureDraftSavedAsync`, so it cannot create a second draft before starting the workflow.

## Split / Start Readiness

The existing `TimelapseRequestRules.Validate` remains the source of truth. Required fields are:

- enabled Timelapse service and valid service selection
- enabled profile code
- scene count: 3, 4, 5, or 6
- supported video mode: `fast` or `professional`
- supported ratio: `16_9` or `9_16`
- valid uploaded original/reference image
- successful price estimate

The workflow action no longer depends on a separate manual save. It ensures the current draft is persisted first, then starts/resumes the same job.

## Image Thumbnail

Completed image cards use the existing `PublicUrl` and render the real image with preserved aspect ratio. A centered eye/visibility affordance is clickable and opens the existing `ReferenceImageLightboxDialog`.

## Video Thumbnail

Completed video cards use the existing `PublicUrl`. The shared renderer uses the video itself with `muted`, `preload="metadata"`, `playsinline`, and no autoplay. A centered play affordance opens the existing `LandingIndustryVideoPreviewDialog`. Rendering and failed states keep their existing loading/error UI.

## Files Changed

- `Components/Pages/TimelapseJobCreate.razor`
- `Components/Pages/TimelapseJobDetail.razor`
- `Components/Shared/RenderMediaFrame.razor`
- `Components/Shared/RenderMediaFrame.razor.css`
- `Tests/TimelapseUiRegressionTests.cs`
- `docs/reports/20260825_timelapse-ui-autosave-thumbnail-report.md`

## Tests and Validation

- `dotnet build TodoX.Web.csproj --no-restore -c Release`: passed, 0 errors.
- `dotnet test TodoX.Web.csproj --no-restore -c Release --filter "FullyQualifiedName~TimelapseUiRegressionTests|FullyQualifiedName~TimelapseWorkerClaimRegressionTests|FullyQualifiedName~RenderVideoJobsLayoutTests"`: passed.
- `git diff --check`: passed.
- `dotnet format TodoX.Web.csproj --verify-no-changes --no-restore`: not passed because of pre-existing whitespace findings in unrelated files; no unrelated files were changed.
- `dotnet publish TodoX.Web.csproj -c Release --no-restore -o D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`: passed.

## Regression Check

- Existing Render lại actions remain unchanged.
- Existing Timelapse image/video statuses and processing/error states remain in place.
- Existing 7A behavior was not modified.
- No YEScale changes.
- No 79AI API changes.
- No migration or database update is required.

## Publish

Output directory:

`D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

Result: passed.
