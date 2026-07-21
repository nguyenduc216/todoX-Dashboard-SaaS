-- Manual SQL for KIE Dance Sell Phase 2.
-- Idempotent extension only; do not run automatically from application startup.

BEGIN;

CREATE SCHEMA IF NOT EXISTS dance_sell;

ALTER TABLE dance_sell.dance_sell_jobs ADD COLUMN IF NOT EXISTS title text NULL;
ALTER TABLE dance_sell.dance_sell_jobs ADD COLUMN IF NOT EXISTS character_media_id uuid NULL REFERENCES media.media_files(id) ON DELETE SET NULL;
ALTER TABLE dance_sell.dance_sell_jobs ADD COLUMN IF NOT EXISTS character_object_key text NULL;
ALTER TABLE dance_sell.dance_sell_jobs ADD COLUMN IF NOT EXISTS product_media_id uuid NULL REFERENCES media.media_files(id) ON DELETE SET NULL;
ALTER TABLE dance_sell.dance_sell_jobs ADD COLUMN IF NOT EXISTS product_object_key text NULL;
ALTER TABLE dance_sell.dance_sell_jobs ADD COLUMN IF NOT EXISTS product_image_url text NULL;
ALTER TABLE dance_sell.dance_sell_jobs ADD COLUMN IF NOT EXISTS motion_source_type text NULL;
ALTER TABLE dance_sell.dance_sell_jobs ADD COLUMN IF NOT EXISTS motion_source_url text NULL;
ALTER TABLE dance_sell.dance_sell_jobs ADD COLUMN IF NOT EXISTS motion_video_media_id uuid NULL REFERENCES media.media_files(id) ON DELETE SET NULL;
ALTER TABLE dance_sell.dance_sell_jobs ADD COLUMN IF NOT EXISTS motion_video_object_key text NULL;
ALTER TABLE dance_sell.dance_sell_jobs ADD COLUMN IF NOT EXISTS placement_mode text NOT NULL DEFAULT 'HOLD_PRODUCT';
ALTER TABLE dance_sell.dance_sell_jobs ADD COLUMN IF NOT EXISTS custom_placement_instruction text NULL;
ALTER TABLE dance_sell.dance_sell_jobs ADD COLUMN IF NOT EXISTS prepared_reference_media_id uuid NULL REFERENCES media.media_files(id) ON DELETE SET NULL;
ALTER TABLE dance_sell.dance_sell_jobs ADD COLUMN IF NOT EXISTS prepared_reference_object_key text NULL;
ALTER TABLE dance_sell.dance_sell_jobs ADD COLUMN IF NOT EXISTS prepared_reference_url text NULL;
ALTER TABLE dance_sell.dance_sell_jobs ADD COLUMN IF NOT EXISTS prepared_reference_status text NOT NULL DEFAULT 'not_created';
ALTER TABLE dance_sell.dance_sell_jobs ADD COLUMN IF NOT EXISTS prepared_reference_approved_at timestamptz NULL;
ALTER TABLE dance_sell.dance_sell_jobs ADD COLUMN IF NOT EXISTS source_stage_status text NOT NULL DEFAULT 'pending';
ALTER TABLE dance_sell.dance_sell_jobs ADD COLUMN IF NOT EXISTS source_stage_error text NULL;
ALTER TABLE dance_sell.dance_sell_jobs ADD COLUMN IF NOT EXISTS created_by uuid NULL REFERENCES auth.app_users(id) ON DELETE SET NULL;
ALTER TABLE dance_sell.dance_sell_jobs ADD COLUMN IF NOT EXISTS updated_by uuid NULL REFERENCES auth.app_users(id) ON DELETE SET NULL;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'dance_sell_jobs_status_ck'
          AND conrelid = 'dance_sell.dance_sell_jobs'::regclass
    ) THEN
        ALTER TABLE dance_sell.dance_sell_jobs DROP CONSTRAINT dance_sell_jobs_status_ck;
    END IF;

    ALTER TABLE dance_sell.dance_sell_jobs
        ADD CONSTRAINT dance_sell_jobs_status_ck
        CHECK (status IN ('draft','queued','submitted','rendering','completed','failed','timeout'));
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'dance_sell_jobs_motion_source_type_ck'
          AND conrelid = 'dance_sell.dance_sell_jobs'::regclass
    ) THEN
        ALTER TABLE dance_sell.dance_sell_jobs
            ADD CONSTRAINT dance_sell_jobs_motion_source_type_ck
            CHECK (motion_source_type IS NULL OR motion_source_type IN ('upload','tiktok'));
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'dance_sell_jobs_reference_status_ck'
          AND conrelid = 'dance_sell.dance_sell_jobs'::regclass
    ) THEN
        ALTER TABLE dance_sell.dance_sell_jobs
            ADD CONSTRAINT dance_sell_jobs_reference_status_ck
            CHECK (prepared_reference_status IN ('not_created','generating','ready','approved','failed'));
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'dance_sell_jobs_source_stage_status_ck'
          AND conrelid = 'dance_sell.dance_sell_jobs'::regclass
    ) THEN
        ALTER TABLE dance_sell.dance_sell_jobs
            ADD CONSTRAINT dance_sell_jobs_source_stage_status_ck
            CHECK (source_stage_status IN ('pending','resolving','downloading','staging','ready','failed'));
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'dance_sell_jobs_placement_mode_ck'
          AND conrelid = 'dance_sell.dance_sell_jobs'::regclass
    ) THEN
        ALTER TABLE dance_sell.dance_sell_jobs
            ADD CONSTRAINT dance_sell_jobs_placement_mode_ck
            CHECK (placement_mode IN ('HOLD_PRODUCT','WEAR_PRODUCT','DISPLAY_PRODUCT','USE_PRODUCT','CUSTOM'));
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS dance_sell_jobs_reference_status_ix
    ON dance_sell.dance_sell_jobs(prepared_reference_status, created_at DESC);

CREATE INDEX IF NOT EXISTS dance_sell_jobs_motion_source_ix
    ON dance_sell.dance_sell_jobs(motion_source_type, source_stage_status, created_at DESC);

COMMIT;
