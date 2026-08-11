# AI Provider 79AI Duration Pricing Report

Date: 2026-08-12

## Scope

Implemented the production-friendly 79AI catalog, duration pricing, model options, scheduled sync, and provider admin UX improvements.

Not implemented by design:

- Timelapse worker
- Customer charging
- n8n workflow changes
- 79AI render execution

## Files Changed

- `TodoX.Web/Components/Pages/AiProviders.razor`
- `TodoX.Web/Models/AiProviderModels.cs`
- `TodoX.Web/Program.cs`
- `TodoX.Web/Services/AiProviders/AiCatalogClient.cs`
- `TodoX.Web/Services/AiProviders/AiPricingEngine.cs`
- `TodoX.Web/Services/AiProviders/AiPricingRepository.cs`
- `TodoX.Web/Services/AiProviders/AiProviderModelRepository.cs`
- `TodoX.Web/Services/AiProviders/AiProviderSyncService.cs`
- `TodoX.Web/Services/AiProviders/AiProviderCatalogSyncOptions.cs`
- `TodoX.Web/Services/AiProviders/AiProviderCatalogSyncWorker.cs`
- `TodoX.Web/Services/AiProviders/AiProviderModelOptionsNormalizer.cs`
- `TodoX.Web.Tests/AiProviderDurationPricingTests.cs`
- `database/manual/79ai_catalog_duration_pricing_verify.sql`
- `TodoX.Web/docs/ai-provider-duration-pricing-report.md`

## Database

Existing schema supports duration/mode/resolution pricing; no schema migration required.

No SQL was executed automatically.

Verification SQL provided:

```text
database/manual/79ai_catalog_duration_pricing_verify.sql
```

## 79AI Catalog Endpoint

The catalog endpoint remains configuration-driven from provider `ConfigJson`:

- `catalog.image_models_path` or `image_models_path`
- `catalog.video_models_path` or `video_models_path`

The sync client does not hard-code `/videos` as a catalog endpoint.

## Normalized Model Options

The model list/detail now exposes:

- `SupportedModes`
- `SupportedDurations`
- `SupportedResolutions`
- `SupportedRatios`

Options are derived from explicit provider arrays, provider price variants, and raw JSON fallback.

## Price Sync

For every incoming provider price variant, sync uses:

```text
model_id + mode + resolution + duration_seconds + ratio
```

Provider-controlled fields are updated:

- provider price
- provider default price
- provider price unit
- rate type
- unit type
- internal cost points
- price source
- active state

Admin-controlled fields are preserved on existing variants:

- sell points
- sell price mode
- markup percent
- minimum points
- rounding rule

Removed variants are marked inactive instead of deleted.

## Default Prices Seeded

VEO Omni verified seed variants are preserved by the catalog parser:

- 720p: 4s 1260, 6s 1800, 8s 2160, 10s 2700
- 1080p: 4s 1440, 6s 1980, 8s 2430, 10s 2880
- 4K: 4s 4500, 6s 5400, 8s 6300, 10s 7200

Seedance pricing is not invented when the provider snapshot has durations but no price matrix.

## Daily Sync

Config section:

```json
{
  "AiProviderCatalogSync": {
    "Enabled": true,
    "DailyHourLocal": 2,
    "ProviderCodes": [ "79ai" ],
    "TimeoutSeconds": 120,
    "RetryDelaySeconds": 30
  }
}
```

The scheduled worker:

- runs once daily
- uses provider codes from config
- creates a scoped sync service
- avoids duplicate concurrent sync through the provider-level lock in `AiProviderSyncService`
- records sync headers and changes

## Provider Admin UI

Route:

```text
/admin/ai-providers
```

Changes:

- MODEL tab shows model, media, status, modes, durations, resolutions, provider price from, TodoX points from.
- Detail view shows human-readable capability chips and supported configuration groups.
- Pricing matrix separates 79AI price, TodoX internal cost, and sell points.
- Raw JSON is only shown inside collapsed "Nâng cao".
- Sync history tab was renamed to `LỊCH SỬ ĐỒNG BỘ`.
- Added `GIÁ & ĐIỂM` tab for selected model pricing review.

## Validation

Commands run:

```powershell
dotnet build TodoX.Web\TodoX.Web.csproj -c Release /p:UseSharedCompilation=false
dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release /p:UseSharedCompilation=false --no-restore
dotnet build TodoX.Dashboard.sln -c Release /p:UseSharedCompilation=false --no-restore
dotnet format TodoX.Dashboard.sln whitespace --verify-no-changes --no-restore --include TodoX.Web/Models/AiProviderModels.cs TodoX.Web/Services/AiProviders/AiCatalogClient.cs TodoX.Web/Services/AiProviders/AiProviderModelOptionsNormalizer.cs TodoX.Web/Services/AiProviders/AiProviderModelRepository.cs TodoX.Web/Services/AiProviders/AiPricingEngine.cs TodoX.Web/Services/AiProviders/AiPricingRepository.cs TodoX.Web/Services/AiProviders/AiProviderSyncService.cs TodoX.Web/Services/AiProviders/AiProviderCatalogSyncOptions.cs TodoX.Web/Services/AiProviders/AiProviderCatalogSyncWorker.cs TodoX.Web.Tests/AiProviderDurationPricingTests.cs
dotnet format TodoX.Web.csproj whitespace --verify-no-changes --no-restore --include Components/Pages/AiProviders.razor
dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\ai-provider-duration-pricing
```

Results:

- Web build: passed with existing generated Razor nullable warnings in `AiProviders_razor.g.cs`.
- Tests: passed, 226 total.
- Solution build: passed, 0 warnings, 0 errors.
- Format checks: passed.
- Publish: passed.

Publish output:

```text
artifacts/publish/ai-provider-duration-pricing
```

## Manual Smoke Test

1. Login as admin/operator/root.
2. Open `/admin/ai-providers`.
3. Select the 79AI provider.
4. Check configured catalog paths in provider config.
5. Click `Đồng bộ ngay`.
6. Open tab `MODEL`.
7. Confirm VEO Omni shows duration `4s · 6s · 8s · 10s`.
8. Confirm model detail shows mode, duration, resolution, ratio chips.
9. Open `GIÁ & ĐIỂM` and verify pricing matrix.
10. Open `LỊCH SỬ ĐỒNG BỘ` and verify MODEL/PRICE change records.
11. Run `database/manual/79ai_catalog_duration_pricing_verify.sql` manually against the target database.

## Notes

Runtime "number of price variants synced" depends on the current 79AI catalog response. Tests verify the VEO Omni default 12 variants and that Seedance durations without provider prices do not create fake price rows.
