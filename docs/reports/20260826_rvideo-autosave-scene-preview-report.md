# RVIDEO Autosave and Scene Preview Work Report

Date: 2026-08-26
Branch: `integration/rdance-on-construction-video-core`

## Root cause

`RenderVideoJobs.razor` disabled `Tạo / phân tách scene` while `_projectId` was null. A new RVIDEO configuration therefore required the user to click manual save first. The old fresh-project branch also used the legacy `VideoRepo.CreateProjectAsync`, so it did not assign the Core RVIDEO `_jobId` used by the draft lifecycle.

## Save flow

The button now validates authentication, service identity, prompt JSON, aspect ratio, and resolution. It calls `EnsureDraftSavedAsync`, which reuses `PersistCurrentJobAsync`:

1. New draft: call `RVideoJobs.CreateDraftAsync`, assign `_jobId` and `_projectId`, persist settings through the existing Core RVIDEO service, reload the project, then continue.
2. Existing draft: update the same project, settings, and Core job through `VideoRepo.UpdateProjectDraftAsync`, `RVideoSettings.SaveAsync`, and `RVideoJobs.UpdateAsync`.
3. Only after save succeeds does the code call `VideoRepo.ReplaceScenesAsync` and reload scenes.

Save failures stop scene splitting and write the existing debug log entry `rvideo_draft_save_failed`. Split failures keep the saved draft for retry.

`SaveJobAsync` remains available as an optional explicit save action. Persistence is serialized with `_draftSaveGate`, and the create branch only runs when both `_jobId` and `_projectId` are null.

## Scene layout

`RenderVideoJobs.razor.css` adds `.scene-card-grid` with two equal columns on desktop, one column below the tablet breakpoint, and one column on mobile. Each scene remains its own card; image and video media remain inside that card.

## Video preview

Rendered scene videos now pass `OnClick="OpenSceneVideoPreview(scene)"`. The existing `LandingIndustryVideoPreviewDialog` accepts `AutoPlay`, renders the video with controls and autoplay requested, and calls `todoXVideoPreview.play` after mount. The helper resets `currentTime` to zero and handles browser autoplay rejection silently. `RenderMediaFrame` thumbnails remain without autoplay.

## Changed files

- `TodoX.Web/Components/Pages/RenderVideoJobs.razor`
- `TodoX.Web/Components/Pages/RenderVideoJobs.razor.css`
- `TodoX.Web/Components/Dialogs/LandingIndustryVideoPreviewDialog.razor`
- `TodoX.Web/wwwroot/js/todox-render-log.js`
- `TodoX.Web.Tests/RVideoAutosaveWorkflowTests.cs`
- `docs/reports/20260826_rvideo-autosave-scene-preview-report.md`

## Validation

- `dotnet build TodoX.Dashboard.sln -c Release --no-restore`: passed, 0 errors.
- Focused RVIDEO tests: passed, 4/4.
- `dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release --no-restore`: passed, 768/768.
- `git diff --check`: passed; only Git line-ending normalization notices were reported.
- `dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard`: passed.
- `dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore`: not clean because of pre-existing whitespace diagnostics across unrelated files; no unrelated formatting was applied.

Publish output: `artifacts/publish/todox-dashboard`.
