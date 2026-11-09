DO $$
DECLARE mode text;
BEGIN
    SELECT s.execution_mode INTO mode
      FROM video_render.rvideo_job_settings s
      JOIN video_render.video_projects p ON p.id = s.project_id AND p.tenant_id = s.tenant_id
     WHERE p.core_job_id = '0c493aaf-5491-4b86-a1e3-429b66205535';
    IF mode IS DISTINCT FROM 'AUTO' THEN
        RAISE EXCEPTION 'Job 0c493aaf is not in AUTO mode (mode=%)', mode;
    END IF;
    RAISE NOTICE 'Job 0c493aaf execution_mode=%', mode;
END $$;
