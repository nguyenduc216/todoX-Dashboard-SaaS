ALTER TABLE dance_sell.dance_sell_jobs
    ADD COLUMN IF NOT EXISTS reference_mode text NOT NULL DEFAULT 'GENERATE_REFERENCE',
    ADD COLUMN IF NOT EXISTS direct_reference_media_id uuid NULL,
    ADD COLUMN IF NOT EXISTS direct_reference_object_key text NULL,
    ADD COLUMN IF NOT EXISTS direct_reference_url text NULL,
    ADD COLUMN IF NOT EXISTS reference_provider_code text NULL,
    ADD COLUMN IF NOT EXISTS reference_provider_model text NULL,
    ADD COLUMN IF NOT EXISTS reference_provider_capability_id uuid NULL,
    ADD COLUMN IF NOT EXISTS reference_provider_account_id uuid NULL,
    ADD COLUMN IF NOT EXISTS motion_provider_code text NULL,
    ADD COLUMN IF NOT EXISTS motion_provider_model text NULL,
    ADD COLUMN IF NOT EXISTS motion_provider_capability_id uuid NULL,
    ADD COLUMN IF NOT EXISTS motion_provider_account_id uuid NULL,
    ADD COLUMN IF NOT EXISTS image_prompt text NULL,
    ADD COLUMN IF NOT EXISTS reference_approved_at timestamptz NULL,
    ADD COLUMN IF NOT EXISTS total_provider_usage numeric(20,8),
    ADD COLUMN IF NOT EXISTS total_provider_cost numeric(20,8),
    ADD COLUMN IF NOT EXISTS total_provider_currency text,
    ADD COLUMN IF NOT EXISTS total_provider_cost_vnd numeric(20,2),
    ADD COLUMN IF NOT EXISTS total_todox_points_estimated numeric(20,4),
    ADD COLUMN IF NOT EXISTS total_todox_points_charged numeric(20,4),
    ADD COLUMN IF NOT EXISTS total_todox_points_refunded numeric(20,4),
    ADD COLUMN IF NOT EXISTS current_stage text NOT NULL DEFAULT 'draft',
    ADD COLUMN IF NOT EXISTS billing_status text NOT NULL DEFAULT 'not_required',
    ADD COLUMN IF NOT EXISTS refund_status text NOT NULL DEFAULT 'not_required';

CREATE INDEX IF NOT EXISTS dance_sell_jobs_reference_mode_idx ON dance_sell.dance_sell_jobs(reference_mode, created_at);
CREATE INDEX IF NOT EXISTS dance_sell_jobs_current_stage_idx ON dance_sell.dance_sell_jobs(current_stage, created_at);
CREATE INDEX IF NOT EXISTS dance_sell_jobs_billing_idx ON dance_sell.dance_sell_jobs(billing_status, refund_status);
