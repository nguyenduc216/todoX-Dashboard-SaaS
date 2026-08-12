# AI Provider Contract Hotfix Report

Date: 2026-08-12

## Scope

Fixed exactly two production contract mismatches:

- `public.todox_ai_provider_sync_change.change_type` source-controlled migration contract.
- AI provider capability `unit_type` application validation contract.

No database migration was executed. No production deployment or service restart was performed.

## Changes

- Added manual SQL migration `database/manual/ai-provider-catalog/04_fix_sync_change_type_check.sql`.
- Recreated `ck_todox_ai_provider_sync_change_type` with both legacy and current change type values.
- Added canonical `AiProviderCatalog.UnitTypes` contract and `AiProviderCatalog.IsValidUnitType`.
- Updated capability save validation to use the canonical unit type validator.
- Kept the AI Providers UI dropdown sourced from `AiProviderCatalog.UnitTypes`.
- Added regression tests for sync change type and capability unit type contract drift.

## Canonical Unit Types

The application canonical set is:

`credits`, `tokens`, `token_1000`, `request`, `requests`, `image`, `images`, `second`, `seconds`, `video_second`, `video_seconds`, `minute`, `minutes`, `scene`, `character_1000`, `usd`, `fixed`.

## Sync Change Types

The source-controlled migration allows:

Legacy:

`insert`, `update`, `status_change`, `price_change`, `disable`, `enable`, `no_change`.

Current:

`MODEL_ADDED`, `MODEL_UPDATED`, `MODEL_STATUS_CHANGED`, `MODE_ADDED`, `DURATION_ADDED`, `DURATION_REMOVED`, `RESOLUTION_ADDED`, `PRICE_ADDED`, `PRICE_CHANGED`, `PRICE_DISABLED`.

## Validation

- `git fetch origin`: Passed.
- `dotnet build TodoX.Dashboard.sln -c Release`: Passed with 48 existing warnings, 0 errors.
- `dotnet test TodoX.Web.Tests\TodoX.Web.Tests.csproj -c Release`: Passed, 243 tests.
- `dotnet publish TodoX.Web\TodoX.Web.csproj -c Release --no-restore -o artifacts\publish\todox-dashboard`: Passed.
- `git diff --check`: Passed with normal CRLF warnings only.

One parallel validation attempt hit a local `TodoX.Web.dll` file lock from `VBCSCompiler`; rerunning the test command sequentially passed.

## Confirmation

- `image` is now accepted by application-side capability validation.
- Every production-supported capability `unit_type` in the source-controlled schema contract is accepted.
- Random invalid unit values remain rejected.
- The UI unit dropdown and validator share the same canonical list.
- The sync change type migration is source-controlled and preserves legacy values.
- Secure credentials, 79AI `/models` POST logic, n8n, Timelapse, RVideo, RDance, pricing rules, wallet, and billing were not changed.
