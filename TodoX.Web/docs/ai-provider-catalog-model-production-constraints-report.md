# AI Provider Catalog Model Production Constraints Hotfix

## Summary

Fixed catalog model persistence so new provider models satisfy the production `public.todox_ai_provider_model` NOT NULL contract.

## Changes

- Normalized catalog-created model details before persistence:
  - missing provider status -> `UNKNOWN`
  - missing model-level provider price unit -> `credit`
  - 79AI model-level `79ai_credit` / `credits` -> `credit`
  - missing source -> `catalog`
  - missing raw JSON -> `{}`
  - missing display name -> `provider_model_code`
  - negative failure count -> `0`
- Preserved provider identity from the TodoX provider record:
  - `ProviderId = provider.Id`
  - `ProviderCode = provider.ProviderCode`
- Added invalid media type diagnostics instead of inventing required identifiers.
- Hardened every `INSERT INTO public.todox_ai_provider_model` path with SQL-level guards for nullable runtime parameters that target production NOT NULL columns.

## Changed Files

- `TodoX.Web/Services/AiProviders/AiProviderSyncService.cs`
- `TodoX.Web/Services/AiProviders/AiProviderModelRepository.cs`
- `TodoX.Web.Tests/AiProviderContractTests.cs`
- `TodoX.Web/docs/ai-provider-catalog-model-production-constraints-report.md`

## Validation

- `dotnet build TodoX.Dashboard.sln -c Release`
  - Result: passed
- `dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release`
  - Result: passed, 275 tests
- `dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard`
  - Result: passed
  - Output: `artifacts/publish/todox-dashboard`

## Notes

- No database schema changes were made.
- Pricing table provider unit semantics were not changed.
- 79AI `/models` API contract, secure credentials, wallet/billing, Timelapse, RVideo, RDance, and n8n were not modified.
