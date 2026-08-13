-- Phase 2B: Timelapse ordered render workflow.
-- Idempotent migration; does not modify existing Phase 2A draft job payloads.

CREATE SCHEMA IF NOT EXISTS timelapse;

ALTER TABLE render.render_jobs
    DROP CONSTRAINT IF EXISTS ck_render_jobs_status;

ALTER TABLE render.render_jobs
    ADD CONSTRAINT ck_render_jobs_status CHECK (
        status IN (
            'draft',
            'queued',
            'preparing',
            'rendering',
            'post_processing',
            'pending_reconciliation',
            'completed',
            'failed',
            'cancelled',
            'paused',
            'DRAFT',
            'GENERATING_IMAGES',
            'IMAGES_READY',
            'GENERATING_VIDEOS',
            'VIDEOS_READY',
            'FINALIZING',
            'COMPLETED',
            'PAUSED',
            'FAILED'
        )
    );

CREATE TABLE IF NOT EXISTS timelapse.timelapse_image_stages (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES system.tenants(id),
    job_id uuid NOT NULL REFERENCES render.render_jobs(id) ON DELETE CASCADE,
    stage_index integer NOT NULL,
    progress_percent integer NOT NULL,
    is_original boolean NOT NULL DEFAULT false,
    depends_on_progress_percent integer,
    status varchar(40) NOT NULL DEFAULT 'WAITING',
    active_attempt integer NOT NULL DEFAULT 0,
    active_version_id uuid,
    prompt_snapshot_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    provider_code varchar(80),
    provider_model varchar(160),
    provider_task_id varchar(200),
    result_media_id uuid REFERENCES media.media_files(id) ON DELETE SET NULL,
    object_key text,
    public_url text,
    error_code varchar(120),
    error_message text,
    created_at timestamptz NOT NULL DEFAULT now(),
    started_at timestamptz,
    completed_at timestamptz,
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_timelapse_image_stages_job_progress UNIQUE (job_id, progress_percent),
    CONSTRAINT ck_timelapse_image_stage_status CHECK (status IN ('WAITING','RENDERING','COMPLETED','FAILED','INVALIDATED'))
);

CREATE TABLE IF NOT EXISTS timelapse.timelapse_image_stage_versions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES system.tenants(id),
    image_stage_id uuid NOT NULL REFERENCES timelapse.timelapse_image_stages(id) ON DELETE CASCADE,
    job_id uuid NOT NULL REFERENCES render.render_jobs(id) ON DELETE CASCADE,
    attempt integer NOT NULL,
    status varchar(40) NOT NULL DEFAULT 'WAITING',
    prompt_snapshot_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    provider_code varchar(80),
    provider_model varchar(160),
    provider_task_id varchar(200),
    result_media_id uuid REFERENCES media.media_files(id) ON DELETE SET NULL,
    object_key text,
    public_url text,
    error_code varchar(120),
    error_message text,
    request_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    response_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    started_at timestamptz,
    completed_at timestamptz,
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_timelapse_image_stage_versions_attempt UNIQUE (image_stage_id, attempt),
    CONSTRAINT ck_timelapse_image_stage_version_status CHECK (status IN ('WAITING','RENDERING','COMPLETED','FAILED','INVALIDATED'))
);

CREATE TABLE IF NOT EXISTS timelapse.timelapse_video_clips (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES system.tenants(id),
    job_id uuid NOT NULL REFERENCES render.render_jobs(id) ON DELETE CASCADE,
    clip_index integer NOT NULL,
    start_progress_percent integer NOT NULL,
    end_progress_percent integer NOT NULL,
    status varchar(40) NOT NULL DEFAULT 'WAITING',
    active_attempt integer NOT NULL DEFAULT 0,
    active_version_id uuid,
    provider_code varchar(80),
    provider_model varchar(160),
    provider_task_id varchar(200),
    result_media_id uuid REFERENCES media.media_files(id) ON DELETE SET NULL,
    object_key text,
    public_url text,
    error_code varchar(120),
    error_message text,
    duration_seconds integer NOT NULL DEFAULT 6,
    video_mode varchar(40) NOT NULL,
    ratio varchar(40) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    started_at timestamptz,
    completed_at timestamptz,
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_timelapse_video_clips_job_clip UNIQUE (job_id, clip_index),
    CONSTRAINT ck_timelapse_video_clip_status CHECK (status IN ('WAITING','RENDERING','COMPLETED','FAILED','INVALIDATED'))
);

CREATE TABLE IF NOT EXISTS timelapse.timelapse_video_clip_versions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES system.tenants(id),
    video_clip_id uuid NOT NULL REFERENCES timelapse.timelapse_video_clips(id) ON DELETE CASCADE,
    job_id uuid NOT NULL REFERENCES render.render_jobs(id) ON DELETE CASCADE,
    attempt integer NOT NULL,
    status varchar(40) NOT NULL DEFAULT 'WAITING',
    provider_code varchar(80),
    provider_model varchar(160),
    provider_task_id varchar(200),
    result_media_id uuid REFERENCES media.media_files(id) ON DELETE SET NULL,
    object_key text,
    public_url text,
    error_code varchar(120),
    error_message text,
    request_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    response_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    started_at timestamptz,
    completed_at timestamptz,
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_timelapse_video_clip_versions_attempt UNIQUE (video_clip_id, attempt),
    CONSTRAINT ck_timelapse_video_clip_version_status CHECK (status IN ('WAITING','RENDERING','COMPLETED','FAILED','INVALIDATED'))
);

CREATE TABLE IF NOT EXISTS timelapse.timelapse_final_outputs (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES system.tenants(id),
    job_id uuid NOT NULL REFERENCES render.render_jobs(id) ON DELETE CASCADE,
    version integer NOT NULL,
    status varchar(40) NOT NULL DEFAULT 'WAITING',
    result_media_id uuid REFERENCES media.media_files(id) ON DELETE SET NULL,
    object_key text,
    public_url text,
    error_code varchar(120),
    error_message text,
    request_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    response_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    started_at timestamptz,
    completed_at timestamptz,
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_timelapse_final_outputs_job_version UNIQUE (job_id, version),
    CONSTRAINT ck_timelapse_final_output_status CHECK (status IN ('WAITING','RENDERING','COMPLETED','FAILED','INVALIDATED'))
);

CREATE INDEX IF NOT EXISTS idx_timelapse_image_stages_job
    ON timelapse.timelapse_image_stages(job_id, stage_index);

CREATE INDEX IF NOT EXISTS idx_timelapse_video_clips_job
    ON timelapse.timelapse_video_clips(job_id, clip_index);

CREATE INDEX IF NOT EXISTS idx_timelapse_final_outputs_job
    ON timelapse.timelapse_final_outputs(job_id, version DESC);

COMMENT ON TABLE timelapse.timelapse_image_stages IS
    'Current active Timelapse image stage state; 100 percent is customer original image.';

COMMENT ON TABLE timelapse.timelapse_image_stage_versions IS
    'Attempt/version history for Timelapse generated image stages.';

COMMENT ON TABLE timelapse.timelapse_video_clips IS
    'Current active Timelapse video clip state between consecutive image stages.';

COMMENT ON TABLE timelapse.timelapse_video_clip_versions IS
    'Attempt/version history for Timelapse video clips.';

COMMENT ON TABLE timelapse.timelapse_final_outputs IS
    'Versioned final merged Timelapse outputs.';
