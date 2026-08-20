-- Reconcile runtime schema for video_render.scene_video_versions.
-- Additive and idempotent. Do not drop, recreate, or delete data.

ALTER TABLE video_render.scene_video_versions
ADD COLUMN IF NOT EXISTS provider_capability_id bigint;

CREATE INDEX IF NOT EXISTS ix_scene_video_versions_provider_capability_id
ON video_render.scene_video_versions(provider_capability_id);
