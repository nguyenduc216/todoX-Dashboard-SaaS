-- Safe only BEFORE any preparation. Never discard transitions or reverse generations.
BEGIN;
LOCK TABLE video_render.scene_video_operation_heads, video_render.scene_video_operation_transitions IN ACCESS EXCLUSIVE MODE;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM video_render.scene_video_operation_transitions)
    OR EXISTS(SELECT 1 FROM video_render.scene_video_operation_heads) THEN
  RAISE EXCEPTION 'Rollback refused: durable operation history exists; retain tables and use a reviewed forward repair';
 END IF;
END $$;
DROP TRIGGER IF EXISTS scene_video_version_generation_fence ON video_render.scene_video_versions;
DROP TRIGGER IF EXISTS scene_video_job_generation_fence ON render.render_jobs;
DROP TRIGGER IF EXISTS scene_video_scene_generation_fence ON video_render.video_project_scenes;
DROP TRIGGER IF EXISTS scene_video_project_generation_fence ON video_render.video_projects;
DO $$ BEGIN
 IF to_regclass('video_render.final_video_versions') IS NOT NULL THEN
  DROP TRIGGER IF EXISTS scene_video_final_generation_fence ON video_render.final_video_versions;
 END IF;
END $$;
DROP FUNCTION video_render.fence_video_operation_write();
DROP FUNCTION video_render.assert_video_operation_current(uuid,uuid);
DROP FUNCTION video_render.scene_video_job_is_current(uuid,uuid);
DROP FUNCTION video_render.scene_video_job_is_superseded(uuid,uuid);
DROP TABLE video_render.scene_video_operation_transitions;
DROP TABLE video_render.scene_video_operation_heads;
COMMIT;
