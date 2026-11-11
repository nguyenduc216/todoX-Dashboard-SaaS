-- Purge meaningless repeated RVideo background log lines (user request 2026-11-11), 2026-11-10 00:00 .. 2026-11-11 13:30 (+07).
-- 1) Lifecycle "state" events re-written on every pass: for each run of identical content (per project, event type,
--    content without per-pass ids) keep the first and the last line, purge the rest.
-- 2) Job 71fe1a13 (project 226) final-video merge loop 11:42-13:18: same selection merged ~218 times; keep first/last cycles.
-- Rows are MOVED to video_render.video_project_events_purged (restorable), not destroyed. Additive: new table only.

CREATE TABLE IF NOT EXISTS video_render.video_project_events_purged
    (LIKE video_render.video_project_events INCLUDING DEFAULTS);
ALTER TABLE video_render.video_project_events_purged ADD COLUMN IF NOT EXISTS purged_at timestamptz NOT NULL DEFAULT now();
ALTER TABLE video_render.video_project_events_purged ADD COLUMN IF NOT EXISTS purge_reason text;

CREATE TEMP TABLE purge_ids ON COMMIT DROP AS
WITH s AS (
  SELECT id, project_id, event_type, created_at,
         (data_json - 'jobId' - 'activeJobId' - 'renderJobId' - '_sig')::text AS content
    FROM video_render.video_project_events
   WHERE created_at >= '2026-11-10 00:00:00+07' AND created_at < '2026-11-11 13:30:00+07'
     AND event_type IN ('SCENE_VIDEO_AUTO_ENQUEUE_SKIPPED','SCENE_AUDIO_AUTO_ENQUEUE_SKIPPED','SCENE_AUDIO_AUTO_ENQUEUE_FAILED',
                        'PROJECT_FINAL_MERGE_NOT_READY','RVIDEO_AUTO_VALIDATION_FAILED','SCENE_IMAGE_REFERENCE_VALIDATION_FAILED',
                        'RVIDEO_IMAGE_REFERENCE_VALIDATION_FAILED')
), r AS (
  SELECT id,
         row_number() OVER (PARTITION BY project_id, event_type, content ORDER BY created_at, id) AS rn_asc,
         row_number() OVER (PARTITION BY project_id, event_type, content ORDER BY created_at DESC, id DESC) AS rn_desc
    FROM s
)
SELECT id, 'repeated_lifecycle_state'::text AS reason FROM r WHERE rn_asc > 1 AND rn_desc > 1
UNION ALL
SELECT id, 'project_226_merge_loop' FROM video_render.video_project_events
 WHERE project_id = 226
   AND created_at > '2026-11-11 11:42:00+07' AND created_at < '2026-11-11 13:18:00+07'
   AND event_type IN ('PROJECT_MERGE_AUTO_ENQUEUED','PROJECT_MERGED','PROJECT_MERGE_VALIDATED','PROJECT_MERGE_TRANSCODE_FALLBACK_STARTED');

INSERT INTO video_render.video_project_events_purged
SELECT e.*, now(), p.reason
  FROM video_render.video_project_events e JOIN purge_ids p ON p.id = e.id
ON CONFLICT DO NOTHING;

DELETE FROM video_render.video_project_events e USING purge_ids p WHERE e.id = p.id;
