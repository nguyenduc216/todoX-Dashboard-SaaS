-- Source-controlled manual migration for Construction Timelapse user cancellation.
-- Run manually in each environment before enabling the UI cancellation controls.

DO $$
BEGIN
    ALTER TABLE timelapse.timelapse_image_stages
        DROP CONSTRAINT IF EXISTS ck_timelapse_image_stage_status;
    ALTER TABLE timelapse.timelapse_image_stages
        ADD CONSTRAINT ck_timelapse_image_stage_status
        CHECK (status IN ('WAITING','RENDERING','COMPLETED','FAILED','INVALIDATED','CANCELLED'));

    ALTER TABLE timelapse.timelapse_image_stage_versions
        DROP CONSTRAINT IF EXISTS ck_timelapse_image_stage_version_status;
    ALTER TABLE timelapse.timelapse_image_stage_versions
        ADD CONSTRAINT ck_timelapse_image_stage_version_status
        CHECK (status IN ('WAITING','RENDERING','COMPLETED','FAILED','INVALIDATED','CANCELLED'));

    ALTER TABLE timelapse.timelapse_video_clips
        DROP CONSTRAINT IF EXISTS ck_timelapse_video_clip_status;
    ALTER TABLE timelapse.timelapse_video_clips
        ADD CONSTRAINT ck_timelapse_video_clip_status
        CHECK (status IN ('WAITING','RENDERING','COMPLETED','FAILED','INVALIDATED','CANCELLED'));

    ALTER TABLE timelapse.timelapse_video_clip_versions
        DROP CONSTRAINT IF EXISTS ck_timelapse_video_clip_version_status;
    ALTER TABLE timelapse.timelapse_video_clip_versions
        ADD CONSTRAINT ck_timelapse_video_clip_version_status
        CHECK (status IN ('WAITING','RENDERING','COMPLETED','FAILED','INVALIDATED','CANCELLED'));

    ALTER TABLE timelapse.timelapse_final_outputs
        DROP CONSTRAINT IF EXISTS ck_timelapse_final_output_status;
    ALTER TABLE timelapse.timelapse_final_outputs
        ADD CONSTRAINT ck_timelapse_final_output_status
        CHECK (status IN ('WAITING','RENDERING','COMPLETED','FAILED','INVALIDATED','CANCELLED'));
END $$;
