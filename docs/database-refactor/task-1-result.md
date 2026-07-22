# Task 1 result - AI core reset inventory and plan

## Scope

Task 1 completed inventory, backup, safe provider/model export, code-impact map, target schema docs, runbooks, and reset SQL package. No runtime tables were dropped or modified.

## Backup

- Custom dump: `D:\todoX\db-backups\ai-core-reset\backup-before-ai-core-reset-20260722-1413.dump`
- Custom dump bytes: `2427944`
- Custom dump SHA256: `124E5E3721A15B66E8A381D7B51F979B0FBD8F07154056AC6B3229AF3BA7D492`
- Schema backup: `D:\todoX\db-backups\ai-core-reset\backup-before-ai-core-reset-schema-20260722-1413.sql`
- Schema backup bytes: `206301`
- Schema backup SHA256: `0A05FFB5BA531CC62DA665F253F56E47B97EC89ECE45435E0DB9F3283AB69687`
- `pg_restore --list`: success, `732` lines

## Provider/model export

- Providers exported: `
5
`
- Capabilities exported: `
35
`
- Credential references exported: `
0
`
- Secret columns excluded: `secret_value`, `secret_json`

## Code impact

- Impact rows: `
1541
`
- Impacted files: `
100
`
- Highest-risk replacements: feature-provider route, Dance Sell provider operations, operation assets, image-only billing tables.

## SQL package

Created `database/manual/ai-core-reset/` with backup checklist, inventory, clear/drop/normalize/create/seed/verify scripts. Destructive scripts include transactions, advisory lock, and timeouts.

## Proposed keep/create/drop

- Keep/normalize: provider, capability, render core, provider usage, token wallets/transactions/usage, Dance Sell business tables.
- Create: provider account, account credential, account lease, balance ledger, generic AI billing records/attempts.
- Drop after reset/code switch: feature provider route, Dance Sell provider operations, operation assets, operation billing transactions; image-only billing tables after generic replacement.

## Validation

- `database/manual/ai-core-reset/01_inventory.sql`: passed against `todo_saas`.
- `git diff --check`: passed; only Git line-ending warning for pre-existing dirty key file outside this task.
- `dotnet restore TodoX.Dashboard.sln`: passed, all projects up-to-date.
- `dotnet build TodoX.Dashboard.sln -c Release --no-restore`: passed, 0 warnings, 0 errors.
- `dotnet format TodoX.Dashboard.sln --verify-no-changes`: failed on pre-existing whitespace in unrelated runtime files (`AccountRepository.cs`, `AuditRepository.cs`, `CatalogAdminRepository.cs`, `ChibiAvatarService.Generate.cs`, `PromptTemplateRepository.cs`, `SettingsApiRepository.cs`, `SocialPageRepository.cs`, `WalletService.cs`). No runtime code was changed in Task 1.
- New-file secret leak check: passed.

## Risks

- `system.provider_credentials` currently has 0 rows, so KIE account seed relies on credential config name metadata (`KIE_API_KEY`) instead of DB credential row.
- Full runtime code still depends on legacy tables; Task 2 should be followed by Task 3 quickly.
- Existing unrelated dirty file `TodoX.Web/keys/todox-vertex-sa.json` remains uncommitted and excluded.
