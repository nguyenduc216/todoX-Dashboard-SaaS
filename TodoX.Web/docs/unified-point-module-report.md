# Unified Point Module Report

## Repository / Branch
Repository: `https://github.com/nguyenduc216/todoX-Dashboard-SaaS`
Branch: `integration/rdance-on-construction-video-core`

## Base Commit
`5a9f6d189743bb312b9071b18845ee23aadc4294`

## Final Commit SHA
`a39486af6bb23acd230ea59a82f3e95bebfb63f1`

## Root Cause
Active billing paths still mixed the unified Point Module with legacy point flags and fixed-duration assumptions. That allowed `LegacyPointBilling:Enabled=false` to suppress new estimates, allowed rVideo duration to imply quality, and made Timelapse image/video estimates drift from the actual work plan.

The remaining gaps were incomplete aggregate usage planning, child-level video authority, missing backend wallet authorization, and rerender intent not being represented as a billable operation.

## Shared Usage Plan

`PreRenderUsagePlan` and `PreRenderVideoScene` provide the shared immutable input. Video seconds are validated and summed from explicit scene durations; pricing remains owned by `IPointPricingService`.

## Duration Source by Engine
rVideo scene duration comes from the prompt/imported scene plan and is persisted on `video_render.video_project_scenes.duration_seconds`. Import validation now rejects missing or non-positive durations with `VIDEO_SCENE_DURATION_REQUIRED`.

Timelapse currently creates a fixed runtime clip contract of `TimelapseRequestRules.RuntimeClipDurationSeconds = 6`; the persisted snapshot stores the resulting pricing seconds before start. Generated image count is derived from `TimelapseStageGraphBuilder.Build(sceneCount, hasStartImage)`.

rDance continues to use its current job/mode/provider route configuration. This change did not alter provider API contracts or introduce guessed duration/provider rules.

Core service jobs use explicit `videoSeconds` when supplied; otherwise they compute from `sceneCount * durationSeconds`. Missing billable usage is a hard validation error.

## rVideo Usage Calculation

The batch handler builds a parent usage plan from all selected scenes before child enqueue and charges the parent reference. Child jobs carry zero point cost.
Scene video estimates use `scene.DurationSeconds * resolved video rate`. `SceneVideoRenderHandler` no longer maps `DurationSeconds >= 8` to premium; quality resolves from provider/capability point-quality config and defaults to standard.

## rDance Usage Calculation
rDance Phase 2 still resolves through `PointPricingService` for image/video estimates while provider cost accounting remains separate. No extra image/voice usage was invented.

## Timelapse Usage Calculation
Timelapse estimates image count from the actual generated-stage graph. Uploaded/reused start image at 0% is excluded. Video seconds come from the persisted clip duration list and each clip persists an explicit duration.

## Image Billing Rules
Image points are counted only for planned AI image generation. Uploaded/reused images and selected reference images passed through without AI generation count as zero image units.

## Voice Billing Rules
rVideo external library voice generation is represented as one VOICE unit per Vbee generation. Native/no/existing voice remains zero VOICE units. Timelapse voice is disabled in this flow.

## Pre-render Balance Check
Wallet `balance` is the usable balance. Core reservations subtract from `balance` and add to `locked_balance`, so UI/backend checks must not subtract locked balance a second time.

## Legacy Billing Removal
`LegacyPointBillingFeatureFlags.NormalizePointCostEstimate` now preserves the estimate. `NormalizePointStatus` no longer changes a positive unified estimate to `not_required` when legacy billing is disabled.

## Retry / Rerender
System retries reuse the same logical work and do not create extra customer charges. Explicit user rerender paths must use new logical references for additional usage.

## Customer Authorization
Customer wallet pages now render only own wallet/history and voucher redemption. Rate configuration, all-wallet/history views, voucher administration, and mutations are hidden from customers; backend mutation methods require an actor id.

## Database Changes
`20260902_point_module.sql` now adds idempotent unit constraints:
- `video` requires `per_second`
- `image` requires `per_render`
- `voice` requires `per_render`

Global rate seed now uses canonical `system.tenants` instead of `crm.customers`, so tenants do not need a customer row before receiving the six default rates.

## SQL Verification
SQL was not executed against a database per project safety rules. Source regression tests verify the migration contains both unit constraints and the `system.tenants` seed source.

## Tests
Commands run:
- `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PointModuleRegressionTests|FullyQualifiedName~LegacyPointBillingFeatureFlagsTests|FullyQualifiedName~RVideoFoundationTests|FullyQualifiedName~TodoXVideoPromptParserTests" -p:UseSharedCompilation=false` passed: 15/15.
- `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore -p:UseSharedCompilation=false` failed: 849 passed, 5 failed.

Remaining failing tests are outside the touched files:
- `BillingAndRatioRegressionTests.RequestedRatioOverridesProviderRouteDefaults`
- `RVideoAutosaveWorkflowTests.SceneGrid_IsTwoColumnsOnDesktopAndOneColumnNarrow`
- `DanceSellAi79ReferenceProviderTests.SubmitAsync_UsesVerifiedFashionTryOnFormPayload`
- `DanceSellPhase2ValidationTests.ReferencePrompt_MatchesTheVerified79AiTryOnPromptExactly`
- `FavoriteServicesRegressionTests.FavoriteAction_IsRenderedBesidePrimaryAction_NotOverThumbnail`

## Build
`dotnet build TodoX.Dashboard.sln -c Release` passed with existing generated Razor nullable warnings.

## Publish
`dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\todox-dashboard` passed.
Output directory: `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

## Git Push
Implementation commit pushed to `origin/integration/rdance-on-construction-video-core`.

## Files Changed
- `TodoX.Web/Components/Pages/TimelapseJobCreate.razor`
- `TodoX.Web/Models/RVideoModels.cs`
- `TodoX.Web/Models/Timelapse/TimelapseModels.cs`
- `TodoX.Web/Services/LegacyPointBillingFeatureFlags.cs`
- `TodoX.Web/Services/Platform/CoreBillingService.cs`
- `TodoX.Web/Services/PointPricingService.cs`
- `TodoX.Web/Services/Timelapse/TimelapseJobService.cs`
- `TodoX.Web/Services/VideoRender/RVideoSceneJsonService.cs`
- `TodoX.Web/Services/VideoRender/SceneVideoRenderHandler.cs`
- `TodoX.Web/Tests/LegacyPointBillingFeatureFlagsTests.cs`
- `TodoX.Web/Tests/PointModuleRegressionTests.cs`
- `TodoX.Web/Tests/RVideoFoundationTests.cs`
- `TodoX.Web/Tests/TodoXVideoPromptParserTests.cs`
- `TodoX.Web/Tests/TimelapsePhase2CTests.cs`
- `TodoX.Web/database/migrations/20260902_point_module.sql`

## Remaining Limitations
The repository is not fully green because of the five unrelated failures listed above. No migrations were run, no production deployment was performed, and no YEScale provider metadata was changed.
