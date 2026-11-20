DO $$
DECLARE n int;
BEGIN
  SELECT count(*) INTO n FROM render.render_jobs
   WHERE id IN ('6b3c8080-6031-4ff6-8864-454cc0cf5a7a','7f24d794-d25a-45af-a4b6-1f4bce27e496','9ff53c25-9b6d-468d-8c1d-cce83f2be150')
     AND status = 'pending_reconciliation' AND error_code = 'SCENE_VIDEO_SHUTDOWN_RETAINED'
     AND cancelled_at IS NULL AND lock_owner IS NULL;
  IF n <> 3 THEN RAISE EXCEPTION 'expected 3 re-armed jobs, got %', n; END IF;

  SELECT count(*) INTO n FROM render.render_job_events
   WHERE event_type = 'JOB_REARMED_AFTER_SHUTDOWN_CANCEL'
     AND job_id IN ('6b3c8080-6031-4ff6-8864-454cc0cf5a7a','7f24d794-d25a-45af-a4b6-1f4bce27e496','9ff53c25-9b6d-468d-8c1d-cce83f2be150');
  IF n <> 3 THEN RAISE EXCEPTION 'expected 3 events, got %', n; END IF;

  -- Nothing else touched: the other four scenes of project 231 stay failed.
  SELECT count(*) INTO n FROM video_render.scene_video_versions WHERE project_id = 231 AND status = 'failed';
  IF n <> 4 THEN RAISE EXCEPTION 'expected the 4 failed scenes unchanged, got %', n; END IF;
END $$;
