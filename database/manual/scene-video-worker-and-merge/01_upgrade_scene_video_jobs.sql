\set ON_ERROR_STOP on

-- No schema change is required for render job topology in this repository revision.
-- Runtime now introduces job types:
--   render_video_job      -> batch/orchestrator
--   render_scene_video    -> scene worker
--   merge_video_job       -> merge worker
-- Keep this file for manual rollout traceability.

SELECT 'NO_SCHEMA_CHANGE' section,
       'render job topology handled in application runtime' AS note;
