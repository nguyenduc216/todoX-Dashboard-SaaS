-- Manual schema update for RVIDEO shared reference image mode.
-- Run this once after reviewing the current deployed schema.

ALTER TABLE video_render.rvideo_job_settings
    ADD COLUMN IF NOT EXISTS use_reference_image_for_all_scenes boolean NOT NULL DEFAULT false;

