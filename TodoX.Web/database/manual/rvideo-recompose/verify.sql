\set ON_ERROR_STOP on
-- Operator uses an existing read-only pg_service. Does not enable flags or modify data.
BEGIN TRANSACTION READ ONLY;
SET LOCAL statement_timeout='60s';
SELECT (
WITH required(table_name, columns) AS (VALUES
  ('system.app_settings','setting_key,setting_value,is_active'),
  ('video_render.video_projects','id,tenant_id,selected_final_video_version_id,final_video_url,final_video_path,status,error_message,updated_at'),
  ('video_render.video_project_scenes','id,project_id,tenant_id,scene_index,duration_seconds,selected_video_version_id'),
  ('video_render.scene_video_versions','id,project_id,scene_id,tenant_id,status'),
  ('video_render.final_video_versions','id,project_id,tenant_id,customer_id,created_by,version_number,logical_request_id,render_job_id,composition_config_json,transition_config_json,audio_config_json,subtitle_config_json,storage_key,status,created_at,updated_at,is_selected,selected_at,selected_by,completed_at,public_url,source_file_path,poster_url,duration_seconds,mime_type,error_code,error_message'),
  ('video_render.final_video_version_items','id,final_video_version_id,scene_id,scene_video_version_id,item_order,config_json')),
missing AS (
  SELECT r.table_name,c.name FROM required r CROSS JOIN LATERAL unnest(string_to_array(r.columns,',')) c(name)
  WHERE NOT EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid=to_regclass(r.table_name) AND a.attname=c.name AND a.attnum>0 AND NOT a.attisdropped)),
indexes(table_name,index_name,key_columns,partial) AS (VALUES
  ('video_render.final_video_versions','ux_final_video_one_selected','project_id',true),
  ('video_render.final_video_versions','ux_final_video_version_number','project_id,version_number',false),
  ('video_render.final_video_versions','ux_final_video_logical_request','logical_request_id',false),
  ('video_render.final_video_version_items','ux_final_video_item_order','final_video_version_id,item_order',false),
  ('video_render.final_video_version_items','ux_final_video_item_scene','final_video_version_id,scene_id',false)),
missing_indexes AS (
  SELECT x.index_name FROM indexes x WHERE NOT EXISTS (
    SELECT 1 FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid
    WHERE i.indrelid=to_regclass(x.table_name) AND c.relname=x.index_name AND i.indisunique AND i.indisvalid
      AND (SELECT string_agg(a.attname,',' ORDER BY k.ordinality)
        FROM unnest(i.indkey) WITH ORDINALITY k(attnum,ordinality)
        JOIN pg_attribute a ON a.attrelid=i.indrelid AND a.attnum=k.attnum)=x.key_columns
      AND CASE WHEN x.partial THEN pg_get_expr(i.indpred,i.indrelid)='is_selected' ELSE i.indpred IS NULL END)),
missing_fks AS (
  SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid=to_regclass('video_render.video_projects')
    AND conname='fk_project_selected_final_video_version' AND contype='f' AND convalidated)
  UNION ALL SELECT 1 WHERE (SELECT count(*) FROM pg_constraint
    WHERE conrelid=to_regclass('video_render.final_video_version_items') AND contype='f' AND convalidated)<3
  UNION ALL SELECT 1 WHERE (SELECT count(*) FROM pg_constraint
    WHERE conrelid=to_regclass('video_render.final_video_versions') AND contype='f' AND convalidated)<2)
SELECT NOT EXISTS(SELECT 1 FROM missing) AND NOT EXISTS(SELECT 1 FROM missing_indexes) AND NOT EXISTS(SELECT 1 FROM missing_fks)) AS schema_ready \gset
\echo schema_ready=:schema_ready
\if :schema_ready
SELECT setting_key,setting_value,is_active FROM system.app_settings
WHERE setting_key='features.final_video_versioning';
SELECT 'legacy_final_without_selected_version_supported' AS check_name,p.id,p.tenant_id,p.status,p.final_video_url,p.final_video_path
FROM video_render.video_projects p WHERE p.selected_final_video_version_id IS NULL
AND (NULLIF(p.final_video_url,'') IS NOT NULL OR NULLIF(p.final_video_path,'') IS NOT NULL);
SELECT 'invalid_selected_final' AS check_name,p.id,p.tenant_id,p.selected_final_video_version_id
FROM video_render.video_projects p LEFT JOIN video_render.final_video_versions f ON f.id=p.selected_final_video_version_id
WHERE p.selected_final_video_version_id IS NOT NULL AND
(f.id IS NULL OR f.tenant_id<>p.tenant_id OR f.project_id<>p.id OR f.status<>'completed' OR NOT f.is_selected
 OR p.final_video_url IS DISTINCT FROM f.public_url OR p.final_video_path IS DISTINCT FROM f.source_file_path);
SELECT 'selected_final_pointer_disagrees' AS check_name,f.id,f.project_id FROM video_render.final_video_versions f
LEFT JOIN video_render.video_projects p ON p.id=f.project_id AND p.tenant_id=f.tenant_id
WHERE f.is_selected AND (p.id IS NULL OR p.selected_final_video_version_id IS DISTINCT FROM f.id);
SELECT 'scene_needs_data_repair_no_automatic_render' AS check_name,s.id,s.project_id,s.selected_video_version_id
FROM video_render.video_project_scenes s LEFT JOIN video_render.scene_video_versions v ON v.id=s.selected_video_version_id
WHERE v.id IS NULL OR v.tenant_id<>s.tenant_id OR v.project_id<>s.project_id OR v.scene_id<>s.id OR v.status<>'completed';
SELECT 'orphan_or_cross_project_composition_item' AS check_name,i.id FROM video_render.final_video_version_items i
LEFT JOIN video_render.final_video_versions f ON f.id=i.final_video_version_id
LEFT JOIN video_render.video_project_scenes s ON s.id=i.scene_id
LEFT JOIN video_render.scene_video_versions v ON v.id=i.scene_video_version_id
WHERE f.id IS NULL OR s.id IS NULL OR v.id IS NULL OR s.project_id<>f.project_id OR v.project_id<>f.project_id
OR v.scene_id<>s.id OR s.tenant_id<>f.tenant_id OR v.tenant_id<>f.tenant_id;
SELECT 'final_without_items' AS check_name,f.id,f.status FROM video_render.final_video_versions f
WHERE NOT EXISTS(SELECT 1 FROM video_render.final_video_version_items i WHERE i.final_video_version_id=f.id);
SELECT 'queued_final_without_render_job' AS check_name,f.id FROM video_render.final_video_versions f
LEFT JOIN render.render_jobs r ON r.id=f.render_job_id AND r.tenant_id=f.tenant_id
WHERE f.status IN ('queued','processing') AND r.id IS NULL;
SELECT 'duplicate_active_merge' AS check_name,tenant_id,log_code,count(*) FROM render.render_jobs
WHERE job_type='merge_video_job' AND status IN ('queued','preparing','rendering','post_processing','pending_reconciliation')
GROUP BY tenant_id,log_code HAVING count(*)>1;
\else
\echo 'Schema prerequisites are incomplete; data verification/enablement is blocked. Review preflight output.'
\endif
ROLLBACK;
