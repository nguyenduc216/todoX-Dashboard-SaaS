-- Customer request (2026-11-09): resume automatic processing of job 0c493aaf-5491-4b86-a1e3-429b66205535 (project 223).
-- AUTO = "Tự động thực hiện": the lifecycle worker links, renders and merges the remaining scenes. Data only, idempotent.
UPDATE video_render.rvideo_job_settings s
   SET execution_mode = 'AUTO', updated_at = now()
  FROM video_render.video_projects p
 WHERE p.id = s.project_id
   AND p.tenant_id = s.tenant_id
   AND p.core_job_id = '0c493aaf-5491-4b86-a1e3-429b66205535'
   AND s.execution_mode <> 'AUTO';
