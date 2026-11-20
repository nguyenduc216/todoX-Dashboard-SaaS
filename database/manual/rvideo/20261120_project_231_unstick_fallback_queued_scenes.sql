-- FEATURE_ID: TODOX-RVIDEO-VIDEO-RERENDER
-- Project 231 (job 9890fa94): at 2026-11-20 04:57-04:59 UTC the old model fallback marked all 7 scenes
-- 'video_queued' and enqueued fallback jobs that are not the current operation, so they are quarantined
-- and never run. The current jobs and versions of these scenes are already failed and their billing is
-- released, but the scene label keeps the UI on "Đang tạo video" and blocks re-render.
-- Only scenes with no live current job and no live version are touched.
UPDATE video_render.video_project_scenes sc
   SET status = 'failed',
       updated_at = now()
 WHERE sc.project_id = 231
   AND sc.status = 'video_queued'
   AND NOT EXISTS (SELECT 1 FROM render.render_jobs j
                    WHERE j.tenant_id = sc.tenant_id AND j.job_type = 'render_scene_video'
                      AND COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id') = sc.id::text
                      AND j.status NOT IN ('completed', 'failed', 'cancelled', 'draft')
                      AND video_render.scene_video_job_is_current(j.tenant_id, j.id))
   AND NOT EXISTS (SELECT 1 FROM video_render.scene_video_versions v
                    WHERE v.tenant_id = sc.tenant_id AND v.scene_id = sc.id
                      AND v.status NOT IN ('completed', 'failed'));
