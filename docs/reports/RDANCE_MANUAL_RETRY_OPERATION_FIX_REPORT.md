# RDance Manual Retry Operation Fix Report

## Git
Branch: integration/rdance-on-construction-video-core
Base SHA: a49c4b5f89ccb3d3bbfa2712e42b3c7177306809
Final SHA: af83613b0ee4cf7308645447009ab66ed93dcc91 (implementation commit; report metadata follow-up is separate)
Push: pushed to origin/integration/rdance-on-construction-video-core

## Root cause
Old operation reused: YES
Retry cap inherited: YES
Why provider task was not created: manual retry flowed through the old render-job retry path, so the exhausted motion operation and its retry budget were reused instead of creating a fresh motion attempt.

## Initial motion attempt
Operation creation method: DanceSellPhase2Service.QueueRenderAsync
attempt_no: 1
render_job link: render.render_jobs.input_json.operationId

## Manual retry
New operation created: YES
New attempt_no: 2
parent_operation_id: previous motion operation id
New render_job operationId: new operation id
Retry budget reset: YES

## Automatic retry
Same operation preserved: YES
Retry cap still enforced: YES

## Provider assets
Reference upload reuse: YES, only when verified and media identity matches
Motion upload reuse: YES, only when verified and media identity matches
Media identity validation: YES

## Provider submit
Provider: 79AI
Model: kling_video_motion_3
Ratio: default
New provider_task_id possible: YES

## Billing
Old failed attempt charged: NO
New estimate created: YES
Double-charge risk: NO

## Database
SQL update required: NO
Schema migration required: NO

## Settings
appsettings.json update required: NO
Other settings update required: NO
Restart required: NO

## Provider safety
79AI touched/used: YES
YEScale touched: NO
YEScale MCP called: NO
YEScale config changed: NO
Fallback to YEScale added: NO
Other provider changes: NO

## Validation
Build: `dotnet build TodoX.Dashboard.sln -c Release --no-restore /p:UseSharedCompilation=false /m:1` passed
Tests: `dotnet test TodoX.Web.Tests\\TodoX.Web.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RDanceReferencePromptRegressionTests|FullyQualifiedName~DanceSellRenderHandlerTests" /p:UseSharedCompilation=false /m:1` passed
Tests result: 6 passed, 0 failed
Broader RDance snapshot filter: 4 pre-existing stale-source assertion failures were not changed by this fix: `RDanceDetailPageUsesJobRouteAndKeepsWorkflowTabs`, `DanceSell79AiMotionSubmitUsesRouteFieldsAndProviderMode`, `RDanceDetailPageUsesCustomImageUploadZonesAndReferenceCopy`, and `RDanceRetryAndResultUiUseFreshMotionStateAndSharedLoadingAnimation`.
git diff --check: passed
format: `dotnet format TodoX.Dashboard.sln --verify-no-changes --no-restore --include TodoX.Web\\Services\\Render\\RenderJobService.cs TodoX.Web\\Services\\DanceSell\\DanceSellAiOperations.cs TodoX.Web\\Services\\DanceSell\\DanceSellPhase2Services.cs TodoX.Web\\Services\\DanceSell\\DanceSellRenderHandler.cs TodoX.Web\\Tests\\RDanceReferencePromptRegressionTests.cs` passed
publish: `dotnet publish TodoX.Web\\TodoX.Web.csproj -c Release --no-restore -o artifacts\\publish\\todox-dashboard /p:UseSharedCompilation=false /m:1` passed

## Changed files
- `TodoX.Web/Services/DanceSell/DanceSellAiOperations.cs`
- `TodoX.Web/Services/DanceSell/DanceSellPhase2Services.cs`
- `TodoX.Web/Services/DanceSell/DanceSellRenderHandler.cs`
- `TodoX.Web/Tests/RDanceReferencePromptRegressionTests.cs`
- `TodoX.Web.Tests/DanceSellRenderHandlerTests.cs`
- `TodoX.Web.Tests/RDanceFashionDemoPageTests.cs`
- `TodoX.Web/RDANCE_MANUAL_RETRY_OPERATION_FIX_REPORT.md`

## Acceptance
- [x] Manual retry creates new motion operation
- [x] New attempt_no increments
- [x] parent_operation_id links previous attempt
- [x] New render job uses new operationId
- [x] Old failed operation remains historical
- [x] Retry cap resets for manual retry
- [x] Automatic retry still respects cap
- [x] Verified provider assets can be reused safely
- [x] New reference prevents stale asset reuse
- [x] Kling ratio remains default
- [x] No duplicate billing
- [x] No schema migration
- [x] YEScale untouched
- [x] Build passed
- [x] Tests passed
- [x] Publish passed
- [x] Push completed
