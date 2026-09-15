-- RVIDEO direct image-to-video source. Run manually; this task does not execute SQL.
ALTER TABLE video_render.video_projects
    ADD COLUMN IF NOT EXISTS source_image_url text NULL;
