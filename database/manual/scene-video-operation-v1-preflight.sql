BEGIN READ ONLY;
SELECT current_database(),version(),current_user;
SELECT name,to_regclass(name) AS relation FROM unnest(ARRAY[
 'video_render.video_projects','video_render.video_project_scenes',
 'video_render.scene_video_versions','video_render.final_video_versions','render.render_jobs','render.scene_video_submit_intents',
 'video_render.scene_video_operation_heads','video_render.scene_video_operation_transitions']) name;
SELECT c.relname,a.attname,format_type(a.atttypid,a.atttypmod),a.attnotnull,a.attidentity,
 pg_get_expr(d.adbin,d.adrelid) AS default_expr
FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
JOIN pg_attribute a ON a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped
LEFT JOIN pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum
WHERE (n.nspname='render' AND c.relname='render_jobs')
 OR (n.nspname='video_render' AND c.relname IN('video_projects','video_project_scenes','scene_video_versions'))
ORDER BY n.nspname,c.relname,a.attnum;
SELECT n.nspname,c.relname,k.conname,pg_get_constraintdef(k.oid) AS definition
FROM pg_constraint k JOIN pg_class c ON c.oid=k.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE (n.nspname='render' AND c.relname IN('render_jobs','scene_video_submit_intents'))
 OR (n.nspname='video_render' AND c.relname IN('scene_video_versions','video_project_scenes','video_projects','final_video_versions'));
SELECT n.nspname,c.relname,has_table_privilege(c.oid,'SELECT') AS can_select,has_table_privilege(c.oid,'INSERT') AS can_insert,
 has_table_privilege(c.oid,'UPDATE') AS can_update,
 row_security_active(c.oid) AS row_security_active
FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE c.relkind='r' AND ((n.nspname='render' AND c.relname IN('render_jobs','scene_video_submit_intents'))
 OR (n.nspname='video_render' AND c.relname IN('scene_video_versions','video_project_scenes','video_projects','final_video_versions')));
SELECT n.nspname,c.relname,t.tgname,pg_get_triggerdef(t.oid) AS definition
FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE NOT t.tgisinternal AND n.nspname IN('render','video_render');
-- Require draft/queued/cancelled render_jobs statuses, nullable source_image_version_id,
-- actual FK types, generation functions/tables absent before migration, and every required table above present.
-- Review original FK/check/grants export too. New tables must be absent before forward migration.
COMMIT;
