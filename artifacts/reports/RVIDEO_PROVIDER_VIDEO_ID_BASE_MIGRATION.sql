-- RVIDEO provider identifier contract.
-- Review and execute manually in each target database after deployment.
-- This artifact is intentionally not executed by the application or build.

ALTER TABLE render.render_jobs
    ADD COLUMN IF NOT EXISTS provider_video_id_base text;

ALTER TABLE video_render.scene_video_versions
    ADD COLUMN IF NOT EXISTS provider_video_id_base text;
