-- Phase 2.1.1: link prompt generations to video project workspaces.
-- Manual execution only. This script is additive and does not alter render/billing tables.

ALTER TABLE settings.service_prompt_generations
    ADD COLUMN IF NOT EXISTS video_project_id bigint NULL;

CREATE INDEX IF NOT EXISTS ix_service_prompt_generations_video_project
    ON settings.service_prompt_generations(video_project_id, created_at DESC);

ALTER TABLE video_render.video_projects
    ADD COLUMN IF NOT EXISTS active_prompt_generation_id uuid NULL;
