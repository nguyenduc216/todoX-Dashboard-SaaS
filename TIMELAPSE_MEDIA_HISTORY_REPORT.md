# Timelapse Media History Report

Branch: `integration/rdance-on-construction-video-core`

Scope: Timelapse media history only.

## What changed
- Added a visible history action for scene video cards in `TodoX.Web/Components/Pages/TimelapseJobDetail.razor`.
- Reused `MediaHistoryDialog` and `MediaHistoryItem` for Timelapse history selection.
- Added clip-scoped history queries in `TodoX.Web/Services/Timelapse/TimelapseWorkflowService.cs`.
- Added Timelapse job service wrappers in `TodoX.Web/Services/Timelapse/TimelapseJobService.cs`.
- Extended `TimelapseHistoryItem` with provider metadata in `TodoX.Web/Models/Timelapse/TimelapseModels.cs`.
- Added regression coverage in `TodoX.Web/Tests/MediaHistorySelectionRegressionTests.cs`.
- Added a missing test stub in `TodoX.Web.Tests/DanceSellRenderHandlerTests.cs` so the solution build could complete.

## Verified architecture
- Scene cards are rendered from `TodoX.Web/Components/Pages/TimelapseJobDetail.razor`.
- Video attempt data comes from `TimelapseVideoClip.Attempt`.
- Historical media is stored in Timelapse version tables, so no schema migration was required.
- Current scene video selection is updated through `TimelapseWorkflowService.SelectHistoryAsync`.

## Validation
Command: `dotnet build TodoX.Dashboard.sln -c Release --no-restore`
- Result: succeeded with 0 errors.
- Warnings: pre-existing generated Razor and `RenderVideoJobs.razor` warnings remained.

Command: `dotnet test TodoX.Web\Tests\TodoX.Web.Phase1B.Tests.csproj -c Release --filter MediaHistorySelectionRegressionTests`
- Result: 4 passed, 0 failed.

Command: `dotnet format TodoX.Dashboard.sln --verify-no-changes`
- Result: failed on many pre-existing whitespace issues outside the Timelapse scope.

Command: `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`
- Result: succeeded.
- Output: `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

## Notes
- No database migration was created or applied.
- The Timelapse history UI now shows failed attempts and lets the user reselect an older completed clip without rerendering.
