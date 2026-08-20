-- RVIDEO runtime structure export.
-- Read-only metadata queries. Execute manually in the target database if needed.
-- This script never changes schema or data.

-- 1. Tables and views used by the RVIDEO dashboard/runtime.
SELECT table_schema,
       table_name,
       table_type
  FROM information_schema.tables
 WHERE (table_schema, table_name) IN (
       ('render', 'render_jobs'),
       ('render', 'render_job_events'),
       ('video_render', 'video_projects'),
       ('video_render', 'video_project_scenes'),
       ('video_render', 'rvideo_job_settings'),
       ('video_render', 'scene_image_versions'),
       ('video_render', 'scene_video_versions'),
       ('video_render', 'final_video_versions'),
       ('video_render', 'final_video_version_items'),
       ('billing', 'ai_billing_records'),
       ('billing', 'ai_image_billing_records'),
       ('public', 'todox_ai_provider_usage_log')
 )
 ORDER BY table_schema, table_name;

-- 2. Columns, types, nullability and defaults.
SELECT c.table_schema,
       c.table_name,
       c.ordinal_position,
       c.column_name,
       c.data_type,
       c.udt_schema,
       c.udt_name,
       c.character_maximum_length,
       c.numeric_precision,
       c.numeric_scale,
       c.is_nullable,
       c.column_default
  FROM information_schema.columns c
 WHERE (c.table_schema, c.table_name) IN (
       ('render', 'render_jobs'),
       ('render', 'render_job_events'),
       ('video_render', 'video_projects'),
       ('video_render', 'video_project_scenes'),
       ('video_render', 'rvideo_job_settings'),
       ('video_render', 'scene_image_versions'),
       ('video_render', 'scene_video_versions'),
       ('video_render', 'final_video_versions'),
       ('video_render', 'final_video_version_items'),
       ('billing', 'ai_billing_records'),
       ('billing', 'ai_image_billing_records'),
       ('public', 'todox_ai_provider_usage_log')
 )
 ORDER BY c.table_schema, c.table_name, c.ordinal_position;

-- 3. Index definitions.
SELECT schemaname,
       tablename,
       indexname,
       indexdef
  FROM pg_indexes
 WHERE (schemaname, tablename) IN (
       ('render', 'render_jobs'),
       ('render', 'render_job_events'),
       ('video_render', 'video_projects'),
       ('video_render', 'video_project_scenes'),
       ('video_render', 'rvideo_job_settings'),
       ('video_render', 'scene_image_versions'),
       ('video_render', 'scene_video_versions'),
       ('video_render', 'final_video_versions'),
       ('video_render', 'final_video_version_items'),
       ('billing', 'ai_billing_records'),
       ('billing', 'ai_image_billing_records'),
       ('public', 'todox_ai_provider_usage_log')
 )
 ORDER BY schemaname, tablename, indexname;

-- 4. Primary/unique/foreign/check constraints.
SELECT tc.constraint_schema,
       tc.table_schema,
       tc.table_name,
       tc.constraint_name,
       tc.constraint_type,
       pg_get_constraintdef(cls.oid) AS constraint_definition
  FROM information_schema.table_constraints tc
  JOIN pg_namespace ns
    ON ns.nspname = tc.constraint_schema
  JOIN pg_class cls
    ON cls.relname = tc.constraint_name
   AND cls.relnamespace = ns.oid
 WHERE (tc.table_schema, tc.table_name) IN (
       ('render', 'render_jobs'),
       ('render', 'render_job_events'),
       ('video_render', 'video_projects'),
       ('video_render', 'video_project_scenes'),
       ('video_render', 'rvideo_job_settings'),
       ('video_render', 'scene_image_versions'),
       ('video_render', 'scene_video_versions'),
       ('video_render', 'final_video_versions'),
       ('video_render', 'final_video_version_items'),
       ('billing', 'ai_billing_records'),
       ('billing', 'ai_image_billing_records'),
       ('public', 'todox_ai_provider_usage_log')
 )
 ORDER BY tc.table_schema, tc.table_name, tc.constraint_name;
