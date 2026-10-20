-- Read-only verification and reconciliation inventory; never automatically retry these rows.
SELECT to_regclass('render.scene_video_submit_intents') AS submit_intents;
SELECT conname, contype, pg_get_constraintdef(oid) AS definition
FROM pg_constraint WHERE conrelid = 'render.scene_video_submit_intents'::regclass
ORDER BY conname;
SELECT submit_state, count(*) AS rows FROM render.scene_video_submit_intents GROUP BY submit_state;