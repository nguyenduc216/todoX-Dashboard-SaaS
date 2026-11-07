-- Read-only preflight. Do not continue unless this file completes successfully.
\set ON_ERROR_STOP on

DO $$
DECLARE missing text; bad_types text;
BEGIN
  SELECT string_agg(x.name, ', ') INTO missing
  FROM (VALUES
    ('video_render.video_projects'),
    ('video_render.video_project_scenes'),
    ('render.render_jobs')
  ) x(name) WHERE to_regclass(x.name) IS NULL;
  IF missing IS NOT NULL THEN RAISE EXCEPTION 'Missing required objects: %', missing; END IF;

  SELECT string_agg(format('%s.%s=%s', table_name,column_name,data_type), ', ') INTO bad_types
  FROM information_schema.columns
  WHERE table_schema='video_render'
    AND ((table_name='video_projects' AND column_name='id' AND data_type<>'bigint')
      OR (table_name='video_project_scenes' AND column_name IN ('id','project_id') AND data_type<>'bigint'));
  IF bad_types IS NOT NULL THEN RAISE EXCEPTION 'Unexpected key types: %', bad_types; END IF;
END $$;

SELECT 'DATABASE' section, current_database() value, 'PASS' status
UNION ALL SELECT 'PROJECTS', count(*)::text, 'INFO' FROM video_render.video_projects
UNION ALL SELECT 'SCENES', count(*)::text, 'INFO' FROM video_render.video_project_scenes
UNION ALL SELECT 'SCENES_WITH_IMAGE', count(*)::text, 'INFO' FROM video_render.video_project_scenes WHERE COALESCE(static_image_path,static_image_url) IS NOT NULL
UNION ALL SELECT 'SCENES_WITH_VIDEO', count(*)::text, 'INFO' FROM video_render.video_project_scenes WHERE COALESCE(scene_video_path,scene_video_url) IS NOT NULL
UNION ALL SELECT 'PROJECTS_WITH_FINAL_VIDEO', count(*)::text, 'INFO' FROM video_render.video_projects WHERE COALESCE(final_video_path,final_video_url) IS NOT NULL;

SELECT table_name,column_name,data_type,is_nullable
FROM information_schema.columns
WHERE table_schema='video_render'
  AND table_name IN ('video_projects','video_project_scenes')
ORDER BY table_name,ordinal_position;

