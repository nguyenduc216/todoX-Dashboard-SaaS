BEGIN;
SET LOCAL statement_timeout = '10min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE SCHEMA IF NOT EXISTS dance_sell;

ALTER TABLE dance_sell.dance_sell_jobs
    ADD COLUMN IF NOT EXISTS reference_render_job_id uuid NULL REFERENCES render.render_jobs(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS motion_render_job_id uuid NULL REFERENCES render.render_jobs(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS selected_reference_version_id uuid NULL,
    ADD COLUMN IF NOT EXISTS result_media_id uuid NULL,
    ADD COLUMN IF NOT EXISTS result_video_url text NULL,
    ADD COLUMN IF NOT EXISTS business_status text NULL,
    ADD COLUMN IF NOT EXISTS selected_reference_provider_code text NULL,
    ADD COLUMN IF NOT EXISTS selected_reference_model text NULL,
    ADD COLUMN IF NOT EXISTS selected_motion_provider_code text NULL,
    ADD COLUMN IF NOT EXISTS selected_motion_model text NULL;

CREATE TABLE IF NOT EXISTS dance_sell.dance_sell_reference_versions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    dance_sell_job_id uuid NOT NULL REFERENCES dance_sell.dance_sell_jobs(id) ON DELETE CASCADE,
    render_job_id uuid NULL REFERENCES render.render_jobs(id) ON DELETE SET NULL,
    version_no integer NOT NULL,
    status text NOT NULL DEFAULT 'generating',
    media_id uuid NULL,
    object_key text NULL,
    public_url text NULL,
    provider_url text NULL,
    prompt text NULL,
    placement_mode text NULL,
    is_selected boolean NOT NULL DEFAULT false,
    approved_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT dance_sell_reference_versions_status_ck CHECK (status IN ('generating','ready','approved','failed'))
);

COMMIT;
