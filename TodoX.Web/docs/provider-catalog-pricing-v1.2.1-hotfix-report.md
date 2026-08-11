# Provider Catalog & Pricing v1.2.1 Hotfix Report

Date: 2026-08-12

## Changed Files

- `TodoX.Web/Components/Pages/AiProviders.razor`
- `TodoX.Web/Services/AiProviders/AiPricingRepository.cs`
- `TodoX.Web/Services/AiProviders/AiPricingService.cs`
- `TodoX.Web/Services/AiProviders/AiProviderCatalogSyncWorker.cs`
- `TodoX.Web/Services/AiProviders/AiProviderSyncService.cs`
- `TodoX.Web.Tests/AiProviderDurationPricingTests.cs`
- `TodoX.Web.Tests/AiProviderQuickDefaultsSourceTests.cs`

## Scheduled Sync Trigger

- Added `SyncScheduledProviderAsync(...)` to the provider sync service.
- Manual UI sync keeps trigger `manual`.
- Daily worker now uses trigger `scheduled`.

## Retry Token Fix

- The daily worker now creates a fresh timeout `CancellationTokenSource` for each attempt.
- Retry reuses the parent stopping token only, not a cancelled timeout token.

## Pricing Editor

- The `GIA & DIEM` tab now exposes per-variant editing for sell points, mode, markup, minimum, and active state.
- Provider price fields stay read-only.
- Saving uses the existing pricing service/repository path.
- The UI reloads the selected model after save.

## Price Source UX

- Price source is shown as a badge.
- `catalog` shows as `79AI live`.
- `verified_seed` shows as `Verified seed` plus a warning note.
- `manual` shows as `Manual`.

## History UX

- `PRICE_CHANGED` sync records now render a readable summary using `before_json` and `after_json`.

## Validation

- `dotnet build TodoX.Web.csproj -c Release /p:UseSharedCompilation=false`
- `dotnet test .\TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release /p:UseSharedCompilation=false`
- `dotnet build .\TodoX.Dashboard.sln -c Release /p:UseSharedCompilation=false`
- `dotnet publish .\TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o .\artifacts\publish\ai-provider-pricing-hotfix`

Results:

- Build: passed
- Tests: passed, 226/226
- Publish: passed

## Database

- No migration required.
- No schema change made.
