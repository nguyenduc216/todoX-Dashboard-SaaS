# Target AI core schema

## Keep and normalize

- `public.todox_ai_provider`: provider registry only, no per-token assumption.
- `public.todox_ai_provider_capability`: model/capability metadata, operation type, feature codes, endpoint, pricing/runtime JSON.
- `render.render_jobs`: one provider task equals one render job.
- `render.render_job_steps`, `render.render_job_events`, `render.render_job_inputs`, `render.render_artifacts`: shared execution inputs/timeline/artifacts.
- `public.todox_ai_provider_usage_log`: generic usage with unit differentiation.
- `billing.token_wallets`, `billing.token_transactions`, `billing.token_usage_logs`: generic point wallet ledger.
- `dance_sell.dance_sell_jobs`, `dance_sell.dance_sell_reference_versions`: business state only.

## Create

- `public.todox_ai_provider_account`
- `public.todox_ai_provider_account_credential`
- `public.todox_ai_provider_account_lease`
- `public.todox_ai_provider_balance_ledger`
- `billing.ai_billing_records`
- `billing.ai_provider_attempts`

## Drop after code switch/reset

- `public.todox_ai_feature_provider_route`
- `dance_sell.dance_sell_provider_operations`
- `public.todox_ai_operation_assets`
- `public.todox_ai_operation_billing_transactions`
- `billing.ai_image_billing_records` and `billing.ai_image_provider_attempts` after generic billing replacement is verified.

## Usage units

Allowed runtime units must include `credits`, `tokens`, `token_1000`, `requests`, `images`, `seconds`, `video_seconds`, `minutes`, `fixed`, and currency cost fields.

## Concurrency

Provider account claim must use a PostgreSQL transaction plus `FOR UPDATE SKIP LOCKED`; active leases are persisted in `public.todox_ai_provider_account_lease`, not only in memory.
