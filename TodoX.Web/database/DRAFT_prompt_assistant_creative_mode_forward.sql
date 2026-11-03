-- DRAFT ONLY. Operator must verify the deployed schema before manual execution.
ALTER TABLE video_render.rvideo_job_settings
    ADD COLUMN creative_mode boolean NOT NULL DEFAULT false;