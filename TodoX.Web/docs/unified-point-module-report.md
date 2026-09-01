# Unified Point Module Report

## Repository / Branch
`integration/rdance-on-construction-video-core`

## Commit SHA
Pending final commit

## Root Cause / Previous Point Architecture
The repository used several pricing paths: legacy sell-point tables, fixed per-scene/duration logic, and wallet debits that were not centralized. The new point module moves customer billing to one shared authority: `IPointPricingService` / `PointPricingService`.

## Existing Wallet Semantics
- available balance: `billing.token_wallets.balance`
- locked/special/reserved balance: `billing.token_wallets.locked_balance`
- point status behavior: persisted on render jobs with existing `RenderPointStatuses`

## Legacy Point Logic Inventory
Engine | File | Class | Method | Old Logic | Replacement
--- | --- | --- | --- | --- | ---
Timelapse | `TodoX.Web/Services/Timelapse/TimelapseJobService.cs` | `TimelapseJobService` | `CreateDraftAsync`, `StartOrResumeAsync` | fixed sell-point snapshot and charge flow | `PointPricingService`
rVideo | `TodoX.Web/Services/VideoRender/SceneVideoRenderHandler.cs` | `SceneVideoRenderHandler` | `HandleAsync` | per-scene provider price estimation plus separate point estimate path | `PointPricingService`
rDance | `TodoX.Web/Services/DanceSell/DanceSellPhase2Services.cs` | `DanceSellPhase2Services` | billing/setup paths | old phase pricing snapshot usage | `PointPricingService`
Core | `TodoX.Web/Services/Platform/CoreBillingService.cs` | `CoreBillingService` | `EstimateAsync`, `ReserveAsync`, `CompleteAsync`, `FailAsync` | provider-neutral legacy billing path | `PointPricingService`

## Part 1 — Point Management
Implemented wallet balance display, history, top-up, adjustments, refunds, and voucher create/redeem flows in the existing wallet stack.

## Part 2 — Point Configuration
Added database-backed global IMAGE/VIDEO/VOICE rates plus per-service overrides, with one shared resolver and estimate calculator.

## Part 3 — Legacy Point Migration
Migrated Timelapse, rVideo, rDance, and core billing entry points to the new pricing service for active customer job estimation.

## Part 4 — Pre-render Point Chain
Render entry points now build a usage plan, estimate points, compare against usable balance, and block insufficient jobs before provider submission.

## rVideo Integration
Scene-video jobs now build a point estimate from the unified service and store it on the child job input.

## rDance Integration
Phase 2 billing paths now resolve through the unified point service while keeping provider cost metadata separate.

## Timelapse Integration
Draft and start/resume now use IMAGE + VIDEO point estimation from the shared module. Voice is treated as disabled.

## Other Active Render Services
Core billing was routed through the shared point service. Legacy provider-cost logic remains available for internal analysis only.

## Voucher Implementation
Added voucher tables, redemption validation, atomic wallet credit, and transaction logging.

## Job Point Display
Timelapse job UI now shows total and component point estimates. Wallet and service screens expose point details and admin controls.

## Main Header Point Display
Authenticated header now shows the current usable balance chip.

## Database Changes
Created idempotent SQL for point rates, service overrides, vouchers, and redemptions.

## SQL Files
- `TodoX.Web/database/migrations/20260902_point_module.sql`
- `TodoX.Web/database/manual/verify_point_module.sql`

## Permissions / Audit
Wallet mutations and voucher actions write ledger and usage records through the existing billing tables.

## Tests
- Focused regression slice passed: 92 tests
- Full suite: 847 passed, 5 failed
- Failing tests:
  - `BillingAndRatioRegressionTests.RequestedRatioOverridesProviderRouteDefaults`
  - `RVideoAutosaveWorkflowTests.SceneGrid_IsTwoColumnsOnDesktopAndOneColumnNarrow`
  - `DanceSellAi79ReferenceProviderTests.SubmitAsync_UsesVerifiedFashionTryOnFormPayload`
  - `DanceSellPhase2ValidationTests.ReferencePrompt_MatchesTheVerified79AiTryOnPromptExactly`
  - `FavoriteServicesRegressionTests.FavoriteAction_IsRenderedBesidePrimaryAction_NotOverThumbnail`

## Build Result
- `dotnet build TodoX.Web\TodoX.Web.csproj -c Release` passed with existing Razor nullable warnings
- `dotnet build TodoX.Dashboard.sln -c Release` passed with existing Razor nullable warnings

## Publish Result
`dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\todox-dashboard` passed. Output directory: `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`.

## Git Push Result
Not completed in this session

## Files Changed
- `TodoX.Web/Program.cs`
- `TodoX.Web/Models/Catalog/PointPricingModels.cs`
- `TodoX.Web/Models/Timelapse/TimelapseModels.cs`
- `TodoX.Web/Services/PointPricingService.cs`
- `TodoX.Web/Services/WalletService.cs`
- `TodoX.Web/Services/BillingRepository.cs`
- `TodoX.Web/Services/Platform/CoreBillingService.cs`
- `TodoX.Web/Services/Platform/CoreJobApplicationService.cs`
- `TodoX.Web/Services/Timelapse/TimelapseJobService.cs`
- `TodoX.Web/Services/VideoRender/SceneVideoRenderHandler.cs`
- `TodoX.Web/Services/VideoRender/RVideoSceneAudioAutoChainService.cs`
- `TodoX.Web/Services/DanceSell/DanceSellPhase2Services.cs`
- `TodoX.Web/Components/Layout/MainLayout.razor`
- `TodoX.Web/Components/Pages/Wallets.razor`
- `TodoX.Web/Components/Pages/Services.razor`
- `TodoX.Web/Components/Pages/TimelapseJobCreate.razor`
- `TodoX.Web/Components/Dialogs/ServicePointRatesDialog.razor`
- `TodoX.Web/database/migrations/20260902_point_module.sql`
- `TodoX.Web/database/manual/verify_point_module.sql`
- `TodoX.Web.Tests/PointModuleRegressionTests.cs`
- `TodoX.Web.Tests/TimelapsePhase2ATests.cs`
- `TodoX.Web.Tests/TimelapsePhase2CTests.cs`

## Remaining Limitations
The full repository still contains unrelated legacy billing paths and pre-existing failing tests. The current point module change set covers the active customer-facing flows identified in the working slice, but the suite is not fully green yet.
