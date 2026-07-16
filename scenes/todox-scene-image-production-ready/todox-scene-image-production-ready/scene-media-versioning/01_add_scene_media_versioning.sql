-- Additive scene image, scene video and final video versioning schema.
-- Standalone SQL patch; not an EF migration.
BEGIN;

ALTER TABLE video_render.video_projects
  ADD COLUMN IF NOT EXISTS selected_final_video_version_id uuid NULL;

ALTER TABLE video_render.video_project_scenes
  ADD COLUMN IF NOT EXISTS selected_image_version_id uuid NULL,
  ADD COLUMN IF NOT EXISTS selected_video_version_id uuid NULL;

CREATE TABLE IF NOT EXISTS video_render.scene_image_versions (
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
  requested_model text NULL,
  actual_model text NULL,
  provider_task_id text NULL,
  image_prompt_snapshot text NULL,
  video_prompt_snapshot text NULL,
  negative_prompt_snapshot text NULL,
  scene_snapshot_json jsonb NOT NULL DEFAULT '{}'::jsonb,
  reference_snapshot_json jsonb NOT NULL DEFAULT '[]'::jsonb,
  render_config_json jsonb NOT NULL DEFAULT '{}'::jsonb,
  result_media_id uuid NULL,
  storage_key text NULL,
  source_file_path text NULL,
  public_url text NULL,
  width integer NULL,
  height integer NULL,
  aspect_ratio text NULL,
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
  CONSTRAINT ux_scene_image_version_number UNIQUE(scene_id,version_number),
  CONSTRAINT ux_scene_image_logical_request UNIQUE(logical_request_id),
  CONSTRAINT ck_scene_image_version_number CHECK(version_number>0),
  CONSTRAINT ck_scene_image_status CHECK(status IN ('queued','submitted','processing','completed','failed','cancelled','pending_reconciliation')),
  CONSTRAINT ck_scene_image_cost CHECK(charged_points>=0 AND refunded_points>=0)
);

CREATE TABLE IF NOT EXISTS video_render.scene_video_versions (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  project_id bigint NOT NULL REFERENCES video_render.video_projects(id) ON DELETE CASCADE,
  scene_id bigint NOT NULL REFERENCES video_render.video_project_scenes(id) ON DELETE CASCADE,
  source_image_version_id uuid NULL REFERENCES video_render.scene_image_versions(id) ON DELETE RESTRICT,
  tenant_id uuid NOT NULL,
  customer_id uuid NULL,
  created_by uuid NULL,
  version_number integer NOT NULL,
  logical_request_id text NOT NULL,
  render_job_id uuid NULL REFERENCES render.render_jobs(id) ON DELETE SET NULL,
  provider_code text NULL,
  requested_model text NULL,
  actual_model text NULL,
  provider_task_id text NULL,
  image_prompt_snapshot text NULL,
  video_prompt_snapshot text NULL,
  scene_snapshot_json jsonb NOT NULL DEFAULT '{}'::jsonb,
  render_config_json jsonb NOT NULL DEFAULT '{}'::jsonb,
  result_media_id uuid NULL,
  poster_media_id uuid NULL,
  storage_key text NULL,
  source_file_path text NULL,
  public_url text NULL,
  poster_url text NULL,
  duration_seconds numeric(12,3) NULL,
  fps numeric(8,3) NULL,
  aspect_ratio text NULL,
  width integer NULL,
  height integer NULL,
  mime_type text NULL,
  voice_audio_version_id uuid NULL,
  subtitle_version_id uuid NULL,
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
  CONSTRAINT ux_scene_video_version_number UNIQUE(scene_id,version_number),
  CONSTRAINT ux_scene_video_logical_request UNIQUE(logical_request_id),
  CONSTRAINT ck_scene_video_version_number CHECK(version_number>0),
  CONSTRAINT ck_scene_video_status CHECK(status IN ('queued','submitted','processing','completed','failed','cancelled','pending_reconciliation')),
  CONSTRAINT ck_scene_video_cost CHECK(charged_points>=0 AND refunded_points>=0)
);

CREATE TABLE IF NOT EXISTS video_render.final_video_versions (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  project_id bigint NOT NULL REFERENCES video_render.video_projects(id) ON DELETE CASCADE,
  tenant_id uuid NOT NULL,
  customer_id uuid NULL,
  created_by uuid NULL,
  version_number integer NOT NULL,
  logical_request_id text NOT NULL,
  render_job_id uuid NULL REFERENCES render.render_jobs(id) ON DELETE SET NULL,
  composition_config_json jsonb NOT NULL DEFAULT '{}'::jsonb,
  transition_config_json jsonb NOT NULL DEFAULT '{}'::jsonb,
  audio_config_json jsonb NOT NULL DEFAULT '{}'::jsonb,
  subtitle_config_json jsonb NOT NULL DEFAULT '{}'::jsonb,
  result_media_id uuid NULL,
  poster_media_id uuid NULL,
  storage_key text NULL,
  source_file_path text NULL,
  public_url text NULL,
  poster_url text NULL,
  duration_seconds numeric(12,3) NULL,
  fps numeric(8,3) NULL,
  resolution text NULL,
  aspect_ratio text NULL,
  mime_type text NULL,
  status text NOT NULL DEFAULT 'queued',
  error_code text NULL,
  error_message text NULL,
  is_selected boolean NOT NULL DEFAULT false,
  selected_at timestamptz NULL,
  selected_by uuid NULL,
  completed_at timestamptz NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT ux_final_video_version_number UNIQUE(project_id,version_number),
  CONSTRAINT ux_final_video_logical_request UNIQUE(logical_request_id),
  CONSTRAINT ck_final_video_version_number CHECK(version_number>0),
  CONSTRAINT ck_final_video_status CHECK(status IN ('queued','processing','completed','failed','cancelled'))
);

CREATE TABLE IF NOT EXISTS video_render.final_video_version_items (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  final_video_version_id uuid NOT NULL REFERENCES video_render.final_video_versions(id) ON DELETE CASCADE,
  scene_id bigint NOT NULL REFERENCES video_render.video_project_scenes(id) ON DELETE RESTRICT,
  scene_video_version_id uuid NOT NULL REFERENCES video_render.scene_video_versions(id) ON DELETE RESTRICT,
  item_order integer NOT NULL,
  trim_start_seconds numeric(12,3) NULL,
  trim_end_seconds numeric(12,3) NULL,
  transition_in text NULL,
  transition_out text NULL,
  transition_duration_seconds numeric(12,3) NULL,
  volume numeric(8,4) NULL,
  config_json jsonb NOT NULL DEFAULT '{}'::jsonb,
  created_at timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT ux_final_video_item_order UNIQUE(final_video_version_id,item_order),
  CONSTRAINT ux_final_video_item_scene UNIQUE(final_video_version_id,scene_id),
  CONSTRAINT ck_final_video_item_order CHECK(item_order>=0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_scene_image_one_selected ON video_render.scene_image_versions(scene_id) WHERE is_selected;
CREATE UNIQUE INDEX IF NOT EXISTS ux_scene_video_one_selected ON video_render.scene_video_versions(scene_id) WHERE is_selected;
CREATE UNIQUE INDEX IF NOT EXISTS ux_final_video_one_selected ON video_render.final_video_versions(project_id) WHERE is_selected;
CREATE INDEX IF NOT EXISTS ix_scene_image_history ON video_render.scene_image_versions(scene_id,version_number DESC);
CREATE INDEX IF NOT EXISTS ix_scene_video_history ON video_render.scene_video_versions(scene_id,version_number DESC);
CREATE INDEX IF NOT EXISTS ix_scene_video_source_image ON video_render.scene_video_versions(source_image_version_id);
CREATE INDEX IF NOT EXISTS ix_final_video_history ON video_render.final_video_versions(project_id,version_number DESC);
CREATE INDEX IF NOT EXISTS ix_final_video_items_source ON video_render.final_video_version_items(scene_video_version_id);

DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_scene_selected_image_version') THEN
    ALTER TABLE video_render.video_project_scenes ADD CONSTRAINT fk_scene_selected_image_version FOREIGN KEY(selected_image_version_id) REFERENCES video_render.scene_image_versions(id) ON DELETE SET NULL;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_scene_selected_video_version') THEN
    ALTER TABLE video_render.video_project_scenes ADD CONSTRAINT fk_scene_selected_video_version FOREIGN KEY(selected_video_version_id) REFERENCES video_render.scene_video_versions(id) ON DELETE SET NULL;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_project_selected_final_video_version') THEN
    ALTER TABLE video_render.video_projects ADD CONSTRAINT fk_project_selected_final_video_version FOREIGN KEY(selected_final_video_version_id) REFERENCES video_render.final_video_versions(id) ON DELETE SET NULL;
  END IF;
END $$;

COMMIT;

