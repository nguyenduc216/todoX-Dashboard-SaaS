CREATE SCHEMA IF NOT EXISTS render;

CREATE TABLE IF NOT EXISTS render.marketing_image_render_logs
(
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    user_id uuid NULL,
    customer_id uuid NULL,
    render_job_id uuid NOT NULL,
    log_code text NOT NULL,
    status text NOT NULL,
    service_type text NULL,
    service_name text NULL,
    request_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    render_plan_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    compiled_prompt text NULL,
    result_media_id uuid NULL,
    result_url text NULL,
    logs_json jsonb NOT NULL DEFAULT '[]'::jsonb,
    error_message text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz NULL
);

CREATE INDEX IF NOT EXISTS ix_marketing_image_render_logs_tenant_created
    ON render.marketing_image_render_logs (tenant_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_marketing_image_render_logs_user_created
    ON render.marketing_image_render_logs (user_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_marketing_image_render_logs_log_code
    ON render.marketing_image_render_logs (log_code);
