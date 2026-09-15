-- RVIDEO Dashboard Phase 1 additive migration.
-- Do not execute automatically. Run manually after reviewing the deployed video_render schema.

CREATE TABLE IF NOT EXISTS video_render.rvideo_job_settings (
    project_id bigint PRIMARY KEY REFERENCES video_render.video_projects(id) ON DELETE CASCADE,
    tenant_id uuid NOT NULL,
    execution_mode text NOT NULL DEFAULT 'MANUAL'
        CHECK (execution_mode IN ('MANUAL', 'AUTO')),
    current_stage text NOT NULL DEFAULT 'INFO'
        CHECK (current_stage IN ('INFO', 'SCENE', 'IMAGE', 'VIDEO', 'RESULT')),
    skip_character boolean NOT NULL DEFAULT false,
    character_mode text NOT NULL DEFAULT 'NONE',
    selected_character_id bigint NULL,
    character_snapshot_json jsonb NULL,
    voice_mode text NOT NULL DEFAULT 'NONE'
        CHECK (voice_mode IN ('NONE', 'NATIVE', 'LIBRARY')),
    voice_catalog_code text NULL,
    voice_snapshot_json jsonb NULL,
    default_tts_rate numeric(6,3) NOT NULL DEFAULT 1.0,
    music_catalog_code text NULL,
    music_snapshot_json jsonb NULL,
    music_volume numeric(5,4) NOT NULL DEFAULT 0.8
        CHECK (music_volume >= 0 AND music_volume <= 1),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_rvideo_settings_tenant_mode
    ON video_render.rvideo_job_settings(tenant_id, execution_mode, updated_at DESC);
