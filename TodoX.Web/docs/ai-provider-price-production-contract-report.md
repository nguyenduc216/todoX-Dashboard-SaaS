# AI Provider Price Production Contract Report

Date: 2026-08-13

## What changed

- Added a canonical `AiModelPriceNormalizer` for catalog and manual save paths.
- Normalized provider price metadata before persistence.
- Kept the active variant UPSERT identity aligned with the production index.
- Added defensive SQL defaults for both price write paths.
- Added regression tests for normalization, conflict identity, and insert-path contract checks.

## Production contract covered

- `rate_type` defaults to `per_unit`
- `unit_type` defaults to `request`
- `provider_price_unit` defaults to `credit`
- `sell_price_mode` defaults to `AUTO`
- `minimum_points` is clamped to `>= 0`
- `rounding_rule` defaults to `CEIL`
- `price_source` defaults to `catalog` for sync rows
- `effective_from` defaults to current UTC time
- catalog rows default to `active = true`

## Invalid row handling

- `duration_seconds <= 0` is rejected
- negative provider price values are rejected
- negative provider price default values are rejected
- negative internal cost points are rejected
- negative sell points are rejected
- invalid effective ranges are rejected

## Validation

- `dotnet build TodoX.Dashboard.sln -c Release`
- `dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release`
- `dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard`

## Result

- Build: passed
- Tests: passed
- Publish: passed
