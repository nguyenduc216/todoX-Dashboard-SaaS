-- Idempotent runtime contract for RDance/Fashion Dance reference preparation.
-- Manual execution only. Do not run automatically from the application.

CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE SCHEMA IF NOT EXISTS dance_sell;

CREATE TABLE IF NOT EXISTS dance_sell.dance_sell_provider_operations (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    dance_sell_job_id uuid NOT NULL REFERENCES dance_sell.dance_sell_jobs(id) ON DELETE CASCADE,
    render_job_id uuid NULL,
    parent_operation_id uuid NULL REFERENCES dance_sell.dance_sell_provider_operations(id),
    operation_type text NOT NULL,
    attempt_no integer NOT NULL DEFAULT 1,
    reference_mode text NULL,
    provider_code text NOT NULL,
    provider_capability_id uuid NULL,
    provider_account_id uuid NULL,
    provider_model text NOT NULL,
    provider_task_id text NULL,
    status text NOT NULL,
    provider_status text NULL,
    billing_status text NOT NULL,
    refund_status text NOT NULL,
    request_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    response_json jsonb NULL,
    callback_json jsonb NULL,
    error_json jsonb NULL,
    provider_usage_json jsonb NULL,
    pricing_snapshot_json jsonb NULL,
    usage_quantity numeric(20,8),
    usage_unit text,
    credits_estimated numeric(20,8),
    credits_consumed numeric(20,8),
    provider_cost numeric(20,8),
    provider_currency text,
    provider_cost_vnd numeric(20,2),
    exchange_rate numeric(20,8),
    todox_points_estimated numeric(20,4),
    todox_points_reserved numeric(20,4),
    todox_points_charged numeric(20,4),
    todox_points_refunded numeric(20,4),
    balance_before numeric(20,8),
    balance_after numeric(20,8),
    cost_source text,
    error_code text,
    error_message text,
    created_at timestamptz NOT NULL DEFAULT now(),
    started_at timestamptz,
    submitted_at timestamptz,
    completed_at timestamptz,
    failed_at timestamptz,
    refunded_at timestamptz,
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS dance_sell_provider_operations_task_idx
    ON dance_sell.dance_sell_provider_operations(provider_code, provider_task_id)
    WHERE provider_task_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS dance_sell_provider_operations_attempt_idx
    ON dance_sell.dance_sell_provider_operations(dance_sell_job_id, operation_type, attempt_no);

CREATE INDEX IF NOT EXISTS dance_sell_provider_operations_job_idx ON dance_sell.dance_sell_provider_operations(dance_sell_job_id, created_at);
CREATE INDEX IF NOT EXISTS dance_sell_provider_operations_render_idx ON dance_sell.dance_sell_provider_operations(render_job_id);
CREATE INDEX IF NOT EXISTS dance_sell_provider_operations_provider_idx ON dance_sell.dance_sell_provider_operations(provider_code, provider_model);
CREATE INDEX IF NOT EXISTS dance_sell_provider_operations_status_idx ON dance_sell.dance_sell_provider_operations(status, created_at);
CREATE INDEX IF NOT EXISTS dance_sell_provider_operations_billing_idx ON dance_sell.dance_sell_provider_operations(billing_status, refund_status);
CREATE INDEX IF NOT EXISTS dance_sell_provider_operations_type_idx ON dance_sell.dance_sell_provider_operations(operation_type, created_at);
CREATE INDEX IF NOT EXISTS dance_sell_provider_operations_error_idx ON dance_sell.dance_sell_provider_operations(error_code);
CREATE INDEX IF NOT EXISTS dance_sell_provider_operations_created_idx ON dance_sell.dance_sell_provider_operations(created_at);

CREATE TABLE IF NOT EXISTS public.todox_ai_operation_assets (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    operation_id uuid NOT NULL REFERENCES dance_sell.dance_sell_provider_operations(id) ON DELETE CASCADE,
    asset_role text NOT NULL,
    media_id uuid NULL,
    object_key text NULL,
    public_url text NULL,
    provider_url text NULL,
    mime_type text NULL,
    metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS todox_ai_operation_assets_unique_idx
    ON public.todox_ai_operation_assets(
        operation_id,
        asset_role,
        COALESCE(media_id, '00000000-0000-0000-0000-000000000000'::uuid),
        COALESCE(public_url, ''),
        COALESCE(provider_url, '')
    );

CREATE INDEX IF NOT EXISTS todox_ai_operation_assets_operation_idx ON public.todox_ai_operation_assets(operation_id, created_at);
CREATE INDEX IF NOT EXISTS todox_ai_operation_assets_role_idx ON public.todox_ai_operation_assets(asset_role);
