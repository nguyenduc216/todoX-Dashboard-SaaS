-- Manual SQL for KIE Dance Sell Phase 2 reference image versions.

BEGIN;

CREATE SCHEMA IF NOT EXISTS dance_sell;

CREATE TABLE IF NOT EXISTS dance_sell.dance_sell_reference_versions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    dance_sell_job_id uuid NOT NULL REFERENCES dance_sell.dance_sell_jobs(id) ON DELETE CASCADE,
    version_no integer NOT NULL,
    character_media_id uuid NULL REFERENCES media.media_files(id) ON DELETE SET NULL,
    product_media_id uuid NULL REFERENCES media.media_files(id) ON DELETE SET NULL,
    placement_mode text NOT NULL DEFAULT 'HOLD_PRODUCT',
    custom_instruction text NULL,
    prompt text NOT NULL,
    provider_code text NULL,
    provider_model text NULL,
    request_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    response_json jsonb NULL,
    error_json jsonb NULL,
    media_id uuid NULL REFERENCES media.media_files(id) ON DELETE SET NULL,
    object_key text NULL,
    public_url text NULL,
    status text NOT NULL DEFAULT 'generating',
    is_selected boolean NOT NULL DEFAULT false,
    created_by uuid NULL REFERENCES auth.app_users(id) ON DELETE SET NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz NULL,
    CONSTRAINT dance_sell_reference_versions_version_uk UNIQUE (dance_sell_job_id, version_no),
    CONSTRAINT dance_sell_reference_versions_status_ck CHECK (status IN ('generating','ready','approved','failed')),
    CONSTRAINT dance_sell_reference_versions_placement_ck CHECK (placement_mode IN ('HOLD_PRODUCT','WEAR_PRODUCT','DISPLAY_PRODUCT','USE_PRODUCT','CUSTOM'))
);

CREATE UNIQUE INDEX IF NOT EXISTS dance_sell_reference_versions_one_selected_uk
    ON dance_sell.dance_sell_reference_versions(dance_sell_job_id)
 WHERE is_selected = true;

CREATE INDEX IF NOT EXISTS dance_sell_reference_versions_status_ix
    ON dance_sell.dance_sell_reference_versions(status, created_at DESC);

CREATE INDEX IF NOT EXISTS dance_sell_reference_versions_job_created_ix
    ON dance_sell.dance_sell_reference_versions(dance_sell_job_id, created_at DESC);

COMMIT;
