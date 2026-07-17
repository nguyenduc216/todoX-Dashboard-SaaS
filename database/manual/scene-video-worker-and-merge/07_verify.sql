\set ON_ERROR_STOP on

SELECT 'JOB_TYPES' section, job_type, count(*) row_count
FROM render.render_jobs
WHERE job_type IN ('render_video_job','render_scene_video','merge_video_job')
GROUP BY job_type
ORDER BY job_type;

SELECT 'SCENE_VIDEO_DUPLICATE_LOGICAL_REQUEST' section, logical_request_id, count(*) row_count
FROM video_render.scene_video_versions
GROUP BY logical_request_id
HAVING count(*) > 1
ORDER BY logical_request_id;

SELECT 'SCENE_VIDEO_SELECTED_DUPLICATE' section, scene_id, count(*) row_count
FROM video_render.scene_video_versions
WHERE is_selected = true
GROUP BY scene_id
HAVING count(*) > 1;

SELECT 'FINAL_VIDEO_SELECTED_DUPLICATE' section, project_id, count(*) row_count
FROM video_render.final_video_versions
WHERE is_selected = true
GROUP BY project_id
HAVING count(*) > 1;

SELECT 'FINAL_ITEM_GAPS' section, count(*) invalid_item_count
FROM video_render.final_video_version_items item
LEFT JOIN video_render.scene_video_versions v ON v.id = item.scene_video_version_id
WHERE v.id IS NULL OR v.status <> 'completed';

SELECT 'SCENE_POINTER_GAPS' section, count(*) broken_pointer_count
FROM video_render.video_project_scenes s
LEFT JOIN video_render.scene_video_versions v ON v.id = s.selected_video_version_id
WHERE s.selected_video_version_id IS NOT NULL
  AND (v.id IS NULL OR v.scene_id <> s.id OR v.project_id <> s.project_id);
