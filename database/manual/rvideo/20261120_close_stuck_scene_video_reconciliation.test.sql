DO $$
DECLARE n int;
BEGIN
  SELECT count(*) INTO n FROM video_render.scene_video_versions
   WHERE status = 'pending_reconciliation' AND updated_at < '2026-11-01'
     AND NOT video_render.scene_video_job_is_superseded(tenant_id, render_job_id);
  IF n <> 0 THEN RAISE EXCEPTION 'still % stuck current versions', n; END IF;

  SELECT count(*) INTO n FROM video_render.scene_video_versions WHERE error_code = 'STUCK_RECONCILIATION_CLOSED' AND status = 'failed';
  IF n <> 35 THEN RAISE EXCEPTION 'expected 35 closed versions, got %', n; END IF;

  SELECT count(*) INTO n FROM render.render_jobs WHERE error_code = 'STUCK_RECONCILIATION_CLOSED' AND status = 'failed';
  IF n <> 2 THEN RAISE EXCEPTION 'expected 2 closed jobs, got %', n; END IF;

  SELECT count(*) INTO n FROM billing.ai_image_billing_records b
    JOIN video_render.scene_video_versions v ON v.logical_request_id = b.logical_request_id
   WHERE v.error_code = 'STUCK_RECONCILIATION_CLOSED' AND b.status IN ('pending_reconciliation', 'manual_review');
  IF n <> 0 THEN RAISE EXCEPTION '% billing holds not released', n; END IF;

  SELECT count(*) INTO n FROM video_render.video_project_scenes sc
    JOIN video_render.scene_video_versions v ON v.scene_id = sc.id
   WHERE v.error_code = 'STUCK_RECONCILIATION_CLOSED' AND sc.status = 'video_rendering';
  IF n <> 0 THEN RAISE EXCEPTION '% scenes still shown rendering', n; END IF;
END $$;
