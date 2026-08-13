# AI Provider Sync Audit Contract Hotfix Report

Date: 2026-08-13

## Summary

Fixed the production sync audit contract failure caused by invalid `entity_type` values being inserted into `public.todox_ai_provider_sync_change`.

## Changes

- Added `AiProviderSyncChangeContract` as the canonical source for allowed sync audit `entity_type` and `change_type` values.
- Added strict repository-boundary validation in `InsertSyncChangeAsync` so unsupported values fail before reaching PostgreSQL.
- Replaced invalid sync audit entity mappings:
  - `catalog_ignored` -> `model`
  - `price_ignored` -> `price`
  - `status_diagnostic` -> `status`
  - `model_option` -> `capability`
- Kept diagnostic meaning inside JSON payloads.

## Production Contract

Allowed `entity_type`:

- `provider`
- `model`
- `capability`
- `price`
- `status`

Allowed `change_type`:

- `insert`
- `update`
- `status_change`
- `price_change`
- `disable`
- `enable`
- `no_change`
- `MODEL_ADDED`
- `MODEL_UPDATED`
- `MODEL_STATUS_CHANGED`
- `MODE_ADDED`
- `DURATION_ADDED`
- `DURATION_REMOVED`
- `RESOLUTION_ADDED`
- `PRICE_ADDED`
- `PRICE_CHANGED`
- `PRICE_DISABLED`

## Changed Files

- `TodoX.Web/Services/AiProviders/AiProviderSyncChangeContract.cs`
- `TodoX.Web/Services/AiProviders/AiProviderModelRepository.cs`
- `TodoX.Web/Services/AiProviders/AiProviderSyncService.cs`
- `TodoX.Web.Tests/AiProviderContractTests.cs`
- `artifacts/reports/ai-provider-sync-audit-contract-report.md`

## Validation

- `git diff --check`
  - Passed. Only Git line-ending warnings were reported.
- `dotnet build TodoX.Dashboard.sln -c Release /p:UseSharedCompilation=false`
  - Passed. Existing Razor generated-code CS8669 warnings remain.
- `dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release /p:UseSharedCompilation=false`
  - Passed: 342 tests.
- `dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard /p:UseSharedCompilation=false`
  - Passed.

## Publish Output

`D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

## Database

No database schema or migration changes were made.
