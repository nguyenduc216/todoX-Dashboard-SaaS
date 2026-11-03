\set ON_ERROR_STOP on
-- Run with an operator-owned READ ONLY role/service. No credentials in this file.
-- psql service=... -X -v job_uuid=464639f5-16ee-4953-80c1-2050fcadb3fb -f rvideo-final-evidence.sql
BEGIN TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '60s';
WITH target AS (
    SELECT * FROM render.render_jobs WHERE id = :'job_uuid'::uuid
), projects AS (
    SELECT DISTINCT p.* FROM video_render.video_projects p
    JOIN target j ON p.tenant_id=j.tenant_id
    WHERE p.core_job_id=j.id OR p.id::text=j.input_json->>'projectId'
       OR EXISTS (SELECT 1 FROM video_render.final_video_versions f WHERE f.project_id=p.id AND f.tenant_id=p.tenant_id AND f.render_job_id=j.id)
       OR EXISTS (SELECT 1 FROM video_render.scene_video_versions v WHERE v.project_id=p.id AND v.tenant_id=p.tenant_id AND v.render_job_id=j.id)
), jobs AS (
    SELECT DISTINCT j.* FROM render.render_jobs j JOIN projects p ON j.tenant_id=p.tenant_id
    WHERE j.id=p.core_job_id OR j.input_json->>'projectId'=p.id::text
       OR EXISTS (SELECT 1 FROM video_render.final_video_versions f WHERE f.project_id=p.id AND f.tenant_id=p.tenant_id AND f.render_job_id=j.id)
       OR EXISTS (SELECT 1 FROM video_render.scene_video_versions v WHERE v.project_id=p.id AND v.tenant_id=p.tenant_id AND v.render_job_id=j.id)
)
SELECT 'target_job' AS kind, to_jsonb(t) AS evidence FROM target t
UNION ALL SELECT 'project',to_jsonb(p) FROM projects p
UNION ALL SELECT 'scene',to_jsonb(s) FROM video_render.video_project_scenes s JOIN projects p ON s.project_id=p.id AND s.tenant_id=p.tenant_id
UNION ALL SELECT 'scene_video_version',to_jsonb(v) FROM video_render.scene_video_versions v JOIN projects p ON v.project_id=p.id AND v.tenant_id=p.tenant_id
UNION ALL SELECT 'scene_audio_version',to_jsonb(v) FROM video_render.scene_audio_versions v JOIN projects p ON v.project_id=p.id AND v.tenant_id=p.tenant_id
UNION ALL SELECT 'final_video_version',to_jsonb(f) FROM video_render.final_video_versions f JOIN projects p ON f.project_id=p.id AND f.tenant_id=p.tenant_id
UNION ALL SELECT 'composition_item',to_jsonb(i) FROM video_render.final_video_version_items i JOIN video_render.final_video_versions f ON f.id=i.final_video_version_id JOIN projects p ON f.project_id=p.id AND f.tenant_id=p.tenant_id
UNION ALL SELECT 'render_job',to_jsonb(j) FROM jobs j
UNION ALL SELECT 'project_event',to_jsonb(e) FROM video_render.video_project_events e JOIN projects p ON e.project_id=p.id AND e.tenant_id=p.tenant_id
UNION ALL SELECT 'render_event',to_jsonb(e) FROM render.render_job_events e JOIN jobs j ON e.job_id=j.id AND e.tenant_id=j.tenant_id;
ROLLBACK;
