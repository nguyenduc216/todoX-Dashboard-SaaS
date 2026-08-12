# Phase A/B Secure Credential 79AI Report

## Summary

Implemented Phase A generic secure provider credential framework and Phase B 79AI migration into that framework.

Phase C was not implemented. 79AI catalog HTTP contract, `/models`, model parsing, pricing sync, and duration sync were not changed.

Phase E was not implemented. KIE, YEScale, Vbee, and other providers were not migrated.

## Changed Files

- `TodoX.Web/Program.cs`
- `TodoX.Web/Components/Pages/AiProviders.razor`
- `TodoX.Web/Services/AiProviders/ProviderCredentialModels.cs`
- `TodoX.Web/Services/AiProviders/ProviderCredentialKeyStore.cs`
- `TodoX.Web/Services/AiProviders/ProviderCredentialProtector.cs`
- `TodoX.Web/Services/AiProviders/ProviderCredentialRepository.cs`
- `TodoX.Web/Services/AiProviders/ProviderCredentialResolver.cs`
- `TodoX.Web/Services/AiProviders/Ai79CredentialMigrationService.cs`
- `TodoX.Web.Tests/ProviderCredentialFrameworkTests.cs`
- `TodoX.Web/docs/ai-provider-secure-credentials.md`
- `TodoX.Web/docs/ai-provider-secure-credential-phase-ab-report.md`
- `database/manual/ai-provider-secure-credentials/01_create_master_key_store.sql`
- `database/manual/ai-provider-secure-credentials/02_verify_79ai_secure_credential.sql`

## New Interfaces And Services

- `IProviderCredentialKeyStore` / `ProviderCredentialKeyStore`
- `IProviderCredentialProtector` / `ProviderCredentialProtector`
- `IProviderCredentialRepository` / `ProviderCredentialRepository`
- `IProviderCredentialResolver` / `ProviderCredentialResolver`
- `IAi79CredentialMigrationService` / `Ai79CredentialMigrationService`

## Admin Migration Action

Location: `Admin > AI Providers`, provider detail panel for provider code `79ai`.

Action: `Khởi tạo credential bảo mật`.

The action requires admin access, asks for confirmation, reads the legacy 79AI runtime credential from the automation database, and displays only safe metadata.

## Manual SQL

- `database/manual/ai-provider-secure-credentials/01_create_master_key_store.sql`
- `database/manual/ai-provider-secure-credentials/02_verify_79ai_secure_credential.sql`

Verification SQL does not select plaintext token, key material, ciphertext, nonce, or auth tag.

## Operator Steps

1. Apply `database/manual/ai-provider-secure-credentials/01_create_master_key_store.sql` to `todo_saas`.
2. Deploy published output from `artifacts/publish/ai-provider-secure-credential-phase-ab`.
3. Log in as admin.
4. Open `AI Providers`.
5. Select provider `79ai`.
6. Click `Khởi tạo credential bảo mật`.
7. Confirm the dialog.
8. Confirm the UI shows only masked hint, active status, credential role, key version, and last-used metadata.
9. Run `database/manual/ai-provider-secure-credentials/02_verify_79ai_secure_credential.sql`.

## Validation

- `dotnet build TodoX.Web/TodoX.Web.csproj -c Release`: passed.
- `dotnet test TodoX.Web.Tests/TodoX.Web.Tests.csproj -c Release`: passed, 234 tests.
- `dotnet build TodoX.Dashboard.sln -c Release`: passed.
- `dotnet publish TodoX.Web/TodoX.Web.csproj -c Release --no-restore -o artifacts/publish/ai-provider-secure-credential-phase-ab`: passed.

## Confirmations

- No ENV, IIS, DPAPI, server-local certificate, or per-server secret setup is required for the DB-managed key.
- n8n workflows were not modified.
- Legacy `public.todox_video_79ai_provider_keys` is read only; the legacy 79AI credential row is not modified.
- No plaintext production secret or key material is added to source, UI, logs, docs, SQL verification output, or tests.
- No other provider was migrated.
