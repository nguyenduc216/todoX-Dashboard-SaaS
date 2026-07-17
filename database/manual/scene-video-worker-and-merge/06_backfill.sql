\set ON_ERROR_STOP on

-- No automatic data backfill is required for the YEScale scene-video worker rollout.
-- Existing scene/final version rows remain valid.

SELECT 'NO_BACKFILL_REQUIRED' section, 'Existing scene-media versioning data is preserved.' AS note;
