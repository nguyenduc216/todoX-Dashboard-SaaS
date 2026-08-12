# AI Providers Model Dialog and 79AI Catalog Completion Report

## Summary

Implemented the `Admin > AI Providers > MODEL & VARIANT` model detail popup and completed a live 79AI video catalog audit using the existing secure credential resolver. The model list remains the primary page content; model detail/edit, variants, provider costs, and raw diagnostics now open in a MudDialog.

## Changed Files

- `TodoX.Web/Components/Pages/AiProviders.razor`
- `TodoX.Web/Components/Dialogs/AiProviderModelDetailDialog.razor`
- `TodoX.Web/Services/AiProviders/AiCatalogClient.cs`
- `TodoX.Web/Services/AiProviders/AiProviderSyncService.cs`
- `TodoX.Web.Tests/AiProviderDurationPricingTests.cs`
- `TodoX.Web.Tests/AiProvidersAuthHydrationTests.cs`
- `TodoX.Web.Tests/AiProvidersEncodingTests.cs`
- `TodoX.Web.Tests/AiProvidersExperienceTests.cs`
- `TodoX.Web/docs/79ai-video-catalog-audit.md`
- `TodoX.Web/docs/ai-provider-model-dialog-79ai-catalog-completion-report.md`

## Dialog Behavior

- Row click, model name click, `Chi tiết`, and `Giá vốn` actions open `AiProviderModelDetailDialog`.
- Dialog uses `MaxWidth.ExtraLarge`, `FullWidth=true`, close button, and responsive scroll height.
- Dialog tabs: `TỔNG QUAN`, `VARIANT`, `GIÁ VỐN`, `RAW / NÂNG CAO`.
- Inline selected-model detail was removed from `MODEL & VARIANT`; only filters and the compact model table remain.

## 79AI Live Catalog

- Live catalog returned 48 total models and 25 video models.
- Grok exists as `grok_video_heavy`.
- VEO models found: `veo_omni`, `veo_3_1`.
- VEO Fast/Lite are returned as modes on `veo_3_1`, not separate provider model codes.
- Seedance found: `seedance_20_pro`, `seedance_20_mini`, `seedance_20_pro_edit`, `seedance_25_omni`.
- Seedance 2.0 Pro modes/resolutions/durations found: `fast`, `fast_2`, `professional`, `professional_2`; durations `4-15`; resolutions `480p`, `720p`, `1080p`.

Full non-secret catalog summary: `TodoX.Web/docs/79ai-video-catalog-audit.md`.

## Parser and Sync Fixes

- Parser now reads additional safe aliases for model identity and metadata: `model_name`, `model_id`, `model_key`, `label`, `modality`, `provider_server`.
- Parser now preserves nested provider variants/options/price options when returned.
- Sync remains keyed by `provider_id + provider_model_code`.
- Sync records sanitized ignored diagnostics for invalid/no model code and duplicate provider model code.
- No provider model codes or provider prices were invented.
- VEO Omni verified fallback behavior remains unchanged.

## Validation

- `dotnet build TodoX.Dashboard.sln -c Release`: passed.
- `dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release`: passed, 267/267 tests.
- `dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard`: passed.
- `git diff --check`: passed; Windows line-ending warnings only.

## Publish Output

- `D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

## Scope Confirmations

- No customer service pricing changes.
- No Timelapse, RVideo, RDance, wallet, billing, n8n, or secure credential storage/encryption changes.
- No database schema changes.
- No access token or raw credential data committed.
