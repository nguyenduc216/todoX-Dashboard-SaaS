\set ON_ERROR_STOP on

-- The core unique constraints/indexes come from scene-media-versioning.
-- Re-run idempotent guards here for rollout convenience.

CREATE UNIQUE INDEX IF NOT EXISTS ux_scene_video_one_selected
    ON video_render.scene_video_versions(scene_id)
    WHERE is_selected;

CREATE UNIQUE INDEX IF NOT EXISTS ux_final_video_one_selected
    ON video_render.final_video_versions(project_id)
    WHERE is_selected;

CREATE INDEX IF NOT EXISTS ix_scene_video_logical_billing
    ON video_render.scene_video_versions(billing_logical_request_id);

CREATE INDEX IF NOT EXISTS ix_final_video_items_source
    ON video_render.final_video_version_items(scene_video_version_id);
