DO $$
DECLARE remaining int; archived int;
BEGIN
    -- No repeated state line is left in the window except the first and last of each run.
    WITH s AS (
      SELECT id, project_id, event_type, created_at, (data_json - 'jobId' - 'activeJobId' - 'renderJobId' - '_sig')::text AS content
        FROM video_render.video_project_events
       WHERE created_at >= '2026-11-10 00:00:00+07' AND created_at < '2026-11-11 13:30:00+07'
         AND event_type IN ('SCENE_VIDEO_AUTO_ENQUEUE_SKIPPED','SCENE_AUDIO_AUTO_ENQUEUE_SKIPPED','SCENE_AUDIO_AUTO_ENQUEUE_FAILED',
                            'PROJECT_FINAL_MERGE_NOT_READY','RVIDEO_AUTO_VALIDATION_FAILED')
    ), r AS (
      SELECT row_number() OVER (PARTITION BY project_id, event_type, content ORDER BY created_at, id) AS a,
             row_number() OVER (PARTITION BY project_id, event_type, content ORDER BY created_at DESC, id DESC) AS d
        FROM s)
    SELECT count(*) INTO remaining FROM r WHERE a > 1 AND d > 1;
    IF remaining > 0 THEN RAISE EXCEPTION 'repeated lifecycle lines remain: %', remaining; END IF;

    SELECT count(*) INTO remaining FROM video_render.video_project_events
     WHERE project_id = 226 AND created_at > '2026-11-11 11:42:00+07' AND created_at < '2026-11-11 13:18:00+07'
       AND event_type IN ('PROJECT_MERGE_AUTO_ENQUEUED','PROJECT_MERGED','PROJECT_MERGE_VALIDATED','PROJECT_MERGE_TRANSCODE_FALLBACK_STARTED');
    IF remaining > 0 THEN RAISE EXCEPTION 'project 226 merge loop lines remain: %', remaining; END IF;

    -- Everything removed is restorable from the archive.
    SELECT count(*) INTO archived FROM video_render.video_project_events_purged;
    IF archived = 0 THEN RAISE EXCEPTION 'nothing was archived'; END IF;
    RAISE NOTICE 'archived rows: %', archived;
END $$;
