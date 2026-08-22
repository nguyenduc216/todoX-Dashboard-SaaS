-- Additive scene audio versioning schema for external voice / Vbee attempts.
-- Standalone SQL patch; not an EF migration.
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
  updated_at timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT ux_scene_audio_version_number UNIQUE(scene_id,version_number),
  CONSTRAINT ux_scene_audio_logical_request UNIQUE(logical_request_id),
  CONSTRAINT ck_scene_audio_version_number CHECK(version_number>0),
  CONSTRAINT ck_scene_audio_status CHECK(status IN ('queued','submitted','processing','completed','failed','cancelled','pending_reconciliation')),
  CONSTRAINT ck_scene_audio_cost CHECK(charged_points>=0 AND refunded_points>=0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_scene_audio_one_selected ON video_render.scene_audio_versions(scene_id) WHERE is_selected;
CREATE INDEX IF NOT EXISTS ix_scene_audio_history ON video_render.scene_audio_versions(scene_id,version_number DESC);
CREATE INDEX IF NOT EXISTS ix_scene_audio_logical_billing ON video_render.scene_audio_versions(billing_logical_request_id);
CREATE INDEX IF NOT EXISTS ix_scene_audio_provider_task ON video_render.scene_audio_versions(provider_task_id);

DO $$ BEGIN
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
