# Timelapse Autosave Readiness Fix Report

## Scope

Aligned only the Timelapse create action `Bắt đầu tạo video` with autosaved-draft readiness. No `Tách scene` action exists in the Timelapse create/edit flow; the matching search results belong to RVideo and were not changed.

## Changes

- Added `CanStartWorkflow` in `TodoX.Web/Components/Pages/TimelapseJobCreate.razor`.
- Central readiness requires customer access, an enabled Timelapse service, completed price estimate, and `TimelapseRequestRules.Validate(_request, HasValidImage)` success.
- `SubmitDisabled` now derives from `CanStartWorkflow` plus active-click protection only; it has no draft ID or manual-save dependency.
- Autosave reuses `CanStartWorkflow`.
- The action awaits `EnsureDraftSavedAsync()` before `StartOrResumeAsync()`. Existing semaphore/create-or-update draft behavior is unchanged.
- Added regression coverage for valid readiness, missing profile, invalid scene count/video mode/ratio, missing image, invalid price guard, save-before-start order, existing-draft update, autosave gate, and single start invocation.

## Validation

- `dotnet test Tests\TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TimelapseUiRegressionTests" -p:UseSharedCompilation=false`: passed, 6 tests.
- `dotnet build TodoX.Web.csproj -c Release --no-restore`: passed, 0 errors; 48 existing warnings from generated Razor and unrelated RVideo files.
- `dotnet test Tests\TodoX.Web.Phase1B.Tests.csproj -c Release --no-restore -p:UseSharedCompilation=false`: failed outside this change, 7 of 140 tests: 2 missing RVideo SQL paths, 1 pre-existing Timelapse worker UI assertion, and 4 RVideo prompt-parser assertions.
- `dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore --include TodoX.Web\Components\Pages\TimelapseJobCreate.razor TodoX.Web\Tests\TimelapseUiRegressionTests.cs`: passed.
- `dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore`: failed on existing whitespace violations in unrelated files; none were changed.
- `git diff --check`: passed.
- `dotnet publish TodoX.Web.csproj -c Release --no-restore -o ..\artifacts\publish\todox-dashboard -p:UseSharedCompilation=false`: passed.

Publish output: `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`.

## Exclusions

No database migration or database update was required. Thumbnail/view/play code, 79AI, YEScale, and 7A were not changed.
