-- Align legacy scene audio version rows/tables with the current runtime contract.
-- Additive compatibility script only. Review and execute manually in production.
BEGIN;

ALTER TABLE video_render.video_project_scenes
  ADD COLUMN IF NOT EXISTS selected_audio_version_id uuid NULL;

ALTER TABLE video_render.scene_video_versions
  ADD COLUMN IF NOT EXISTS voice_audio_version_id uuid NULL;

CREATE TABLE IF NOT EXISTS video_render.scene_audio_versions (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  project_id bigint NOT NULL REFERENCES video_render.video_projects(id) ON DELETE CASCADE,
  scene_id bigint NOT NULL REFERENCES video_render.video_project_scenes(id) ON DELETE CASCADE,
  tenant_id uuid NOT NULL,
  customer_id uuid NULL,
  created_by uuid NULL,
  version_number integer NOT NULL,
  logical_request_id text NOT NULL,
  render_job_id uuid NULL REFERENCES render.render_jobs(id) ON DELETE SET NULL,
  provider_code text NULL,
  provider_capability_id bigint NULL,
  requested_model text NULL,
  actual_model text NULL,
  provider_task_id text NULL,
  voice_catalog_code text NULL,
  voice_snapshot_json jsonb NULL,
  narration_text_snapshot text NULL,
  voice_instruction_snapshot text NULL,
  tts_rate numeric(6,3) NULL,
  duration_seconds numeric(12,3) NULL,
  scene_snapshot_json jsonb NOT NULL DEFAULT '{}'::jsonb,
  render_config_json jsonb NOT NULL DEFAULT '{}'::jsonb,
  result_media_id uuid NULL,
  storage_key text NULL,
  source_file_path text NULL,
  public_url text NULL,
  mime_type text NULL,
  billing_logical_request_id text NULL,
  estimated_usd numeric(18,6) NULL,
  actual_usd numeric(18,6) NULL,
  charged_points numeric(18,6) NOT NULL DEFAULT 0,
  refunded_points numeric(18,6) NOT NULL DEFAULT 0,
  cost_source text NULL,
  status text NOT NULL DEFAULT 'queued',
  error_code text NULL,
  error_message text NULL,
  is_selected boolean NOT NULL DEFAULT false,
  selected_at timestamptz NULL,
  selected_by uuid NULL,
  submitted_at timestamptz NULL,
  completed_at timestamptz NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE video_render.scene_audio_versions
  ADD COLUMN IF NOT EXISTS id uuid DEFAULT gen_random_uuid(),
  ADD COLUMN IF NOT EXISTS project_id bigint,
  ADD COLUMN IF NOT EXISTS scene_id bigint,
  ADD COLUMN IF NOT EXISTS tenant_id uuid,
  ADD COLUMN IF NOT EXISTS customer_id uuid NULL,
  ADD COLUMN IF NOT EXISTS created_by uuid NULL,
  ADD COLUMN IF NOT EXISTS version_number integer,
  ADD COLUMN IF NOT EXISTS logical_request_id text,
  ADD COLUMN IF NOT EXISTS render_job_id uuid NULL,
  ADD COLUMN IF NOT EXISTS provider_code text NULL,
  ADD COLUMN IF NOT EXISTS provider_capability_id bigint NULL,
  ADD COLUMN IF NOT EXISTS requested_model text NULL,
  ADD COLUMN IF NOT EXISTS actual_model text NULL,
  ADD COLUMN IF NOT EXISTS provider_task_id text NULL,
  ADD COLUMN IF NOT EXISTS voice_catalog_code text NULL,
  ADD COLUMN IF NOT EXISTS voice_snapshot_json jsonb NULL,
  ADD COLUMN IF NOT EXISTS narration_text_snapshot text NULL,
  ADD COLUMN IF NOT EXISTS voice_instruction_snapshot text NULL,
  ADD COLUMN IF NOT EXISTS tts_rate numeric(6,3) NULL,
  ADD COLUMN IF NOT EXISTS duration_seconds numeric(12,3) NULL,
  ADD COLUMN IF NOT EXISTS scene_snapshot_json jsonb NOT NULL DEFAULT '{}'::jsonb,
  ADD COLUMN IF NOT EXISTS render_config_json jsonb NOT NULL DEFAULT '{}'::jsonb,
  ADD COLUMN IF NOT EXISTS result_media_id uuid NULL,
  ADD COLUMN IF NOT EXISTS storage_key text NULL,
  ADD COLUMN IF NOT EXISTS source_file_path text NULL,
  ADD COLUMN IF NOT EXISTS public_url text NULL,
  ADD COLUMN IF NOT EXISTS mime_type text NULL,
  ADD COLUMN IF NOT EXISTS billing_logical_request_id text NULL,
  ADD COLUMN IF NOT EXISTS estimated_usd numeric(18,6) NULL,
  ADD COLUMN IF NOT EXISTS actual_usd numeric(18,6) NULL,
  ADD COLUMN IF NOT EXISTS charged_points numeric(18,6) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS refunded_points numeric(18,6) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS cost_source text NULL,
  ADD COLUMN IF NOT EXISTS status text NOT NULL DEFAULT 'queued',
  ADD COLUMN IF NOT EXISTS error_code text NULL,
  ADD COLUMN IF NOT EXISTS error_message text NULL,
  ADD COLUMN IF NOT EXISTS is_selected boolean NOT NULL DEFAULT false,
  ADD COLUMN IF NOT EXISTS selected_at timestamptz NULL,
  ADD COLUMN IF NOT EXISTS selected_by uuid NULL,
  ADD COLUMN IF NOT EXISTS submitted_at timestamptz NULL,
  ADD COLUMN IF NOT EXISTS completed_at timestamptz NULL,
  ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now(),
  ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT now();

DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM information_schema.columns
     WHERE table_schema='video_render' AND table_name='scene_audio_versions' AND column_name='provider_request_id'
  ) THEN
    EXECUTE 'UPDATE video_render.scene_audio_versions SET provider_task_id=COALESCE(provider_task_id, provider_request_id) WHERE provider_task_id IS NULL';
  END IF;

  IF EXISTS (
    SELECT 1 FROM information_schema.columns
     WHERE table_schema='video_render' AND table_name='scene_audio_versions' AND column_name='voice_code_snapshot'
  ) THEN
    EXECUTE 'UPDATE video_render.scene_audio_versions SET voice_catalog_code=COALESCE(voice_catalog_code, voice_code_snapshot) WHERE voice_catalog_code IS NULL';
  END IF;

  IF EXISTS (
    SELECT 1 FROM information_schema.columns
     WHERE table_schema='video_render' AND table_name='scene_audio_versions' AND column_name='voice_text_snapshot'
  ) THEN
    EXECUTE 'UPDATE video_render.scene_audio_versions SET narration_text_snapshot=COALESCE(narration_text_snapshot, voice_text_snapshot) WHERE narration_text_snapshot IS NULL';
  END IF;

  IF EXISTS (
    SELECT 1 FROM information_schema.columns
     WHERE table_schema='video_render' AND table_name='scene_audio_versions' AND column_name='audio_config_json'
  ) THEN
    EXECUTE 'UPDATE video_render.scene_audio_versions SET render_config_json=COALESCE(render_config_json, audio_config_json, ''{}''::jsonb) WHERE render_config_json IS NULL OR render_config_json = ''{}''::jsonb';
  END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_scene_audio_one_selected
  ON video_render.scene_audio_versions(scene_id) WHERE is_selected;
CREATE INDEX IF NOT EXISTS ix_scene_audio_history
  ON video_render.scene_audio_versions(scene_id, version_number DESC);
CREATE INDEX IF NOT EXISTS ix_scene_audio_logical_request
  ON video_render.scene_audio_versions(tenant_id, logical_request_id);
CREATE INDEX IF NOT EXISTS ix_scene_audio_logical_billing
  ON video_render.scene_audio_versions(billing_logical_request_id);
CREATE INDEX IF NOT EXISTS ix_scene_audio_provider_task
  ON video_render.scene_audio_versions(provider_task_id);
CREATE INDEX IF NOT EXISTS ix_scene_audio_render_job
  ON video_render.scene_audio_versions(render_job_id);

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_scene_audio_version') THEN
    ALTER TABLE video_render.video_project_scenes
      ADD CONSTRAINT fk_scene_audio_version
      FOREIGN KEY (selected_audio_version_id) REFERENCES video_render.scene_audio_versions(id) ON DELETE SET NULL;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_scene_video_voice_audio_version') THEN
    ALTER TABLE video_render.scene_video_versions
      ADD CONSTRAINT fk_scene_video_voice_audio_version
      FOREIGN KEY (voice_audio_version_id) REFERENCES video_render.scene_audio_versions(id) ON DELETE SET NULL;
  END IF;
END $$;

COMMIT;
