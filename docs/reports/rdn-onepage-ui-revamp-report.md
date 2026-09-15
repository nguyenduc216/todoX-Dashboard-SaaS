## Repository
TodoX Dashboard SaaS

## Branch
feature/rdn-onepage-ui-revamp

## Base Commit
4f2e632

## New Commit SHA
Not committed yet

## Files Changed
- TodoX.Web/Components/Pages/RDanceJobDetail.razor
- TodoX.Web/appsettings.json
- TodoX.Web.Tests/RDanceFashionDemoPageTests.cs
- TodoX.Web.Tests/DanceSellRenderHandlerTests.cs

## Current RDN Architecture
RDance job detail stays on the existing route and data model. The page now has a feature-flagged one-page branch, while the legacy tab UI remains available as the fallback.

## UI Changes
- Added top action buttons for browser, save, and video download.
- Added a one-page workflow layout with title, reference video, reference image, result video, job status, and point summary.
- Added stage-based status chips and progress.
- Kept the existing upload and render actions tied to the current workflow data.

## Preserved Backend Logic
- Job creation
- Provider submission
- Polling and callback flow
- Render status handling
- Point calculation
- Wallet charging
- Database mapping

## Feature Flag
`Features:RdnOnePageUiEnabled` in `TodoX.Web/appsettings.json`

## Tests
- `dotnet test TodoX.Web.Tests\\TodoX.Web.Tests.csproj -c Release --filter RDanceFashionDemoPageTests` passed
- Full `dotnet build TodoX.Dashboard.sln -c Release /p:UseSharedCompilation=false` passed
- `git diff --check` passed

## Build Result
Passed with warnings only

## Publish Result
`dotnet publish TodoX.Web\\TodoX.Web.csproj -c Release --no-restore -o artifacts\\publish\\todox-dashboard`
Result: passed

## Rollback Instructions
Restore branch `feature/rdn-onepage-ui-revamp` to tag `rdn-before-ui-revamp-v1`.
