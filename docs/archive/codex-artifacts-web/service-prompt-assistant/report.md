# Gommo Agent Credential Resolver Report

Date: 2026-09-21
Branch: `feature/admin-job-monitor`
Baseline: `fcf9e637216b5e6174b2ba4d1cbfbb2153193ac5`

## Changed Files

- `TodoX.Web/Services/AiProviders/ProviderCredentialResolver.cs`
- `TodoX.Web.Tests/ProviderCredentialFrameworkTests.cs`

No database schema, migration, video pipeline, render routing, queue, or provider selection code was changed.

## Root Cause

The reported `billing.provider_accounts` lookup does not exist in the current production repository implementation. Inspection confirmed that `ProviderCredentialRepository` already uses the existing TodoX AI credential framework:

- `public.todox_ai_provider_account`
- `public.todox_ai_provider_account_credential`
- `system.ai_provider_credentials_secure`

There is no query to `billing.provider_accounts`, no EF `ToTable` mapping for that schema in this credential path, and no new credential table was introduced by this fix. The remaining defect in the previous commit was duplicate `gommo_agent -> 79ai` normalization statements in `ProviderCredentialResolver`.

## Solution

- Kept the Prompt Assistant contract input as `provider_code = gommo_agent`, `credential_role = access_token`.
- Consolidated normalization into one switch expression: `gommo_agent => 79ai`.
- The resolver then calls the existing repository path, which selects the enabled production `79ai` account, active credential mapping, and secure credential record, decrypts the secret, updates `last_used_at`, and returns it to the caller.
- Direct `79ai` input remains unchanged for all existing video and other AI provider callers.
- Missing credentials still throw the existing sanitized `InvalidOperationException`; tests verify no secret leakage.

## Validation

- `dotnet build TodoX.Dashboard.sln -c Release --no-restore -p:UseSharedCompilation=false /m:1` - passed, 0 errors.
- `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~GommoPromptAssistantTests|FullyQualifiedName~ProviderCredentialFrameworkTests"` - passed, 9/9.
- `dotnet test ... --filter FullyQualifiedName~RVideoVideoHotfixTests` - no matching tests in the configured test project; no RVideo production code was changed. Existing RVideo tests are under `TodoX.Web\Tests` and are not included by `TodoX.Web.Tests.csproj`.
- `git diff --check` - passed.
- `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -p:UseSharedCompilation=false /m:1 -o D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard` - passed.

## Database / Deployment

No database change is required. No migration, SQL script, new table, or schema change was created or executed.

Published output:
`D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish\todox-dashboard`

## Protected Areas

Untouched: video render credential lookup behavior, 79AI provider routing, provider account lease behavior, render queue, image/video/audio/voice generation, workers, ffmpeg, mux/finalizer, RDance, Timelapse, Dance Sell, and billing/points.