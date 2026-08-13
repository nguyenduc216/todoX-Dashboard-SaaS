# Timelapse Customer Release Readiness

Date: 2026-08-13

## Selected-Service Flow

The customer Timelapse creator is opened from a commercial service card with `serviceId` and `serviceCode` query parameters. The page loads `catalog.services` by `serviceId`, displays the selected commercial `service_name`, and keeps `ServiceId` / `ServiceCode` in `TimelapseCreateRequest` and job metadata.

If the selected service is missing, inactive, or not `service_type='timelapse'`, the page shows a controlled warning and sends the customer back to `/create`.

## Runtime Mode Mapping

Runtime values are unchanged:

- `fast`
- `professional`

Customer-facing quality labels are:

- `fast` -> `standard` -> `Tiêu chuẩn`
- `professional` -> `premium` -> `Cao cấp`

The shared helper is `TimelapseSellPricing.QualityTierForMode`.

## Runtime Clip Duration

The Timelapse creator does not expose duration as a customer option. This UI phase uses `TimelapseRequestRules.RuntimeClipDurationSeconds = 6` as the fixed service sell-price duration for each generated video scene. This matches the seeded 6-second commercial sell-price rows and keeps duration out of the customer UI until the runtime exposes a duration contract.

## Sell-Price Lookup

Customer estimate and server-side draft validation both use `IServiceSellPriceResolver` against `catalog.service_sell_prices`.

The lookup uses:

- selected `serviceId`
- `asset_type='video_scene'`
- quality tier mapped from runtime mode
- `duration_seconds=6`

Provider/model pricing tables and 79AI/provider costs are not used for customer sell pricing.

## Billable Formula

Uploaded customer reference/final image is free and is not counted as a billable generated image.

For this release, estimate includes video scene price only:

`videoSubtotal = videoSceneSellPoints * SceneCount`

Scene counts remain limited to `3`, `4`, `5`, `6`.

Generated AI images are not included in the estimate because the current Timelapse draft/runtime contract does not expose a deterministic billable generated-image count at create time. TODO: add image-generation billing once the server runtime owns and exposes an authoritative generated-image count.

## Missing Price Behavior

If no active matching sell-price row exists, the page shows:

`Chưa cấu hình giá cho lựa chọn này.`

The submit button is disabled, preventing accidental zero-cost production jobs.

## Server-Side Validation

The browser estimate is not trusted as authority. `TimelapseJobService.CreateDraftAsync` recalculates the active sell price from the selected service, mapped quality tier, and fixed runtime clip duration before saving the draft. If pricing is missing, draft creation fails.

No wallet debit logic was introduced in this UI phase.

## Files Changed

- `TodoX.Web/Components/Pages/TimelapseJobCreate.razor`
- `TodoX.Web/Components/Pages/TimelapseJobCreate.razor.css`
- `TodoX.Web/Models/Timelapse/TimelapseModels.cs`
- `TodoX.Web/Services/CatalogRepository.cs`
- `TodoX.Web/Services/Timelapse/TimelapseJobService.cs`
- `TodoX.Web.Tests/TimelapsePhase2ATests.cs`
- `TodoX.Web/docs/timelapse-customer-release-readiness.md`

## Validation

- `dotnet build TodoX.Dashboard.sln -c Release`: passed with 45 existing CS8669 Razor generated-code warnings and 0 errors.
- `dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release`: passed, 368/368 tests.
- `git diff --check`: passed. Git reported line-ending normalization warnings only.
- `dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard`: passed.
- Publish output: `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`.
