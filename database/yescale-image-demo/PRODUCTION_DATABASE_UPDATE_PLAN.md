# YEScale Image Demo - Production Database Update Plan

Generated for database: `todo_saas`

This document summarizes the SQL files that should be applied manually to prepare YEScale Image Demo for production. It does not contain passwords, API keys, full connection strings, or any secret values.

## Current Database Findings

Last read-only inspection showed:

- Database name: `todo_saas`
- Tenant count: `1`
- Foundation tables: present
- Existing provider `yescale_task_image`: present and enabled
- Image model rows: present for `nano-banana-2`, `gpt-image`, `seedream-5`
- `nano-banana-2`: default on image capabilities
- `gpt-image`: cheap/selectable model
- `seedream-5`: backup model and not user-selectable
- `poster_generation`: currently enabled and should be disabled for this demo
- `billing.token_wallets`: missing demo support columns such as `wallet_scope`, `wallet_code`, `overdraft_limit`, `low_balance_threshold`
- `billing.ai_image_billing_records`: missing
- `billing.ai_image_provider_attempts`: missing
- `billing.yescale_image_default_snapshot`: missing
- Required permissions are missing:
  - `ai.image.system_wallet.use`
  - `ai.billing.dashboard.view`
  - `ai.billing.reconciliation.manage`

## Required Backup

Create a backup before applying any SQL. The latest successful backup observed during preflight was:

```text
D:\todoX\db-backups\todo_saas_before_yescale_image_demo_20260716-142731.dump
```

Before running updates, create a fresh backup again if the database has changed since that timestamp.

Example backup command shape, with secrets supplied outside the command output:

```powershell
$env:PGPASSWORD = "<database password>"
& "C:\Program Files\PostgreSQL\17\bin\pg_dump.exe" `
  -h "<host>" -p 5432 -U "<user>" -d "todo_saas" `
  -F c -f "D:\todoX\db-backups\todo_saas_before_yescale_image_demo_<timestamp>.dump"
$env:PGPASSWORD = $null
```

Proceed only if `pg_dump` exits with code `0` and the backup file size is greater than `0` bytes.

## SQL Apply Order

Run these files in this exact order with `ON_ERROR_STOP=1`.

### 1. `01_add_or_update_billing_support.sql`

Purpose:

- Adds wallet support columns:
  - `wallet_scope`
  - `wallet_code`
  - `overdraft_limit`
  - `low_balance_threshold`
- Creates/updates system wallet support.
- Creates billing records table:
  - `billing.ai_image_billing_records`
- Creates provider attempts table:
  - `billing.ai_image_provider_attempts`
- Adds idempotency and reconciliation fields/indexes.
- Ensures numeric scale can store fractional points such as `0.0192`.

Expected effect:

- Enables system wallet `TODOX_AI_IMAGE_SYSTEM`.
- Adds `logical_request_id` unique constraint.
- Adds provider task, tariff snapshot, estimated/actual USD, points, payer, and reconciliation fields.

### 2. `02_seed_yescale_image_tariffs.sql`

Purpose:

- Seeds/updates provider `yescale_task_image`.
- Seeds/updates model rows for:
  - `nano-banana-2`
  - `gpt-image`
  - `seedream-5`
- Stores pricing/tariff metadata in provider capability config.

Expected formula:

```text
1 USD YEScale = 8,000 VND
1 TodoX point = 10,000 VND
TodoX points = USD * 0.8
```

Expected model tariffs:

```text
nano-banana-2: 0.08 USD  -> 0.064 points
gpt-image:     0.024 USD -> 0.0192 points
seedream-5:    0.065 USD -> 0.052 points
```

### 3. `03_seed_system_wallet_and_permissions.sql`

Purpose:

- Ensures system wallet permission exists:
  - `ai.image.system_wallet.use`
- Ensures dashboard/reconciliation permissions exist:
  - `ai.billing.dashboard.view`
  - `ai.billing.reconciliation.manage`
- Assigns these permissions to existing admin/root roles when matching role codes exist.
- Verifies exactly one `TODOX_AI_IMAGE_SYSTEM` wallet exists.

Expected effect:

- Admin/root users can use the system image wallet only through server-side trusted billing flow.
- Dashboard access is controlled by `ai.billing.dashboard.view`.

### 4. `04_enable_yescale_image_demo.sql`

Purpose:

- Enables YEScale image demo routing.
- Sets `nano-banana-2` as default model.
- Keeps `gpt-image` selectable as cheap model.
- Keeps `seedream-5` as backup and not user-selectable.
- Disables `poster_generation` for YEScale because poster composite is not yet fully routed through the shared image router.
- Captures previous defaults in `billing.yescale_image_default_snapshot`.

Expected enabled capabilities:

```text
avatar_generation
chibi_avatar_generation
character_generation
image_generation
scene_image_generation
thumbnail_generation
```

Expected disabled capability:

```text
poster_generation
```

### 5. `verify_yescale_image_demo.sql`

Purpose:

- Fail-fast verification after update.
- Confirms provider, capabilities, defaults, prices, permissions, system wallet, billing tables, indexes, and reconciliation state.

Expected result:

- Script completes without exception.
- Final query output can be captured as evidence for production readiness review.

## Recommended Manual Runner

The repository includes:

```text
database/yescale-image-demo/run_yescale_image_demo_setup.ps1
```

Recommended command shape:

```powershell
$env:TODOX_DB_CONNECTION = "<full connection string>"
.\database\yescale-image-demo\run_yescale_image_demo_setup.ps1 -ConfirmProduction
$env:TODOX_DB_CONNECTION = $null
```

The runner:

- Checks `psql`
- Runs SQL in order
- Stops on first SQL error
- Runs verify at the end
- Does not print the connection string value

If `psql` is not in `PATH`, either add PostgreSQL bin to `PATH` or run the SQL files manually with:

```powershell
& "C:\Program Files\PostgreSQL\17\bin\psql.exe" `
  --set ON_ERROR_STOP=1 `
  --dbname "<connection string>" `
  --file "database\yescale-image-demo\01_add_or_update_billing_support.sql"
```

Repeat for each SQL file in the order listed above.

## Post-Update Checks

After SQL apply, run:

```powershell
$env:TODOX_DB_CONNECTION = "<full connection string>"
.\database\yescale-image-demo\run_yescale_image_demo_setup.ps1 -VerifyOnly -ConfirmProduction
$env:TODOX_DB_CONNECTION = $null
```

Then verify application configuration:

- `AiProviders__YEScale__AccessKey`: configured in environment/secret store
- Do not store the YEScale access key in source code, SQL, appsettings committed to Git, screenshots, or logs.

## Rollback

Rollback file:

```text
database/yescale-image-demo/rollback_yescale_image_demo.sql
```

Recommended rollback command:

```powershell
$env:TODOX_DB_CONNECTION = "<full connection string>"
.\database\yescale-image-demo\run_yescale_image_demo_setup.ps1 -Rollback -ConfirmProduction
$env:TODOX_DB_CONNECTION = $null
```

Rollback behavior:

- Restores previous defaults from `billing.yescale_image_default_snapshot`.
- Disables YEScale image capability rows not present in the snapshot.
- Does not delete wallet history, provider usage, billing records, attempts, or reconciliation history.

## Production Readiness Criteria

Mark ready only when all are true:

- `verify_yescale_image_demo.sql` passes.
- `poster_generation` is disabled for YEScale.
- Exactly one `TODOX_AI_IMAGE_SYSTEM` wallet exists for the tenant.
- Required permissions exist and are active.
- `billing.ai_image_billing_records` exists.
- `billing.ai_image_provider_attempts` exists.
- `billing.yescale_image_default_snapshot` exists.
- `nano-banana-2` is default for the six demo capabilities.
- `gpt-image` is enabled/selectable.
- `seedream-5` is enabled but not user-selectable.
- `AiProviders__YEScale__AccessKey` is configured in the production secret store.
- No real render/API call is made until the SQL verify and app build/test/publish steps pass.

