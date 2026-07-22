BEGIN;
SET LOCAL statement_timeout = '10min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE SCHEMA IF NOT EXISTS dance_sell;

DROP TABLE IF EXISTS dance_sell.dance_sell_reference_versions CASCADE;
DROP TABLE IF EXISTS dance_sell.dance_sell_jobs CASCADE;

CREATE TABLE dance_sell.dance_sell_jobs (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NULL REFERENCES system.tenants(id),
    customer_id uuid NULL,
    user_id uuid NULL,
    title text NULL,
    reference_mode text NOT NULL DEFAULT 'GENERATE_REFERENCE',
    character_media_id uuid NULL,
    product_media_id uuid NULL,
    direct_reference_media_id uuid NULL,
    motion_media_id uuid NULL,
    selected_reference_version_id uuid NULL,
    reference_render_job_id uuid NULL REFERENCES render.render_jobs(id) ON DELETE SET NULL,
    motion_render_job_id uuid NULL REFERENCES render.render_jobs(id) ON DELETE SET NULL,
    placement_mode text NOT NULL DEFAULT 'HOLD_PRODUCT',
    image_prompt text NULL,
    video_prompt text NULL,
    prompt text NULL,
    mode text NOT NULL DEFAULT '720p',
    orientation text NOT NULL DEFAULT 'image',
    character_orientation text GENERATED ALWAYS AS (orientation) STORED,
    current_stage text NOT NULL DEFAULT 'draft',
    status text NOT NULL DEFAULT 'draft',
    result_media_id uuid NULL,
    result_url text NULL,
    result_video_url text GENERATED ALWAYS AS (result_url) STORED,
    error_code text NULL,
    error_message text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz NULL,
    CONSTRAINT dance_sell_jobs_reference_mode_ck CHECK (reference_mode IN ('GENERATE_REFERENCE','DIRECT_REFERENCE','generate_reference','direct_reference')),
    CONSTRAINT dance_sell_jobs_mode_ck CHECK (mode IN ('720p','1080p')),
    CONSTRAINT dance_sell_jobs_orientation_ck CHECK (orientation IN ('image','video')),
    CONSTRAINT dance_sell_jobs_status_ck CHECK (status IN ('draft','queued','reference_generating','reference_ready','rendering','completed','failed','cancelled'))
);

CREATE TABLE dance_sell.dance_sell_reference_versions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    dance_sell_job_id uuid NOT NULL REFERENCES dance_sell.dance_sell_jobs(id) ON DELETE CASCADE,
    render_job_id uuid NULL REFERENCES render.render_jobs(id) ON DELETE SET NULL,
    version_no integer NOT NULL,
    media_id uuid NULL,
    object_key text NULL,
    public_url text NULL,
    status text NOT NULL DEFAULT 'generating',
    is_selected boolean NOT NULL DEFAULT false,
    created_by uuid NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT dance_sell_reference_versions_status_ck CHECK (status IN ('generating','ready','approved','failed'))
);

ALTER TABLE dance_sell.dance_sell_jobs
    ADD CONSTRAINT dance_sell_jobs_selected_reference_fkey
    FOREIGN KEY (selected_reference_version_id)
    REFERENCES dance_sell.dance_sell_reference_versions(id)
    ON DELETE SET NULL;

COMMIT;
