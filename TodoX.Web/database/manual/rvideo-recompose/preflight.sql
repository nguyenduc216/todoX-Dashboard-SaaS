\set ON_ERROR_STOP on
-- Read-only. Safe to run twice; no CREATE/ALTER/INSERT/UPDATE/DELETE or backfill.
BEGIN TRANSACTION READ ONLY;
SET LOCAL statement_timeout='60s';
SELECT n.nspname AS schema_name,c.relname AS table_name,a.attname AS column_name,format_type(a.atttypid,a.atttypmod) AS data_type,a.attnotnull
FROM pg_attribute a JOIN pg_class c ON c.oid=a.attrelid JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE n.nspname IN ('video_render','system') AND c.relname IN
('app_settings','video_projects','video_project_scenes','scene_video_versions','final_video_versions','final_video_version_items')
AND a.attnum>0 AND NOT a.attisdropped ORDER BY n.nspname,c.relname,a.attnum;
SELECT schemaname,tablename,indexname,indexdef FROM pg_indexes WHERE schemaname='video_render'
AND tablename IN ('final_video_versions','final_video_version_items') ORDER BY tablename,indexname;
SELECT conrelid::regclass AS table_name,conname,contype,convalidated,pg_get_constraintdef(oid) AS definition
FROM pg_constraint WHERE conrelid IN (to_regclass('video_render.video_projects'),to_regclass('video_render.final_video_versions'),to_regclass('video_render.final_video_version_items'))
ORDER BY conrelid,conname;
ROLLBACK;
\ir verify.sql
