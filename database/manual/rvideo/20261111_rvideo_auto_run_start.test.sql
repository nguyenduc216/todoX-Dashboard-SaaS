DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'video_render'
                   AND table_name = 'rvideo_job_settings' AND column_name = 'auto_run_started_at') THEN
        RAISE EXCEPTION 'rvideo_job_settings.auto_run_started_at is missing';
    END IF;
    IF EXISTS (SELECT 1 FROM video_render.rvideo_job_settings s
                WHERE s.execution_mode = 'AUTO' AND s.auto_run_started_at IS NULL
                  AND EXISTS (SELECT 1 FROM video_render.video_project_scenes sc WHERE sc.project_id = s.project_id)) THEN
        RAISE EXCEPTION 'an AUTO job with scenes would stop running';
    END IF;
END $$;
