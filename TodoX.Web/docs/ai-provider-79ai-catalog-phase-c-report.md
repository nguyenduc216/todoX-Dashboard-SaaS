# 79AI Catalog Sync Phase C Report

## Summary

Completed Phase C for provider `79ai` only. The 79AI catalog client now resolves the secure credential through `IProviderCredentialResolver` and calls the Gommo `/models` endpoint using POST form requests for image and video model catalogs.

## Changed Files

- `TodoX.Web/Services/AiProviders/AiCatalogClient.cs`
- `TodoX.Web/Services/AiProviders/ProviderCredentialModels.cs`
- `TodoX.Web/Services/AiProviders/ProviderCredentialRepository.cs`
- `TodoX.Web.Tests/AiProviderDurationPricingTests.cs`
- `database/manual/ai-provider-secure-credentials/03_fix_provider_account_credential_secure_ref_check.sql`
- `TodoX.Web/docs/ai-provider-79ai-catalog-phase-c-report.md`

## Implementation Notes

- 79AI now uses `POST {provider.BaseUrl}/models`.
- Form body includes `access_token`, `domain`, and `type`.
- Image request sends `type=image`.
- Video request sends `type=video`.
- Default catalog path is `/models`.
- Optional override is `catalog.models_path`.
- Domain resolution order is provider account `config_json.domain`, provider `config_json.domain`, then `79ai.net`.
- Secure token is resolved with `IProviderCredentialResolver.ResolveAsync("79ai", "access_token", ct)`.
- The legacy table `public.todox_video_79ai_provider_keys` is not referenced by `Ai79CatalogClient`.
- 79AI does not require `image_models_path` or `video_models_path`.
- The existing parser and VEO Omni verified fallback prices are preserved.
- TodoX sell point preservation remains in `AiProviderSyncService`/pricing repository.

## Manual SQL

Added:

`database/manual/ai-provider-secure-credentials/03_fix_provider_account_credential_secure_ref_check.sql`

This updates the existing CHECK constraint to allow `secure_credential_id`.

No other DB schema changes were added.

## Validation

- `dotnet build TodoX.Web/TodoX.Web.csproj -c Release`: passed.
- `dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release`: passed, 237 tests.
- `dotnet build TodoX.Dashboard.sln -c Release`: passed.
- `dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/todox-dashboard`: passed.

## Operator Steps

1. Ensure Phase A/B secure credential migration has completed for account `79ai-primary`.
2. Apply `database/manual/ai-provider-secure-credentials/03_fix_provider_account_credential_secure_ref_check.sql` on new servers if not already applied.
3. Deploy publish output from `artifacts/publish/todox-dashboard`.
4. Open `Admin > AI Providers`.
5. Select provider `79ai`.
6. Click `Đồng bộ ngay`.
7. Verify sync history records image/video model updates and pricing rows without credential errors.
8. Verify secure credential `last_used_at` is updated.

## Confirmations

- Secure credential resolver is used for 79AI catalog sync.
- 79AI GET catalog path is removed from the 79AI execution path.
- `/models` POST is implemented.
- The access token is not placed in URL, query string, RawJson, summary JSON, Razor UI, logs, or exception text.
- n8n workflows were not modified.
- The legacy 79AI token table remains untouched and is not read by the catalog client.
- Timelapse, RVideo, RDance, KIE, YEScale, Vbee, wallet, billing, and service catalog were not modified.
- No provider besides `79ai` was migrated.
